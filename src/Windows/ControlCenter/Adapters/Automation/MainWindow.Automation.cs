using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private LocalOperationServer? automationServer;
    private readonly SemaphoreSlim automationGate=new(1,1);
    private bool automationReady,automationClosing,syncingAutomation;
    private ToggleSwitch? automationSwitch;
    private Button? automationCopyPrompt;
    private sealed record EffectUndo(string AssetId,WallpaperProfile Before,string AfterRevision);
    private readonly Dictionary<string,EffectUndo> automationUndo=new();

    private void InitializeAutomationSettings()
    {
        var panel=new StackPanel {Spacing=12};panel.Children.Add(UtilityText("MCP","UtilityHeading"));
        automationSwitch=WidgetToggle(UiLanguage.Text("开启"),ToolCatalog.IsEnabled(modules.Settings.Get(ToolCatalog.EnabledSetting)));
        panel.Children.Add(WidgetToggleRow(UiLanguage.Text("开启"),automationSwitch));
        panel.Children.Add(WidgetButton(UiLanguage.Text("授权 AI 素材文件夹"),async()=> {
            try {
                var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder=await picker.PickSingleFolderAsync();if(folder==null)return;
                MediaAccessPolicy.ValidatePath(folder.Path,[folder.Path],[AppContext.BaseDirectory,store.Root],true);
                var roots=JsonSerializer.Deserialize<string[]>(modules.Settings.Get("automation.mediaRoots")??"[]")??[];
                modules.Settings.Set("automation.mediaRoots",JsonSerializer.Serialize(roots.Append(folder.Path).Distinct(StringComparer.OrdinalIgnoreCase)));
                Inform(UiLanguage.Text("AI 素材目录已授权：")+folder.Path);
            } catch(Exception ex) {ShowError(UiLanguage.Text("目录授权失败：")+ex.Message,ex);}
        }));
        panel.Children.Add(WidgetButton(UiLanguage.Text("清除 AI 素材目录授权"),()=> {modules.Settings.Set("automation.mediaRoots","[]");foreach(var job in automationJobs.Values.Where(j=>j.FinishedAt==null))job.Cancellation.Cancel();Inform(UiLanguage.Text("AI 素材目录授权已清除"));}));
        automationCopyPrompt=WidgetButton(UiLanguage.Text("复制 AI 连接提示词"),()=> {
            try {
                if(automationServer==null)throw new OperationFault("invalid_state",UiLanguage.Text("MCP 未就绪"),null);
                string executable=AutomationExecutable();
                var package=new DataPackage();package.SetText(ConnectionPrompt.Create(executable));Clipboard.SetContent(package);
                Inform(UiLanguage.Text("连接提示词已复制"));
            } catch(Exception ex) {ShowError(UiLanguage.Text("无法复制连接提示词：")+ex.Message,ex);}
        });panel.Children.Add(automationCopyPrompt);
        panel.Children.Add(WidgetButton(UiLanguage.Text("AI 操作记录"),async()=> {
            try {await ShowAutomationHistory();}
            catch(Exception ex) {ShowError(UiLanguage.Text("无法打开 AI 操作记录：")+ex.Message,ex);}
        }));
        SettingsAutomationHost.Content=panel;
        automationSwitch.Toggled+=(_,_)=> {
            if(syncingAutomation||automationClosing)return;
            try {SetAutomationEnabled(automationSwitch.IsOn);}
            catch(Exception ex) {
                syncingAutomation=true;automationSwitch.IsOn=automationServer!=null;syncingAutomation=false;
                ShowError(UiLanguage.Text("AI 操作设置未能更新：")+ex.Message,ex);
            }
            UpdateAutomationStatus();
        };
        UpdateAutomationStatus();
    }
    private static string AutomationExecutable()
    {
        string executable=Path.Combine(AppContext.BaseDirectory,"Mcp","ZZZwallpaper.Mcp.exe");
        if(!File.Exists(executable))throw new IOException(UiLanguage.Text("当前目录缺少 MCP 组件，请使用完整发布包。"));
        return executable;
    }
    public void StartAutomation()
    {
        if(automationClosing)return;automationReady=true;
        if(ToolCatalog.IsEnabled(modules.Settings.Get(ToolCatalog.EnabledSetting))) {
            try {SetAutomationEnabled(true);}
            catch(Exception ex) {syncingAutomation=true;automationSwitch!.IsOn=false;syncingAutomation=false;ShowError(UiLanguage.Text("MCP 未能启动：")+ex.Message,ex);}
        }
        UpdateAutomationStatus();
    }
    private void SetAutomationEnabled(bool enabled)
    {
        if(enabled) {
            LocalOperationServer? candidate=null;
            try {
                if(automationReady&&automationServer==null)candidate=new LocalOperationServer(DispatchAutomation,error=> {
                    AppLog.Error("MCP","Local operation service stopped",error);
                    DispatcherQueue.TryEnqueue(()=> {
                        if(automationClosing||!ReferenceEquals(automationServer,candidate))return;
                        automationServer?.Dispose();automationServer=null;syncingAutomation=true;automationSwitch!.IsOn=false;syncingAutomation=false;
                        try {modules.Settings.Set(ToolCatalog.EnabledSetting,"false");}catch(Exception saveError) {AppLog.Warning("MCP","Cannot persist disabled state",saveError);}
                        UpdateAutomationStatus();
                    });
                });
                modules.Settings.Set(ToolCatalog.EnabledSetting,"true");
                if(candidate!=null)automationServer=candidate;
            } catch {candidate?.Dispose();throw;}
        } else {
            StopAutomationTestAudio();
            if(wallpapers!=null)foreach(var running in wallpapers.Running.Values)running.Player.SetAutomationMonitoring(false);
            foreach(var job in automationJobs.Values.Where(j=>j.FinishedAt==null))job.Cancellation.Cancel();
            // Revoke before persistence so a disk error cannot leave a visually disabled service active.
            automationServer?.Dispose();automationServer=null;automationUndo.Clear();scopedEffectUndo.Clear();automationUiSnapshots.Clear();automationControls.Clear();
            modules.Settings.Set(ToolCatalog.EnabledSetting,"false");
        }
        AppLog.Info("MCP",enabled?"User enabled application operations":"User disabled application operations");
    }
    private void UpdateAutomationStatus()
    {
        if(automationCopyPrompt!=null)automationCopyPrompt.IsEnabled=automationServer!=null;
    }
    public void StopAutomation()
    {
        StopAutomationTestAudio();
            if(wallpapers!=null)foreach(var running in wallpapers.Running.Values)running.Player.SetAutomationMonitoring(false);
        foreach(var job in automationJobs.Values.Where(j=>j.FinishedAt==null))job.Cancellation.Cancel();
        automationClosing=true;automationServer?.Dispose();automationServer=null;automationUndo.Clear();scopedEffectUndo.Clear();automationUiSnapshots.Clear();automationControls.Clear();
    }
    private async Task<OperationReply> DispatchAutomation(OperationRequest request,CancellationToken token)
    {
        bool bypass=request.Name is "task_get_job" or "task_cancel_job" or "task_get_operation";
        if(!bypass)await automationGate.WaitAsync(token);
        try {
            var result=new TaskCompletionSource<OperationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!DispatcherQueue.TryEnqueue(async()=> {
                var activity=BeginAutomationActivity(request);
                try {
                    token.ThrowIfCancellationRequested();
                    if(automationClosing||!automationReady||automationServer==null||!ToolCatalog.IsEnabled(modules.Settings.Get(ToolCatalog.EnabledSetting)))throw new OperationFault("invalid_state","MCP 已关闭",null);
                    var tool=ToolCatalog.Validate(request.Name,request.Arguments);
                    if(historyBusy&&!tool.ReadOnly)throw new OperationFault("invalid_state","正在恢复操作，请稍后重试。",null);
                    // Native UI events own their edit transaction. Starting a new
                    // gesture here would submit a text draft before its Enter/Esc.
                    if(!tool.ReadOnly&&request.Name!="ui_perform_action")BeginHistoryGesture();
                    var reply=await ExecuteRecordedAutomation(request,token);
                    CompleteAutomationActivity(activity,reply);
                    if(!automationClosing) {
                        if(!tool.ReadOnly)AppLog.Info("MCP",$"Operation={request.Name}; success={!reply.IsError}");
                    }
                    result.TrySetResult(reply);
                } catch(OperationCanceledException ex) {CompleteAutomationActivity(activity,OperationErrors.FromException(ex),"cancelled");result.TrySetCanceled(token);}
                catch(Exception ex) {AppLog.Warning("MCP",$"Operation failed: {request.Name}",ex);var failure=OperationErrors.FromException(ex);CompleteAutomationActivity(activity,failure);result.TrySetResult(failure);}
            }))throw new OperationFault("invalid_state","软件正在退出",null);
            // Keep serialization until the UI callback has actually completed, even when cancellation occurs.
            return await result.Task;
        } finally {if(!bypass)automationGate.Release();}
    }
    private static string Arg(JsonElement args,string key)=>args.GetProperty(key).GetString()!;
    private static Screen OperationScreen(string id)=>Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==id)??throw new OperationFault("resource_not_found","显示器不存在，请重新读取 app_get_state","arguments.screenId");
    private RunningWallpaper OperationWallpaper(string screen)=>wallpapers!.Running.GetValueOrDefault(screen)??throw new OperationFault("wallpaper_not_applied","此屏幕尚未应用壁纸","arguments.screenId");
    private WallpaperProfile OperationProfile(string screen)=>modules.Effects.ScreenProfile(screen)??(wallpapers!.Running.TryGetValue(screen,out var item)?new(item.Binding.Fill,item.Binding.Effects):new(FillMode.Cover,VideoEffects.Original));
    private string EffectRevision(string screen)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {screen,asset=wallpapers!.Running.GetValueOrDefault(screen)?.Asset.Id,profile=OperationProfile(screen)}))));
    private void RequireEffectRevision(string screen,string revision) {if(revision!=EffectRevision(screen))throw new OperationFault("invalid_state","参数已被修改，请重新调用 effect_get 后再操作。",null);}
    private void SyncAutomationEffects(string screen,WallpaperProfile profile)
    {
        effectSession.ClearRetained(screen);
        if(SelectedScreen?.DeviceName==screen&&selected!=null) {
            SyncHomeFill(profile.Fill);
            effectSession.AdvanceRevision();SetEffectControls(profile.Effects);SetEffectsSaveStatus("AI 已应用并保存");
        }
    }
    private async Task<OperationReply> ExecuteAutomation(OperationRequest request,CancellationToken token)
    {
        var args=request.Arguments;token.ThrowIfCancellationRequested();
        switch(request.Name) {
            case "app_get_state":
                FlushHistoryEdits();
                return OperationReply.Success(new {
                    version=ApplicationVersion,releaseChannel="stable",agentGuideVersion=AgentGuidance.Version,mcpEnabled=true,
                    instanceId=automationJournal.InstanceId,configurationRevision=store.ConfigurationRevision(),history=store.HistoryDetails(),
                    automationPolicy=new {arbitraryCode=false,arbitraryFileAccess=false,archiveExtraction=false,validatedPresetImport=true,validatedSteamAssetImport=true,externalLaunch=false,jobCancellation="completed items retained",operationRetention=256},
                    selectedScreenId=SelectedScreen?.DeviceName,effectTarget=new {scope=effectTargetAssetId==null?"screen":"asset",assetId=effectTargetAssetId},
                    capabilities=new {widgetKinds=ExpansionCatalog.Widgets.Keys.Where(ExpansionCatalog.WidgetAvailable).Select(kind=>kind.ToString()).Concat(FeaturePacks.Widgets.Select(p=>p.Key)).ToArray(),effectKinds=ExpansionCatalog.Effects.Keys.Select(kind=>kind.ToString()).ToArray(),timeWidgets=true,timeWidgetStyles=true,focusTimerActions=true,particles=true,particleEmitters=true,customParticleImages=true,widgetCountLimit=(int?)null,websiteShortcuts=true,maskOverlays=true,widgetOptions=true,removeWidget=true,imageWallpapers=true,importMedia=true,assetEffects=true,effectInheritance=true,emptyLibraryEffects=true,clearEffects=true,copyScreenEffects=true,decoderModes=true,decodeBenchmark=true,manualPoster=true,posterPreview=true,processGpuMetrics=true,separateEffectEditing=true,undoRedo=true,queueEditing=true,cancelWallpaper=true,removeMediaReferences=true,mediaBatchJobs=true,checkpoints=true,configurationSharing=true,maskResources=true,widgetGroups=true,widgetImages=true,uiInput=true,uiInputScope="parameter_editors_and_section_headers",namedPlaylists=true,presets=true,effectOrdering=true,globalAudioProcessing=true,volume=true,uiNavigation=true,uiDropdownSelection=true,renderCapture="visible_desktop_pixels",renderTelemetry=true,audioDevices=true,operationJournal=true,sourceOrPackageAccess=false},
                    screens=Screen.AllScreens.Select(s=>new {id=s.DeviceName,primary=s.Primary,width=s.Bounds.Width,height=s.Bounds.Height,displayMode=AutomationScreenMode(s.DeviceName),origin=new {x=s.Bounds.X,y=s.Bounds.Y},workArea=new {x=s.WorkingArea.X,y=s.WorkingArea.Y,width=s.WorkingArea.Width,height=s.WorkingArea.Height},dpi=96*WidgetPlacement.Scale(s),
                        scale=WidgetPlacement.Scale(s),widthDip=(int)(s.Bounds.Width/WidgetPlacement.Scale(s)),heightDip=(int)(s.Bounds.Height/WidgetPlacement.Scale(s)),
                        wallpaper=wallpapers!.Running.TryGetValue(s.DeviceName,out var item)?new {assetId=item.Asset.Id,name=item.Asset.Name,state=item.Player.ActualState,decoder=item.Player.Decoder,paused=item.Binding.UserPaused,pauseReasons=item.Pause.Reasons.ToString(),sound=wallpapers.VideoSoundEnabled(s.DeviceName)}:null,
                        options=new {fill=OperationProfile(s.DeviceName).Fill.ToString(),hideTaskbar=OperationProfile(s.DeviceName).Effects.HidesTaskbar},playback=wallpapers!.GetPlayback(s.DeviceName)}),audioAutoAdapt=modules.Settings.Get(AudioAutoAdaptKey)!="false",fpsLimit=wallpapers!.FrameRateLimit,decoderMode=wallpapers.DecoderPreference.ToString(),
                    widgets=widgets!.State.Widgets.Select(w=>new {id=w.Id,kind=w.Kind.ToString(),screenId=w.Screen,w.Enabled,w.X,w.Y,w.Opacity,w.Layer,w.Locked,w.InteractionLocked,w.LayoutGroupId,size=WidgetPlacement.LogicalSize(w) is var size?new {widthDip=size.Width,heightDip=size.Height}:null,text=w.Kind==WidgetKind.Text?w.Visual.Text:null,
                        website=widgets.State.Shortcuts.FirstOrDefault(c=>c.Id==w.ShortcutId&&c.Kind==ShortcutKind.Website) is { } card?new {name=card.Name,url=card.Url}:null})
                });
            case "history_undo":case "history_redo":
                return OperationReply.Success(new {message=await RestoreHistory(request.Name=="history_redo",Arg(args,"expectedRevision")),revision=store.HistoryRevision.ToString()});
            case "media_list":
                int offset=args.TryGetProperty("offset",out var offsetValue)?offsetValue.GetInt32():0,limit=args.TryGetProperty("limit",out var limitValue)?limitValue.GetInt32():50;
                var result=await store.QueryLibraryAssetsAsync(AutomationLibraryFolder(args,null),AutomationLibraryFilter(args,new()),token);
                var assets=result.Items;return OperationReply.Success(new {total=assets.Count,offset,items=assets.Skip(offset).Take(limit).Select(a=>new {id=a.Id,name=a.Name,kind=a.Kind,a.Width,a.Height,a.Duration,a.Codec,a.Bytes,a.Color,available=File.Exists(a.Path),poster=ReadPosterChoice(a.Id)})});
            case "wallpaper_set_decoder_mode":case "diagnostics_benchmark_decode":case "media_preview_poster":case "media_set_poster":case "media_reset_poster":return await AutomationMediaTools(request,token);
            case "media_import":return await ImportAutomationMedia(Arg(args,"path"),token,()=>RequireAutomationRevision(args));
            case "effect_order":case "audio_set_rule":case "audio_remove_rule":case "effect_get":case "effect_set":case "effect_set_scope":case "effect_clear":case "effect_copy_screen_to_asset":case "effect_undo":return await AutomationEffects(request,token);
            case "wallpaper_apply": {
                var screen=OperationScreen(Arg(args,"screenId"));var asset=modules.Library.Assets().FirstOrDefault(a=>a.Id==Arg(args,"assetId"))??throw new OperationFault("resource_not_found","素材不存在，请从 media_list 选择已导入的图片或视频。","arguments.assetId");
                EndEffectEditing();var profile=OperationProfile(screen.DeviceName);
                await wallpapers!.Apply(asset,screen,profile.Fill,false,profile.Effects,cancellationToken:token,beforeCommit:()=>RequireAutomationRevision(args));
                automationUndo.Remove(screen.DeviceName);LoadPlaybackSettings();RefreshStatus();return OperationReply.Success(new {applied=true,screenId=screen.DeviceName,assetId=asset.Id});
            }
            case "wallpaper_set_paused": {
                string screen=OperationScreen(Arg(args,"screenId")).DeviceName;var current=OperationWallpaper(screen);
                if(current.Binding.UserPaused!=args.GetProperty("paused").GetBoolean())wallpapers!.ToggleUserPause(screen);
                return OperationReply.Success(new {userPaused=current.Pause.Reasons.HasFlag(PauseReason.User),effectivePaused=current.Pause.IsPaused});
            }
            case "wallpaper_set_sound": {string screen=OperationScreen(Arg(args,"screenId")).DeviceName;wallpapers!.SetVideoSound(screen,args.GetProperty("enabled").GetBoolean());return OperationReply.Success(new {enabled=wallpapers.VideoSoundEnabled(screen)});}
            case "wallpaper_step": {
                string screen=OperationScreen(Arg(args,"screenId")).DeviceName;
                if(!wallpapers!.CanStepPlayback(screen))throw new OperationFault("invalid_state","当前播放模式、队列或加载状态不允许切换。",null);
                bool stepped=await wallpapers.StepPlayback(screen,Arg(args,"direction")=="next"?1:-1,token,()=>RequireAutomationRevision(args));token.ThrowIfCancellationRequested();
                if(!stepped)return OperationReply.Error("切换未完成，当前画面已保留；请检查素材或重新读取状态。");
                return OperationReply.Success(new {assetId=wallpapers.Running.GetValueOrDefault(screen)?.Asset.Id});
            }
            case "wallpaper_set_playback": {
                string screen=OperationScreen(Arg(args,"screenId")).DeviceName;var settings=wallpapers!.GetPlayback(screen) with {Mode=Enum.Parse<PlaybackMode>(Arg(args,"mode"))};
                if(args.TryGetProperty("transition",out var transition))settings=settings with {Transition=Enum.Parse<PlaybackTransition>(transition.GetString()!)};
                if(args.TryGetProperty("trigger",out var trigger))settings=settings with {Trigger=Enum.Parse<PlaybackTrigger>(trigger.GetString()!)};
                if(args.TryGetProperty("intervalSeconds",out var seconds))settings=settings with {IntervalSeconds=seconds.GetInt32()};
                wallpapers.ConfigurePlayback(screen,settings);LoadPlaybackSettings();return OperationReply.Success(new {settings});
            }
            case "wallpaper_set_fps_limit": {
                int cap=args.GetProperty("fps").GetInt32();modules.Settings.Set("fpsLimit",cap.ToString(System.Globalization.CultureInfo.InvariantCulture));wallpapers!.SetFrameRateLimit(cap);syncAutomationFps?.Invoke(cap);return OperationReply.Success(new {fps=cap});
            }
            case "widget_get": {
                CommitSpectrumInput();widgets!.FlushPendingSave();var state=widgets.State;
                var widget=state.Widgets.FirstOrDefault(w=>w.Id==Arg(args,"widgetId"))??throw new OperationFault("resource_not_found","组件不存在","arguments.widgetId");
                string widgetRevision=widgets.ConfigurationRevision,revision=store.ConfigurationRevision();var inspection=await widgets.Inspect(widget.Id,token);
                return OperationReply.Success(new {widgetId=widget.Id,revision,widgetRevision,appliedWidgetRevision=inspection?.Revision,snapshotStillCurrent=widgets.ConfigurationRevision==widgetRevision,renderTelemetry=inspection?.Telemetry,kind=widget.Kind.ToString(),screenId=widget.Screen,defaultGroups=Enumerable.Range(0,8),
                    widget.X,widget.Y,widget.Enabled,widget.Opacity,widget.Layer,locked=state.IsEditingLocked(widget),interactionLocked=state.IsInteractionLocked(widget),widget.LayoutGroupId,
                    widget.IconOnly,metrics=(int)widget.Metrics,visual=widget.Visual with {Image=null},hasImage=widget.Visual.Image!=null,
                    clock=widget.Clock,countdown=widget.Countdown,focus=widget.Focus,remainingSeconds=widget.Kind==WidgetKind.FocusTimer?widget.Focus.Remaining(DateTimeOffset.UtcNow):(double?)null,search=widget.Search,performance=widget.Performance,spectrum=widget.Spectrum,gridOptions=widget.GridOptions,parameterSchemas=WidgetPatch.Schemas.Keys.ToDictionary(k=>k,k=>WidgetPatch.InputSchema(k,false)),
                    website=state.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId&&c.Kind==ShortcutKind.Website) is { } card?new {name=card.Name,url=card.Url}:null});
            }
            case "widget_update": case "widget_remove": {
                CommitSpectrumInput();widgets!.FlushPendingSave();var state=widgets.State;
                var widget=state.Widgets.FirstOrDefault(w=>w.Id==Arg(args,"widgetId"))??throw new OperationFault("resource_not_found","组件不存在","arguments.widgetId");
                if(request.Name=="widget_update"?!WidgetPatch.CanUpdate(state,widget,args):state.IsEditingLocked(widget)||widget.LayoutGroupId!=null)throw new OperationFault("widget_locked","组件位置或交互已锁定，请先解除对应锁定；布局修改需移出组件组。","arguments.widgetId");
                if(request.Name=="widget_remove") {
                    widgets.Save((state with {Widgets=state.Widgets.Where(w=>w.Id!=widget.Id).ToArray()}).Validate());
                    return OperationReply.Success(new {widgetId=widget.Id,removed=true,saved=true});
                }
                if(args.TryGetProperty("screenId",out var screen))widget=widget with {Screen=OperationScreen(screen.GetString()!).DeviceName};
                widget=WidgetPatch.Apply(widget,args);
                if(args.TryGetProperty("name",out _)||args.TryGetProperty("url",out _))state=WidgetPatch.UpdateWebsite(state,widget,args);
                widgets.Save((state with {Widgets=state.Widgets.Select(w=>w.Id==widget.Id?widget:w).ToArray()}).Validate());
                return OperationReply.Success(new {widgetId=widget.Id,saved=true});
            }
            case "widget_add": {
                CommitSpectrumInput();widgets!.FlushPendingSave();var screen=OperationScreen(Arg(args,"screenId"));var kindName=Arg(args,"kind");
                var package=FeaturePacks.Widgets.FirstOrDefault(p=>p.Key==kindName||string.Equals(p.Entry.Id,kindName,StringComparison.OrdinalIgnoreCase));
                var kind=package!=null?WidgetKind.Extension:Enum.Parse<WidgetKind>(kindName,true);
                if(package==null&&!ExpansionCatalog.WidgetAvailable(kind))throw new OperationFault("resource_not_found","请先导入此组件所需的扩展包。");
                var widget=WidgetPatch.Apply(NewWidget(kind,package?.Key) with {Screen=screen.DeviceName},args);
                WidgetConfiguration next;
                if(kind==WidgetKind.Shortcuts) {
                    if(!args.TryGetProperty("name",out _)||!args.TryGetProperty("url",out _))throw new OperationFault("invalid_argument","网站快捷方式需要 name 和 url",null);
                    next=WidgetPatch.AddWebsite(widgets.State,widget,Arg(args,"name"),Arg(args,"url"));
                } else {
                    if(args.TryGetProperty("name",out _)||args.TryGetProperty("url",out _))throw new OperationFault("invalid_argument","name/url 仅用于网站快捷方式",null);
                    next=(widgets.State with {Widgets=[..widgets.State.Widgets,widget]}).Validate();
                }
                widgets.Save(next);return OperationReply.Success(new {widgetId=widget.Id,saved=true});
            }
            case "diagnostics_sample_performance": {
                var targets=SnapshotPerformanceTargets();
                using var sampler=new ProcessPerformanceSampler();await Task.Run(()=>sampler.Sample(targets),token);
                await Task.Delay(2000,token);targets=SnapshotPerformanceTargets();
                var sample=await Task.Run(()=>sampler.Sample(targets),token);
                return OperationReply.Success(new {sampledAt=DateTimeOffset.UtcNow,intervalSeconds=2,processes=sample,gpu=sampler.GpuTotals,note="仅统计本软件相关进程；CPU 按整机归一化；GPU 为最忙引擎；内存为工作集，显存按进程合计；I/O 包含缓存；null 为不可用。"});
            }
            default: return await ExecuteExtendedAutomation(request,token);
        }
    }
    private Action<int>? syncAutomationFps;
}
