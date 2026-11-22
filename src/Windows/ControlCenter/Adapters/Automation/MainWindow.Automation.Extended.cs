using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Automation;
using LumaWall.Contracts;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static object AutomationTaskbarVisual(TaskbarVisualOptions v)=>new {transparentOpacity=v.TransparentOpacity,frostedOpacity=v.FrostedOpacity,blur=v.Blur,color=v.Color,showBorder=v.ShowBorder};
    private readonly OperationJournal automationJournal=new();
    private readonly Dictionary<string,LibraryStore.ConfigurationSnapshot> automationCheckpoints=new();
    private bool automationRestartScheduled;
    private void ScheduleRestart() {
        if(automationRestartScheduled)return;
        if(importLifetime.Active!=null||automationJobs.Values.Any(j=>j.FinishedAt==null))throw new OperationFault("invalid_state",UiLanguage.Text("请先完成或取消导入任务"),null);
        FlushAutomationConfiguration();
        string launcher=Path.Combine(AppContext.BaseDirectory,"ZZZwallpaper.Launcher.exe");if(!File.Exists(launcher))throw new IOException(UiLanguage.Text("缺少本安装的启动器"));
        var start=new System.Diagnostics.ProcessStartInfo(launcher) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--automation-restart-parent");start.ArgumentList.Add(Environment.ProcessId.ToString());start.ArgumentList.Add("--data-root");start.ArgumentList.Add(store.Root);
        using var child=System.Diagnostics.Process.Start(start)??throw new IOException(UiLanguage.Text("启动器无法启动"));
        automationRestartScheduled=true;
        var timer=new Microsoft.UI.Xaml.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=> {timer.Stop();quit?.Invoke();};timer.Start();
    }
    private void FlushAutomationConfiguration() {
        FlushHistoryEdits();
        if(effectSession.Pending!=null||widgets?.HasPendingSave==true||pendingHomePosition!=null||startupSaveFailed)throw new IOException(UiLanguage.Text("存在尚未成功保存的界面修改；请处理保存错误后重试"));
        store.FlushConfiguration();
    }
    private static bool Flag(JsonElement a,string key)=>a.TryGetProperty(key,out var value)&&value.GetBoolean();
    private static string? Optional(JsonElement a,string key)=>a.TryGetProperty(key,out var value)?value.GetString():null;
    private static string[] Ids(JsonElement a,string key)=>a.TryGetProperty(key,out var value)?value.EnumerateArray().Select(v=>v.GetString()!).ToArray():[];
    private async Task<OperationReply> ExecuteRecordedAutomation(OperationRequest request,CancellationToken token)
    {
        var tool=ToolCatalog.Validate(request.Name,request.Arguments);
        if(tool.ReadOnly)return await ExecuteAutomation(request,token);
        if(automationRestartScheduled)throw new OperationFault("invalid_state","重启已安排，请重连后操作",null);
        string operationId=automationJournal.Begin(request,out var replay);if(replay!=null)return replay;
        using var trace=OperationTrace.Enter(automationJournal.Context(operationId));
        store.AutomationOperationId=operationId;
        string before=store.ConfigurationRevision();
        try {
            if(automationJobs.Values.Any(j=>j.FinishedAt==null)&&request.Name is "history_restore_checkpoint" or "app_restart" or "config_commit_import" or "media_remove" or "history_undo" or "history_redo")
                throw new OperationFault("invalid_state","素材导入任务尚未结束；请等待完成，或 task_cancel_job 后确认最终状态再操作",null);
            if(ToolCatalog.UsesConfigurationRevision(request.Name))RequireAutomationRevision(request.Arguments);
            var reply=await ExecuteAutomation(request,token);
            string after=store.ConfigurationRevision();
            var node=JsonNode.Parse(reply.Data.GetRawText())!.AsObject();
            if(after!=before&&!reply.Data.TryGetProperty("saved",out _))node["saved"]=true;
            node["rendererRevisions"]=JsonSerializer.SerializeToNode(wallpapers?.Running.Select(p=>new {screenId=p.Key,processId=p.Value.Player.ProcessId,requestedRevision=p.Value.Player.RequestedRevision,appliedRevision=p.Value.Player.AppliedRevision}));
            node["widgetRequestedRevision"]=widgets?.ConfigurationRevision;node["widgetAppliedRevision"]=widgets?.AppliedRevision;
            reply=reply with {Data=JsonSerializer.SerializeToElement(node)};
            return automationJournal.Complete(operationId,reply,after);
        } catch(Exception ex) {
            AppLog.Warning("MCP",$"operationId={operationId}; tool={request.Name}",ex);
            string after=store.ConfigurationRevision();var failure=OperationErrors.FromException(ex);
            var data=JsonNode.Parse(failure.Data.GetRawText())!.AsObject();
            bool changed=before!=after&&ex is not OperationFault {Code:"revision_conflict"};
            data["saved"]=changed;data["status"]=changed?"partially_completed":ex is OperationCanceledException?"cancelled":"failed";
            return automationJournal.Complete(operationId,failure with {Data=JsonSerializer.SerializeToElement(data)},after);
        } finally {store.AutomationOperationId=null;}
    }
    private void RequireAutomationRevision(JsonElement arguments) {
        FlushHistoryEdits();
        string expected=Arg(arguments,"expectedRevision"),actual=store.ConfigurationRevision();
        if(expected!=actual)throw OperationFault.Conflict(expected,actual,"app_get_state");
    }
    private void RequireKnownScreen(string screen) {
        if(!Screen.AllScreens.Any(s=>s.DeviceName==screen)&&!store.Bindings().Any(b=>b.ScreenId==screen)&&!store.PlaybackScreens().Contains(screen)&&modules.Effects.ScreenProfile(screen)==null)throw new OperationFault("resource_not_found","未知显示器 ID","arguments.screenId");
    }
    private async Task<OperationReply> ExecuteExtendedAutomation(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        switch(request.Name) {
            case "task_get_operation":return OperationReply.Success(automationJournal.Read(Arg(a,"operationId")));
            case "task_get_job":case "task_cancel_job":return ReadAutomationJob(request);
            case "wallpaper_cancel": {
                string screen=Arg(a,"screenId");RequireKnownScreen(screen);bool existed=wallpapers!.CanCancel(screen);
                wallpapers.CancelWallpaper(screen);LoadPlaybackSettings();RefreshStatus();
                return OperationReply.Success(new {screenId=screen,removed=existed,saved=true,wallpaper=(object?)null,systemBackgroundRestored=true,cleanupComplete=!wallpapers.CanCancel(screen)});
            }
            case "media_list_folders":return OperationReply.Success(new {items=modules.Library.Folders().Select(f=>new {id=f.Id,path=f.Path,recursive=f.IncludeSubfolders,assetIds=modules.Library.FolderAssets(f.Id).Select(m=>m.Id),sourceMode="saved_membership"})});
            case "wallpaper_edit_queue": {
                string screen=Arg(a,"screenId");RequireKnownScreen(screen);var old=wallpapers!.GetPlayback(screen);var next=QueueEdit.Apply(old,a,modules.Library.Assets().Select(m=>m.Id).ToHashSet(),modules.Library.Folders().Select(f=>f.Id).ToHashSet());
                wallpapers.ConfigurePlayback(screen,next);if(Flag(a,"stopCurrent"))wallpapers.CancelWallpaper(screen);LoadPlaybackSettings();
                return OperationReply.Success(new {screenId=screen,queue=next,currentAssetId=wallpapers.Running.GetValueOrDefault(screen)?.Asset.Id,saved=true});
            }
            case "media_remove": {
                var ids=Ids(a,"assetIds");var known=modules.Library.Assets().Select(m=>m.Id).ToHashSet();if(ids.Any(id=>!known.Contains(id)))throw new OperationFault("resource_not_found","素材不存在，请重新读取 media_list","arguments.assetId");
                var screens=store.Bindings().Where(b=>ids.Contains(b.AssetId)).Select(b=>b.ScreenId).ToArray();
                var queues=store.PlaybackScreens().Where(s=>modules.Playback.Read(s) is var p&&(p.CustomPlaylists!.Any(playlist=>playlist.AssetIds.Any(ids.Contains))||p.FolderId is { } f&&modules.Library.FolderAssets(f).Any(m=>ids.Contains(m.Id)))).ToArray();
                var done=new List<string>();var errors=new List<object>();
                using(store.BeginUndoBatch("批量移除素材引用")) {
                    foreach(string id in ids) {
                        try {token.ThrowIfCancellationRequested();wallpapers!.RemoveAsset(id);done.Add(id);}
                        catch(Exception ex) {errors.Add(new {assetId=id,error=ex.Message});break;}
                    }
                    foreach(string screen in store.PlaybackScreens())
                    {
                        var p=modules.Playback.Read(screen);
                        if(!p.CustomPlaylists!.Any(playlist=>playlist.AssetIds.Any(done.Contains)))continue;
                        var playlists=p.CustomPlaylists!.Select(playlist=>playlist with {AssetIds=playlist.AssetIds.Where(id=>!done.Contains(id)).ToList()}).ToList();
                        wallpapers!.ConfigurePlayback(screen,p with {CustomPlaylists=playlists,AssetIds=p.AssetIds.Where(id=>!done.Contains(id)).ToList()});
                    }
                }
                RefreshFolders();RefreshLibrary();LoadPlaybackSettings();RefreshStatus();
                return new(errors.Count>0,ToolCatalog.Json(new {saved=done.Count>0,removed=done,errors,affectedScreens=screens,affectedQueues=queues,originalFilesPreserved=true,status=errors.Count>0?"partially_completed":"completed"}));
            }
            case "wallpaper_get_screen_options": {
                FlushHistoryEdits();string screen=Arg(a,"screenId");RequireKnownScreen(screen);
                var profile=modules.Effects.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
                return OperationReply.Success(new {screenId=screen,online=Screen.AllScreens.Any(s=>s.DeviceName==screen),fill=profile.Fill.ToString(),hideTaskbar=profile.Effects.HidesTaskbar,taskbarEnabled=profile.Effects.TaskbarAppearance.HasValue,taskbarStyle=profile.Effects.GetCollection().TaskbarStyle.ToString(),taskbarVisual=AutomationTaskbarVisual(profile.Effects.GetCollection().TaskbarVisual),customPosition=RuntimeSettingsReader.Position(store,screen)!=new WallpaperPosition(),positionX=RuntimeSettingsReader.Position(store,screen).X,positionY=RuntimeSettingsReader.Position(store,screen).Y,scale=RuntimeSettingsReader.Position(store,screen).Scale,configurationRevision=store.ConfigurationRevision()});
            }
            case "wallpaper_set_screen_options": {
                string screen=OperationScreen(Arg(a,"screenId")).DeviceName;var profile=modules.Effects.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
                if(a.TryGetProperty("fill",out var fill))profile=profile with {Fill=Enum.Parse<FillMode>(fill.GetString()!)};
                if(a.TryGetProperty("hideTaskbar",out var hide))profile=profile with {Effects=profile.Effects with {Collection=profile.Effects.GetCollection() with {Added=profile.Effects.GetCollection().Added|EffectKind.HideTaskbar,HideTaskbarEnabled=hide.GetBoolean(),TaskbarStyle=TaskbarStyle.Hidden,HideTaskbar=1}}};
                if(a.TryGetProperty("taskbarStyle",out var style)||a.TryGetProperty("taskbarEnabled",out _)) {
                    var collection=profile.Effects.GetCollection();
                    profile=profile with {Effects=profile.Effects with {Collection=collection with {Added=collection.Added|EffectKind.HideTaskbar,HideTaskbarEnabled=a.TryGetProperty("taskbarEnabled",out var enabled)?enabled.GetBoolean():true,TaskbarStyle=style.ValueKind==JsonValueKind.String?Enum.Parse<TaskbarStyle>(style.GetString()!):collection.TaskbarStyle}}};
                }
                if(a.TryGetProperty("taskbarVisual",out var visual)) {
                    var collection=profile.Effects.GetCollection();var current=collection.TaskbarVisual;
                    var next=current with {TransparentOpacity=visual.TryGetProperty("transparentOpacity",out var opacity)?opacity.GetInt32():current.TransparentOpacity,
                        FrostedOpacity=visual.TryGetProperty("frostedOpacity",out var tint)?tint.GetInt32():current.FrostedOpacity,
                        Blur=visual.TryGetProperty("blur",out var blur)?blur.GetInt32():current.Blur,
                        Color=visual.TryGetProperty("color",out var color)?color.GetString()!:current.Color,
                        ShowBorder=visual.TryGetProperty("showBorder",out var border)?border.GetBoolean():current.ShowBorder};
                    profile=profile with {Effects=profile.Effects with {Collection=collection with {TaskbarVisual=next.Validate()}}};
                }
                // Selecting a standard fill recenters; explicit axes then override it.
                if(a.TryGetProperty("fill",out _))SaveHomePosition(screen,new(),store.Bindings().FirstOrDefault(b=>b.ScreenId==screen)?.AssetId);
                if(a.TryGetProperty("positionX",out _)||a.TryGetProperty("positionY",out _)||a.TryGetProperty("scale",out _)) {
                    var position=RuntimeSettingsReader.Position(store,screen);
                    SaveHomePosition(screen,new(a.TryGetProperty("positionX",out var x)?x.GetInt32():position.X,a.TryGetProperty("positionY",out var y)?y.GetInt32():position.Y,a.TryGetProperty("scale",out var scale)?scale.GetInt32():position.Scale),store.Bindings().FirstOrDefault(b=>b.ScreenId==screen)?.AssetId);
                }
                if(a.TryGetProperty("fill",out _)||a.TryGetProperty("hideTaskbar",out _)||a.TryGetProperty("taskbarStyle",out _)||a.TryGetProperty("taskbarEnabled",out _)||a.TryGetProperty("taskbarVisual",out _))modules.Effects.SaveScreenProfile(screen,profile);
                wallpapers!.RefreshLiveEffects(new EffectChange([screen],[]));LoadEffectDraft(true);RefreshHomePosition();
                var player=wallpapers.Running.GetValueOrDefault(screen)?.Player;bool? confirmed=player==null?null:await player.WaitForConfiguration(token);
                return new(confirmed==false,ToolCatalog.Json(new {screenId=screen,saved=true,renderConfirmed=confirmed,fill=profile.Fill.ToString(),hideTaskbar=profile.Effects.HidesTaskbar,taskbarEnabled=profile.Effects.TaskbarAppearance.HasValue,taskbarStyle=profile.Effects.GetCollection().TaskbarStyle.ToString(),taskbarVisual=AutomationTaskbarVisual(profile.Effects.GetCollection().TaskbarVisual),customPosition=RuntimeSettingsReader.Position(store,screen)!=new WallpaperPosition(),positionX=RuntimeSettingsReader.Position(store,screen).X,positionY=RuntimeSettingsReader.Position(store,screen).Y,scale=RuntimeSettingsReader.Position(store,screen).Scale}));
            }
            case "media_import_batch":return StartAutomationImport(a);
            case "media_refresh":case "media_relink":return await RefreshAutomationMedia(request,token);
            case "media_get_info": {
                var media=modules.Library.Assets().FirstOrDefault(m=>m.Id==Arg(a,"assetId"))??throw new OperationFault("resource_not_found","素材不存在","arguments.assetId");
                if(!File.Exists(media.Path))return OperationReply.Success(new {assetId=media.Id,available=false,reason="file_missing"});
                string path=AuthorizedMediaPath(media.Path,registered:true);var beforeFile=new FileInfo(path);long bytes=beforeFile.Length;var modified=beforeFile.LastWriteTimeUtc;
                var probe=await NativePlayback.Probe(path,AutomationArtifactPath(".bmp"),token,metadataOnly:true);string? hash=null;
                if(Flag(a,"includeSha256")) {using var input=File.OpenRead(path);hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,token));}
                var afterFile=new FileInfo(path);if(afterFile.Length!=bytes||afterFile.LastWriteTimeUtc!=modified)throw new IOException("探测期间文件已变化，请重试");
                return OperationReply.Success(new {assetId=media.Id,available=true,probe,fingerprint=new {bytes,lastWriteUtc=modified,sha256=hash,hashVerified=hash!=null}});
            }
            case "widget_list_shortcuts":case "widget_remove_shortcut":case "widget_set_lock":case "widget_edit_group":case "widget_set_image":case "widget_reset_appearance":return await AutomationWidgetResources(request,token);
            case "screen_set_features": {
                var screen=OperationScreen(Arg(a,"screenId"));
                bool? effects=a.TryGetProperty("effectsEnabled",out var effectFlag)?effectFlag.GetBoolean():null;
                bool? components=a.TryGetProperty("widgetsEnabled",out var widgetFlag)?widgetFlag.GetBoolean():null;
                if(effects==null&&components==null)throw new ArgumentException("请指定效果或组件总开关");
                SetScreenFeatures(screen.DeviceName,effects,components);
                return OperationReply.Success(new {screenId=screen.DeviceName,effectsEnabled=RuntimeSettingsReader.EffectsEnabled(store,screen.DeviceName),widgetsEnabled=RuntimeSettingsReader.WidgetsEnabled(store,screen.DeviceName)});
            }
            case "app_get_settings":return OperationReply.Success(new {configurationRevision=store.ConfigurationRevision(),helpHints=helpHintsEnabled,skin=Appearance.Skin,motion=Appearance.Motion,pattern=Appearance.Pattern,fullscreenPause=wallpapers!.FullscreenPause,pauseOnLock=modules.Settings.PausePolicy().HasFlag(PauseReason.SessionLocked),pauseOnDisplayOff=modules.Settings.PausePolicy().HasFlag(PauseReason.DisplayOff),pauseOnSuspend=modules.Settings.PausePolicy().HasFlag(PauseReason.Suspend),autoStart=StartupRegistration.IsEnabled(),startupDelayEnabled=RuntimeSettingsReader.StartupDelay(store).Enabled,startupDelaySeconds=RuntimeSettingsReader.StartupDelay(store).Seconds,screenFeatures=Screen.AllScreens.Select(s=>new {screenId=s.DeviceName,effectsEnabled=RuntimeSettingsReader.EffectsEnabled(store,s.DeviceName),widgetsEnabled=RuntimeSettingsReader.WidgetsEnabled(store,s.DeviceName)}).ToArray(),audioAutoAdapt=modules.Settings.Get(AudioAutoAdaptKey)!="false",pauseOnHighLoad=modules.Settings.PausePolicy().HasFlag(PauseReason.HighResourceLoad),desktopMenuScreens=menu?.Enabled==false?Array.Empty<string>():EnabledMenuScreens(store),schema=ToolCatalog.All.Single(t=>t.Name=="app_update_settings").InputSchema});
            case "app_update_settings": {
                if(a.TryGetProperty("desktopMenuScreens",out _))foreach(string screen in Ids(a,"desktopMenuScreens"))OperationScreen(screen);
                var appearance=Appearance;
                if(Optional(a,"skin") is { } skin)appearance=appearance with {Skin=skin};
                if(a.TryGetProperty("motion",out var motion))appearance=appearance with {Motion=motion.GetBoolean()};
                if(a.TryGetProperty("pattern",out var pattern))appearance=appearance with {Pattern=pattern.GetBoolean()};
                modules.Settings.Set("appearance.v1",appearance.ToJson());Appearance=appearance;ApplyAppearancePalette();AppearanceChanged?.Invoke(appearance);
                foreach(var pair in new[]{("pauseOnLock",PauseReason.SessionLocked),("pauseOnDisplayOff",PauseReason.DisplayOff),("pauseOnSuspend",PauseReason.Suspend)})if(a.TryGetProperty(pair.Item1,out var value))modules.Settings.Set(RuntimeSettingsReader.PauseKey(pair.Item2),value.GetBoolean()?"true":"false");
                wallpapers!.SetPausePolicy(modules.Settings.PausePolicy());
                if(a.TryGetProperty("fullscreenPause",out var full)) {modules.Settings.Set("fullscreenPause",full.GetBoolean()?"true":"false");wallpapers.FullscreenPause=full.GetBoolean();syncingSettings=true;try {FullscreenSwitch.IsOn=full.GetBoolean();}finally {syncingSettings=false;}wallpapers.CheckFullscreen();}
                bool startupChanged=a.TryGetProperty("autoStart",out _)||a.TryGetProperty("startupDelayEnabled",out _)||a.TryGetProperty("startupDelaySeconds",out _);
                if(startupChanged) {
                    var delay=RuntimeSettingsReader.StartupDelay(store);
                    SetStartupConfiguration(a.TryGetProperty("autoStart",out var start)?start.GetBoolean():StartupRegistration.IsEnabled(),
                        new(a.TryGetProperty("startupDelayEnabled",out var delayOn)?delayOn.GetBoolean():delay.Enabled,a.TryGetProperty("startupDelaySeconds",out var delaySeconds)?delaySeconds.GetInt32():delay.Seconds));
                }
                if(a.TryGetProperty("helpHints",out var hints))SetHelpHintsEnabled(hints.GetBoolean());
                if(a.TryGetProperty("audioAutoAdapt",out var adapt))SetAudioAutoAdapt(adapt.GetBoolean());
                if(a.TryGetProperty("pauseOnHighLoad",out var load)) {modules.Settings.Set(RuntimeSettingsReader.PauseKey(PauseReason.HighResourceLoad),load.GetBoolean()?"true":"false");SyncResourceLoadPauseSettings();}
                if(a.TryGetProperty("desktopMenuScreens",out _)) {
                    var enabled=Ids(a,"desktopMenuScreens").ToHashSet();
                    foreach(var screen in Screen.AllScreens)modules.Settings.Set(MenuScreenKey(screen.DeviceName),enabled.Contains(screen.DeviceName)?"true":"false");
                    modules.Settings.Set("menuSafeDisabled",enabled.Count==0?"true":"false");menu?.SetEnabledScreens(enabled.ToArray());menu?.SetEnabled(enabled.Count>0);SyncMenuScreenSwitches();
                }
                return OperationReply.Success(new {saved=true,systemSideEffect=startupChanged?"startup_registration":null});
            }
            case "history_create_checkpoint": {
                if(automationCheckpoints.Count>=4)throw new OperationFault("invalid_state","最多保留 4 个检查点，请删除不用的检查点后重试",null);
                FlushAutomationConfiguration();var snapshot=store.CaptureConfiguration();ConfigurationCheckpointBudget.Validate(JsonSerializer.SerializeToUtf8Bytes(snapshot).LongLength);
                string id=Guid.NewGuid().ToString("N");automationCheckpoints[id]=snapshot;
                return OperationReply.Success(new {checkpointId=id,configurationRevision=store.ConfigurationRevision(),scope="application_configuration",expires="instance_exit",timerPolicy="saved_wall_clock",excluded=new[]{"source_files","original_media_bytes","authorization","startup_registration","undo_history"}});
            }
            case "history_restore_checkpoint": {
                if(!automationCheckpoints.TryGetValue(Arg(a,"checkpointId"),out var snapshot))throw new OperationFault("resource_not_found","检查点不存在或属于旧实例",null);
                var before=store.CaptureConfiguration();wallpapers!.CancelWallpaper();
                try {store.RestoreConfiguration(snapshot);}catch {store.RestoreConfiguration(before);throw;}
                effectSession.ClearRetained();automationUndo.Clear();scopedEffectUndo.Clear();widgets!.ReloadHistoryState();SyncAudioProcessingSettings();
                Appearance=AppearanceOptions.FromJson(modules.Settings.Get("appearance.v1"));ApplyAppearancePalette();AppearanceChanged?.Invoke(Appearance);
                wallpapers.SetFrameRateLimit(modules.Settings.FpsLimit());wallpapers.SetDecoderMode(modules.Settings.Decoder());wallpapers.SetPausePolicy(modules.Settings.PausePolicy());wallpapers.FullscreenPause=modules.Settings.Get("fullscreenPause")!="false";
                SyncResourceLoadPauseSettings();
                foreach(string screen in store.PlaybackScreens())wallpapers.ReloadPlayback(screen);
                var warnings=new List<object>();foreach(var binding in store.Bindings()) {
                    var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==binding.ScreenId);var media=modules.Library.Assets().FirstOrDefault(m=>m.Id==binding.AssetId);
                    try {if(screen==null||media==null||!File.Exists(media.Path))throw new IOException("屏幕离线或素材不可用");await wallpapers.Apply(media,screen,binding.Fill,binding.UserPaused,saveProfile:false,cancellationToken:token);}
                    catch(Exception ex) {warnings.Add(new {screenId=binding.ScreenId,error=ex.Message});}
                }
                RefreshFolders();RefreshLibrary();LoadPlaybackSettings();LoadEffectDraft(true);RefreshStatus();
                return OperationReply.Success(new {saved=true,status=warnings.Count==0?"completed":"partially_applied",warnings,historyCleared=true});
            }
            case "app_flush_configuration":FlushAutomationConfiguration();return OperationReply.Success(new {saved=true,walCheckpointComplete=true});
            case "app_restart": {
                ScheduleRestart();
                return OperationReply.Success(new {saved=true,status="restart_scheduled",oldInstanceId=automationJournal.InstanceId,reconnectRequired=true});
            }
            case "history_delete_checkpoint":return OperationReply.Success(new {removed=automationCheckpoints.Remove(Arg(a,"checkpointId"))});
            case "ui_get_tree":case "ui_perform_action":return await AutomationUi(request,token);
            case "config_export":case "config_preview_import":case "config_commit_import":case "config_cancel_import":return await AutomationSharing(request,token);
            case "effect_list_masks":case "effect_edit_mask":return await AutomationMasks(request,token);
            default:return await AutomationCoverage(request,token);
        }
    }
}
