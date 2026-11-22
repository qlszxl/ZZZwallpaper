using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed class AutomationJob {
        public string Id {get;}=Guid.NewGuid().ToString("N");
        public string OperationId="";
        public CancellationTokenSource Cancellation {get;}=new();
        public string Status="queued";
        public string? Error;
        public int Total;
        public string InitialRevision="";
        public readonly List<object> Results=[];
        public DateTimeOffset StartedAt=DateTimeOffset.UtcNow;
        public DateTimeOffset? FinishedAt;
    }
    private readonly Dictionary<string,AutomationJob> automationJobs=new();
    private string AuthorizedMediaPath(string path,bool directory=false,bool registered=false)
    {
        string[] roots=JsonSerializer.Deserialize<string[]>(modules.Settings.Get("automation.mediaRoots")??"[]")??[];
        if(registered&&modules.Library.FindAssetByPath(path)!=null)roots=[..roots,Path.GetDirectoryName(Path.GetFullPath(path))!];
        var full=MediaAccessPolicy.ValidatePath(path,roots,[AppContext.BaseDirectory,store.Root],directory);
        return directory?full:MediaFormats.LocalImportPath(full);
    }
    private OperationReply ReadAutomationJob(OperationRequest request)
    {
        var a=request.Arguments;if(!automationJobs.TryGetValue(Arg(a,"jobId"),out var job))throw new OperationFault("resource_not_found","任务不存在或属于旧实例",null);
        if(request.Name=="task_cancel_job"&&job.FinishedAt==null) {job.Status="cancelling";job.Cancellation.Cancel();}
        int offset=a.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=a.TryGetProperty("limit",out var l)?l.GetInt32():50;
        return OperationReply.Success(new {jobId=job.Id,status=job.Status,startedAt=job.StartedAt,finishedAt=job.FinishedAt,total=job.Total,processed=job.Results.Count,offset,items=job.Results.Skip(offset).Take(limit).ToArray(),error=job.Error,completedItemsRetained=true});
    }
    private OperationReply StartAutomationImport(JsonElement a)
    {
        if(automationJobs.Values.Any(j=>j.FinishedAt==null)||importLifetime.Active!=null)throw new OperationFault("invalid_state","已有素材导入任务正在运行",null);
        if(a.TryGetProperty("paths",out _)==a.TryGetProperty("folder",out _))throw new OperationFault("invalid_argument","必须提供 paths 或 folder 其中之一",null);
        string? folder=Optional(a,"folder");if(folder!=null)folder=AuthorizedMediaPath(folder,true);
        string[] paths=Ids(a,"paths");
        if(automationJobs.Count>=32) {var old=automationJobs.FirstOrDefault(j=>j.Value.FinishedAt!=null);if(old.Key!=null) {old.Value.Cancellation.Dispose();automationJobs.Remove(old.Key);}}
        var job=new AutomationJob {OperationId=store.AutomationOperationId!,InitialRevision=store.ConfigurationRevision()};automationJobs.Add(job.Id,job);
        _=RunAutomationImport(job,paths,folder,Flag(a,"recursive"));
        return OperationReply.Success(new {jobId=job.Id,status="running",cancelPolicy="retain_completed_items"});
    }
    private async Task RunAutomationImport(AutomationJob job,string[] files,string? folder,bool recursive)
    {
        await Task.Yield();job.Status="running";var token=job.Cancellation.Token;
        try {
            var paths=files.ToList();
            if(folder!=null) {
                var scan=await Task.Run(()=>ScanAutomationFolder(folder,recursive,token),token);paths=scan.Paths;job.Results.AddRange(scan.Skipped);
            }
            if(paths.Count>10000)throw new OperationFault("invalid_argument","批量导入最多 10000 个文件，请缩小文件夹范围",null);
            job.Total=paths.Count+job.Results.Count;var members=new List<string>();
            foreach(string path in paths) {
                token.ThrowIfCancellationRequested();if(automationClosing||automationServer==null)throw new OperationCanceledException(token);
                try {
                    AuthorizedMediaPath(path);FlushHistoryEdits();string itemRevision=store.ConfigurationRevision();
                    var reply=await ImportAutomationMedia(path,token,()=>RequireAutomationRevision(ToolCatalog.Json(new {expectedRevision=itemRevision})));
                    if(!reply.IsError&&reply.Data.TryGetProperty("assetId",out var id))members.Add(id.GetString()!);
                    job.Results.Add(new {path,status=reply.IsError?"failed":"completed",result=reply.Data});
                } catch(OperationCanceledException) {throw;}
                catch(Exception ex) {job.Results.Add(new {path,status="failed",error=ex.Message,result=OperationErrors.FromException(ex).Data});}
            }
            if(folder!=null) {var source=modules.Library.SaveFolder(new(Guid.NewGuid().ToString("N"),folder,recursive));modules.Library.ReplaceFolderAssets(source.Id,modules.Library.FolderAssets(source.Id).Select(m=>m.Id).Concat(members));RefreshFolders();}
            job.Status=job.Results.Any(r=>JsonSerializer.SerializeToElement(r).TryGetProperty("status",out var s)&&s.GetString()=="failed")?"completed_with_errors":"completed";
        } catch(OperationCanceledException) {job.Status="cancelled";}
        catch(Exception ex) {job.Status="failed";job.Error=ex.Message;}
        finally {job.FinishedAt=DateTimeOffset.UtcNow;if(!automationClosing) {string revision=store.ConfigurationRevision();automationJournal.Complete(job.OperationId,new(job.Status is "failed" or "completed_with_errors",ToolCatalog.Json(new {jobId=job.Id,status=job.Status,total=job.Total,processed=job.Results.Count,error=job.Error,saved=revision!=job.InitialRevision,completedItemsRetained=true})),revision);}}
    }
    private static (List<string> Paths,List<object> Skipped) ScanAutomationFolder(string root,bool recursive,CancellationToken token) {
        var paths=new List<string>();var skipped=new List<object>();var pending=new Stack<string>();pending.Push(root);int visited=0;
        while(pending.TryPop(out string? directory)) {
            token.ThrowIfCancellationRequested();
            try {
                foreach(string path in Directory.EnumerateFileSystemEntries(directory)) {
                    token.ThrowIfCancellationRequested();if(++visited>10000)throw new OperationFault("invalid_argument","扫描最多 10000 个目录项，请缩小范围",null);
                    try {
                        var attributes=File.GetAttributes(path);
                        if((attributes&FileAttributes.ReparsePoint)!=0) {skipped.Add(new {path,status="skipped",reason="reparse_point"});continue;}
                        if((attributes&FileAttributes.Directory)!=0) {if(recursive)pending.Push(path);else skipped.Add(new {path,status="skipped",reason="recursive_disabled"});continue;}
                        if(!MediaFormats.Videos.Concat(MediaFormats.Images).Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase)) {skipped.Add(new {path,status="skipped",reason="unsupported_format"});continue;}
                        paths.Add(path);
                    }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {skipped.Add(new {path,status="skipped",reason="unreadable",error=ex.Message});}
                }
            }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {skipped.Add(new {path=directory,status="skipped",reason="unreadable_directory",error=ex.Message});}
        }
        return(paths,skipped);
    }
    private async Task<OperationReply> RefreshAutomationMedia(OperationRequest request,CancellationToken token)
    {
        if(importLifetime.Active!=null||automationJobs.Values.Any(j=>j.FinishedAt==null))throw new OperationFault("invalid_state","正在导入素材，请稍后刷新",null);
        var a=request.Arguments;var media=modules.Library.Assets().FirstOrDefault(m=>m.Id==Arg(a,"assetId"))??throw new OperationFault("resource_not_found","素材不存在","arguments.assetId");
        string path=request.Name=="media_relink"?AuthorizedMediaPath(Arg(a,"path")):AuthorizedMediaPath(media.Path,registered:true);
        var conflict=modules.Library.FindAssetByPath(path);if(conflict!=null&&conflict.Id!=media.Id)throw new OperationFault("invalid_argument","此路径已由另一个素材 ID 使用",null);
        var result=await ProbePreservingPoster(path,media.Id,token,beforeCommit:()=>RequireAutomationRevision(a));token.ThrowIfCancellationRequested();var file=new FileInfo(path);
        var updated=media with {Path=path,Bytes=file.Length,Width=result.Width,Height=result.Height,Duration=result.Duration,Codec=result.Codec,Color=result.Color,Decoder=result.Decoder};
        modules.Library.Upsert(updated);modules.Library.RememberAssetAudio(updated,result.HasAudio);RememberPosterSelection(media.Id,path);RefreshLibrary();
        return OperationReply.Success(new {assetId=media.Id,saved=true,available=true,metadata=updated,poster=ReadPosterChoice(media.Id),playbackRestarted=false});
    }
}
