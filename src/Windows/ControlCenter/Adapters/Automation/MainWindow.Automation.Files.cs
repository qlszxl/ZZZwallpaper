using System.Text.Json;
using LumaWall.Application;
using LumaWall.Automation;
using LumaWall.Contracts.Steam;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,SteamWallpaperAsset> automationSteamAssets=new();
    private sealed record SourceDeletePlan(LibrarySourceFile File,string Revision,DateTimeOffset Expires);
    private readonly Dictionary<string,SourceDeletePlan> automationSourceDeletes=new();
    private async Task<OperationReply> AutomationFiles(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        if(request.Name=="app_quit") {
            if(importLifetime.Active!=null||automationJobs.Values.Any(j=>j.FinishedAt==null))throw new OperationFault("invalid_state","请先结束素材导入任务",null);
            FlushAutomationConfiguration();automationRestartScheduled=true;
            var timer=new Microsoft.UI.Xaml.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
            timer.Tick+=(_,_)=> {timer.Stop();quit?.Invoke();};timer.Start();
            return OperationReply.Success(new {saved=true,status="shutdown_scheduled"});
        }
        if(request.Name=="media_inspect_steam_project") {
            string path=AuthorizedMediaPath(Arg(a,"path"),directory:true);
            if(!File.Exists(Path.Combine(path,"project.json")))throw new OperationFault("invalid_argument","需要包含 project.json 的单个项目目录",null);
            var projects=await steamCatalog.DiscoverAsync([path],token);
            var project=projects.Projects.SingleOrDefault(p=>LibrarySourceFiles.SamePath(p.ProjectDirectory,path))??throw new OperationFault("invalid_argument","项目无效",null);
            var discovery=await steamCatalog.InspectAssetsAsync(project,token);
            int offset=a.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=a.TryGetProperty("limit",out var l)?l.GetInt32():100;
            var items=discovery.Assets.Skip(offset).Take(limit).ToArray();
            foreach(var item in items) {if(automationSteamAssets.Count>=1024)automationSteamAssets.Remove(automationSteamAssets.Keys.First());automationSteamAssets[item.SourceId]=item;}
            return OperationReply.Success(new {project=new {project.Title,project.Type,project.SourceId},total=discovery.Assets.Length,offset,items=items.Select(p=>new {sourceId=p.SourceId,p.Title,p.RelativePath,kind=p.Kind.ToString(),p.CanImport,p.RequiresExtraction,p.Bytes,p.Width,p.Height,p.Message}),issues=discovery.Issues});
        }
        if(request.Name=="media_import_steam_asset") {
            if(!automationSteamAssets.TryGetValue(Arg(a,"sourceId"),out var selected))throw new OperationFault("resource_not_found","请重新读取 Steam 项目素材",null);
            AuthorizedMediaPath(selected.Project.ProjectDirectory,directory:true);
            string? destination=Optional(a,"copyDirectory");if(destination!=null)destination=AuthorizedMediaPath(destination,directory:true);
            var prepared=await steamCatalog.PrepareAssetAsync(selected,destination,token);
            try {
                var reply=await ImportAutomationMedia(prepared.Path,token,()=>RequireAutomationRevision(a));
                if(!reply.IsError&&reply.Data.TryGetProperty("assetId",out var id))modules.Settings.Set(SteamSourceIdentity.SettingKey(id.GetString()!),JsonSerializer.Serialize(prepared.Source));
                return reply;
            } finally {
                // The extractor owns this newly created copy; never remove a reused/original file.
                if(prepared.Copied&&!prepared.Reused&&modules.Library.FindAssetByPath(prepared.Path)==null)try {File.Delete(prepared.Path);}catch(IOException) { }
            }
        }
        if(request.Name=="media_reveal_source") {
            var asset=modules.Library.Assets().FirstOrDefault(m=>m.Id==Arg(a,"assetId"))??throw new OperationFault("resource_not_found","素材不存在",null);
            librarySourceFiles.Reveal(asset.Path);return OperationReply.Success(new {assetId=asset.Id,opened=true});
        }
        if(request.Name=="media_preview_source_delete") {
            FlushAutomationConfiguration();string revision=store.ConfigurationRevision();
            var asset=modules.Library.Assets().FirstOrDefault(m=>m.Id==Arg(a,"assetId"))??throw new OperationFault("resource_not_found","素材不存在",null);
            string path=AuthorizedMediaPath(asset.Path); // Destructive actions require an explicit directory grant, not merely registration.
            if(SteamSourceIdentity.IsSteamManagedOriginal(path,LibraryFileSteamSource(asset)))throw new OperationFault("invalid_state","Steam 管理的原文件需在 Steam 中处理",null);
            var file=LibrarySourceFiles.Inspect(path);var references=await Task.Run(()=>modules.Presets.FindFileReferences(path),token);
            if(revision!=store.ConfigurationRevision())throw OperationFault.Conflict(revision,store.ConfigurationRevision(),"app_get_state");
            LibrarySourceFiles.RequireUnchanged(file);
            foreach(var old in automationSourceDeletes.Where(p=>p.Value.Expires<DateTimeOffset.UtcNow).Select(p=>p.Key).ToArray())automationSourceDeletes.Remove(old);
            if(automationSourceDeletes.Count>=8)automationSourceDeletes.Remove(automationSourceDeletes.Keys.First());
            string id=Guid.NewGuid().ToString("N");var expires=DateTimeOffset.UtcNow.AddMinutes(5);automationSourceDeletes[id]=new(file,revision,expires);
            var ids=modules.Library.Assets().Where(m=>LibrarySourceFiles.SamePath(m.Path,path)).Select(m=>m.Id).ToArray();
            return OperationReply.Success(new {deletePlanId=id,expectedRevision=revision,path,file.Bytes,expiresAt=expires,assetIds=ids,affectedScreens=store.Bindings().Where(b=>ids.Contains(b.AssetId)).Select(b=>b.ScreenId),presets=references.Presets,unreadablePresets=references.UnreadablePresets,undoable=false});
        }
        if(request.Name=="media_delete_source") {
            if(!Flag(a,"confirm"))throw new OperationFault("invalid_argument","需要明确确认删除",null);
            string id=Arg(a,"deletePlanId");
            if(!automationSourceDeletes.TryGetValue(id,out var plan)||plan.Expires<DateTimeOffset.UtcNow||plan.Revision!=store.ConfigurationRevision())throw new OperationFault("revision_conflict","删除预览已过期，请重新预览",null);
            AuthorizedMediaPath(plan.File.Path);LibrarySourceFiles.RequireUnchanged(plan.File);
            if(!LibraryFileOperationAvailable||automationJobs.Values.Any(j=>j.FinishedAt==null))throw new OperationFault("invalid_state","请先完成当前素材操作",null);
            libraryFileBusy=true;
            try {
                EndEffectEditing();using var blocked=wallpapers?.BlockSourceFile(plan.File.Path);
                if(wallpapers!=null)await wallpapers.StopSourceFileAsync(plan.File.Path);
                token.ThrowIfCancellationRequested();RequireAutomationRevision(a);LibrarySourceFiles.RequireUnchanged(plan.File);
                if(homePreviewRequest is { } preview&&LibrarySourceFiles.SamePath(preview.Asset.Path,plan.File.Path))homePreviewRequest=null;
                var result=store.DeleteSourceFile(plan.File.Path,()=>librarySourceFiles.Delete(plan.File,Flag(a,"permanent")));
                automationSourceDeletes.Remove(id);RefreshFolders();RefreshLibrary();LoadPlaybackSettings();RefreshUndo();RefreshStatus();
                return OperationReply.Success(new {saved=true,deleted=true,permanent=Flag(a,"permanent"),result,undoable=false});
            } catch(Exception error) when(!File.Exists(plan.File.Path)) {
                return new(true,ToolCatalog.Json(new {saved=true,deleted=true,status="partially_completed",error=error.Message,undoable=false}));
            } finally {libraryFileBusy=false;RefreshFolderControls();RefreshNativeLibraryDrop();}
        }
        return await AutomationDiagnostics(request,token);
    }
}
