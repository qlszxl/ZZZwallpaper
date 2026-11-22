using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<OperationReply> AutomationCoverage(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        switch(request.Name) {
            case "ui_library_get_filter":return OperationReply.Success(AutomationLibraryFilterState());
            case "ui_library_set_filter": {
                var filter=AutomationLibraryFilter(a,libraryFilter);
                string? folder=AutomationLibraryFolder(a,SelectedLibraryFolder?.Id);
                RefreshFolders();refreshingFolders=true;
                try {LibraryFolderPicker.SelectedItem=LibraryFolderPicker.Items.OfType<LibraryFolderRow>().FirstOrDefault(f=>f.Folder?.Id==folder);}
                finally {refreshingFolders=false;}
                SetLibraryFilter(filter);RefreshFolderControls();
                return OperationReply.Success(AutomationLibraryFilterState());
            }
            case "app_get_capabilities":return OperationReply.Success(new {
                toolCount=ToolCatalog.All.Count,
                features=new[] {
                    new {area="navigation",tools=new[]{"ui_navigate","ui_get_tree","ui_select_option","ui_perform_action","ui_capture_window","ui_capture_desktop"}},
                    new {area="library",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("media_")||t.Name.StartsWith("ui_library_")).Select(t=>t.Name).ToArray()},
                    new {area="wallpaper",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("wallpaper_")).Select(t=>t.Name).ToArray()},
                    new {area="effects_audio",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("effect_")||t.Name.StartsWith("audio_")).Select(t=>t.Name).ToArray()},
                    new {area="widgets",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("widget_")).Select(t=>t.Name).ToArray()},
                    new {area="presets_sharing",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("preset_")||t.Name.StartsWith("config_")).Select(t=>t.Name).ToArray()},
                    new {area="settings_history_diagnostics",tools=ToolCatalog.All.Where(t=>t.Name.StartsWith("history_")||t.Name.StartsWith("diagnostics_")||t.Name.StartsWith("app_")||t.Name.StartsWith("task_")).Select(t=>t.Name).ToArray()}
                },
                boundaries=new[]{"MCP enablement and directory authorization require manual UI approval","No arbitrary commands, external app control, installation or application source-code access","Website shortcuts are editable; command/folder shortcut execution is intentionally unavailable","OS file pickers are replaced by authorized-path import and app-owned export","Steam-managed originals must be managed in Steam; media deletion requires a preview and explicit confirmation"}
            });
            case "ui_navigate": {
                string page=Arg(a,"page");
                if(Optional(a,"effect")!=null&&page!="display"||Optional(a,"assetId")!=null&&page!="display"||Optional(a,"widgetId")!=null&&page!="widgets")throw new OperationFault("invalid_argument","详情目标与页面不一致",null);
                if(Optional(a,"screenId") is { } screen)OperationScreen(screen);
                if(Optional(a,"widgetId") is { } wid&&!widgets!.State.Widgets.Any(w=>w.Id==wid))throw new OperationFault("resource_not_found","组件不存在",null);
                if(Optional(a,"assetId") is { } asset&&!modules.Library.Assets().Any(m=>m.Id==asset))throw new OperationFault("resource_not_found","素材不存在",null);
                FlushAutomationConfiguration();
                if(Optional(a,"screenId") is { } target)SelectScreen(target);
                if(page=="display") {EndEffectEditing();effectTargetAssetId=Optional(a,"assetId");LoadEffectDraft(true);}
                Navigate(page);
                if(Optional(a,"widgetId") is { } widget)OpenWidget(widget);
                if(Optional(a,"effect") is { } name) {
                    var kind=Enum.Parse<EffectKind>(name);
                    if((effectSession.Collection.Added&kind)==0)throw new OperationFault("invalid_state","请先添加该效果",null);
                    OpenEffect(kind);
                }
                automationUiSnapshots.Clear();
                if(Flag(a,"foreground"))ShowControlCenter();
                return OperationReply.Success(new {page=navigationPage,screenId=SelectedScreen?.DeviceName,foreground=AutomationForeground()==WinRT.Interop.WindowNative.GetWindowHandle(this)});
            }
            case "ui_select_option":return AutomationSelectOption(a);
            case "wallpaper_set_volume": {
                string screen=OperationScreen(Arg(a,"screenId")).DeviceName;
                wallpapers!.SetVideoSoundVolume(screen,a.GetProperty("volume").GetInt32());
                return OperationReply.Success(new {saved=true,screenId=screen,volume=modules.Playback.Volume(screen),enabled=wallpapers.VideoSoundEnabled(screen)});
            }
            case "wallpaper_edit_playlist": {
                string screen=OperationScreen(Arg(a,"screenId")).DeviceName,action=Arg(a,"action");
                var current=wallpapers!.GetPlayback(screen).Validate();var lists=current.CustomPlaylists!.ToList();
                var selected=lists.FirstOrDefault(p=>p.Id==Optional(a,"playlistId"));
                if(action!="create"&&selected==null)throw new OperationFault("resource_not_found","播放列表不存在",null);
                if(action is "create" or "rename") {
                    string name=Optional(a,"name")?.Trim()??"";
                    if(name.Length==0||lists.Any(p=>p.Id!=selected?.Id&&string.Equals(p.Name,name,StringComparison.CurrentCultureIgnoreCase)))throw new OperationFault("invalid_argument","列表名称为空或重复",null);
                    if(action=="create") {selected=new() {Id=Guid.NewGuid().ToString("N"),Name=name};lists.Add(selected);}
                    else {selected=selected! with {Name=name};lists[lists.FindIndex(p=>p.Id==selected.Id)]=selected;}
                }
                if(action=="delete")lists.Remove(selected!);
                var next=current with {CustomPlaylists=lists};
                if(action is "select" or "create")next=next with {PlaylistId=selected!.Id,FolderId=null,AssetIds=selected.AssetIds.ToList()};
                if(action=="delete"&&current.PlaylistId==selected!.Id)next=next with {PlaylistId=lists.FirstOrDefault()?.Id,AssetIds=lists.FirstOrDefault()?.AssetIds.ToList()??[]};
                wallpapers.ConfigurePlayback(screen,next.Validate());LoadPlaybackSettings();
                return OperationReply.Success(new {saved=true,screenId=screen,settings=wallpapers.GetPlayback(screen)});
            }
            case "media_remove_folder": {
                string id=Arg(a,"folderId");bool exists=modules.Library.Folders().Any(f=>f.Id==id);
                if(exists) {modules.Library.RemoveFolder(id);RefreshFolders();RefreshLibrary();LoadPlaybackSettings();}
                return OperationReply.Success(new {saved=exists,folderId=id,removed=exists});
            }
            case "widget_duplicate": {
                var state=widgets!.State;var source=state.Widgets.FirstOrDefault(w=>w.Id==Arg(a,"widgetId"))??throw new OperationFault("resource_not_found","组件不存在",null);
                if(state.IsEditingLocked(source))throw new OperationFault("widget_locked","请先解锁组件",null);
                var copy=source with {Id=Guid.NewGuid().ToString("N"),LayoutGroupId=null,LayoutOrder=0};
                widgets.Save((state with {Widgets=[..state.Widgets,copy]}).Validate());RefreshWidgets();
                return OperationReply.Success(new {saved=true,widgetId=copy.Id});
            }
            case "preset_list":return OperationReply.Success(new {items=modules.Presets.List()});
            case "preset_get": {
                var document=modules.Presets.Inspect(Arg(a,"presetId"),token);
                return OperationReply.Success(new {preset=new {id=Arg(a,"presetId"),document.Name,document.Selection,document.Screens,assets=document.Assets.Length,bindings=document.Bindings.Length,widgets=document.Widgets?.Widgets.Length??0}});
            }
            case "preset_save": {
                bool Choice(string key,bool fallback)=>a.TryGetProperty(key,out var value)?value.GetBoolean():fallback;
                var selection=new PresetSelection {Playing=Choice("playing",true),Library=Choice("library",false),Effects=Choice("effects",true),Widgets=Choice("widgets",true),IncludeWidgetPrivateData=Flag(a,"includePrivateData")};
                if(!selection.Any)throw new OperationFault("invalid_argument","至少选择一个预设类别",null);
                FlushAutomationConfiguration();var document=modules.Presets.Capture(Arg(a,"name"),Screen.AllScreens.Select(s=>s.DeviceName).ToArray(),selection,wallpapers?.Running.Values.Select(p=>p.Binding),Screen.PrimaryScreen?.DeviceName);
                var result=await modules.Presets.SaveAsync(document,token);RefreshPresetCards();return OperationReply.Success(new {saved=true,preset=result});
            }
            case "preset_rename": {
                var result=await modules.Presets.RenameAsync(Arg(a,"presetId"),Arg(a,"name"),token);RefreshPresetCards();return OperationReply.Success(new {saved=true,preset=result});
            }
            case "preset_delete":modules.Presets.Delete(Arg(a,"presetId"));RefreshPresetCards();return OperationReply.Success(new {saved=true,removed=true});
            case "preset_export": {
                string path=AutomationArtifactPath(".zip");await modules.Presets.ExportAsync(Arg(a,"presetId"),path,token);return OperationReply.Success(new {path});
            }
            case "preset_import": {
                string[] roots=JsonSerializer.Deserialize<string[]>(modules.Settings.Get("automation.mediaRoots")??"[]")??[];
                string path=MediaAccessPolicy.ValidatePath(Arg(a,"path"),roots,[AppContext.BaseDirectory,store.Root]);
                if(!string.Equals(Path.GetExtension(path),".zip",StringComparison.OrdinalIgnoreCase))throw new OperationFault("invalid_argument","需要 ZIP 预设",null);
                var preview=await modules.Presets.PreviewPackageAsync(path,token);
                RequireAutomationRevision(a);
                var result=await modules.Presets.ImportAsync(path,new PresetImportOptions {MediaDirectory=Path.Combine(store.Root,"preset-media"),ExistingAssets=modules.Library.Assets().ToArray(),ExpectedFingerprint=preview.Fingerprint},token);
                RefreshPresetCards();return OperationReply.Success(new {saved=true,preset=result});
            }
            case "preset_apply": {
                var map=new Dictionary<string,string>();
                foreach(var entry in a.GetProperty("screens").EnumerateArray()) {
                    string source=entry.GetProperty("source").GetString()!,target=OperationScreen(entry.GetProperty("target").GetString()!).DeviceName;
                    if(!map.TryAdd(source,target)||map.Values.Count(v=>v==target)>1)throw new OperationFault("invalid_argument","显示器映射不能重复",null);
                }
                var document=await modules.Presets.PrepareApplyAsync(Arg(a,"presetId"),modules.Library.Assets(),token);
                if(map.Keys.Any(k=>!document.Screens.Contains(k)))throw new OperationFault("invalid_argument","未知预设显示器",null);
                if(document.Widgets?.Shortcuts.Any(s=>s.Kind!=ShortcutKind.Website)==true)throw new UnauthorizedAccessException("MCP 不能导入命令或文件夹入口");
                if(document.Selection.Widgets&&widgets!.State.Widgets.Any(w=>widgets.State.IsEditingLocked(w)))throw new OperationFault("widget_locked","请先解锁要替换的组件",null);
                RequireAutomationRevision(a);
                var result=modules.Presets.Apply(document,map,expectedRevision:Arg(a,"expectedRevision"),primaryScreen:Screen.PrimaryScreen?.DeviceName);
                effectSession.ClearRetained();automationUndo.Clear();scopedEffectUndo.Clear();
                string warnings=await ActivatePreset(result);RefreshPresetUndo();return OperationReply.Success(new {saved=true,result,warnings});
            }
            default:return await AutomationFiles(request,token);
        }
    }
}
