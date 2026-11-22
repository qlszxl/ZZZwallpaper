using LumaWall.Application;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

internal sealed class ImportSummary
    {
        public int Added,Updated,Existing,Failed;
        public string? LastId;
        public List<string> Members {get;}=new();
        public List<string> Errors {get;}=new();
        public string Text => UiLanguage.Format($"新增 {Added} 个，更新 {Updated} 个，已有 {Existing} 个，跳过 {Failed} 个");
    }
internal sealed record FolderScan(List<string> Paths,int SkippedDirectories);

// Imports report progress as data. Pickers, notices and selection stay in the UI.
internal sealed class LibraryImportCoordinator(LibraryService library,PosterCatalogService posters,LibraryImportLifetime lifetime,Func<string,string> translate)
{
    private static string NormalizePath(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static FolderScan ScanFolder(string path,bool recursive,CancellationToken token)
    {
        if(!Directory.Exists(path))throw new DirectoryNotFoundException(UiLanguage.Text("文件夹不存在或暂时不可访问，原有引用已保留。"));
        var pendingDirectories=new Stack<string>();pendingDirectories.Push(NormalizePath(path));
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int skipped=0;
        while(pendingDirectories.Count>0)
        {
            token.ThrowIfCancellationRequested();string directory=pendingDirectories.Pop();
            try
            {
                foreach(string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes=File.GetAttributes(entry);
                        if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                        if((attributes&FileAttributes.Directory)!=0) {if(recursive)pendingDirectories.Push(entry);}
                        else if(MediaFormats.Supported(entry))files.Add(NormalizePath(entry));
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {AppLog.Warning("Library","Skipping unreadable file",ex);skipped++;}
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {AppLog.Warning("Library","Skipping unreadable directory",ex);skipped++;}
        }
        return new(files.OrderBy(file=>file,StringComparer.OrdinalIgnoreCase).ToList(),skipped);
    }

    public async Task ImportPaths(IReadOnlyList<string> paths,bool refresh,ImportSummary summary,CancellationToken token,Action<string> progress,Action? beforeCommit=null)
    {
        var distinct=paths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for(int index=0;index<distinct.Count;index++)
        {
            lifetime.Check(token);string path=distinct[index];
            progress(UiLanguage.Format($"正在处理 {index+1} / {distinct.Count}：{Path.GetFileName(path)}"));
            try
            {
                if(!MediaFormats.Supported(path))throw new ArgumentException(UiLanguage.Text("不支持此素材格式。"));
                var existing=library.FindAssetByPath(path);
                if(existing!=null&&!refresh&&posters.PosterSelectionCurrent(existing.Id,path))
                {
                    summary.Existing++;summary.Members.Add(existing.Id);summary.LastId=existing.Id;
                    if(index%25==0)await Task.Yield();
                    continue;
                }
                string id=existing?.Id??Guid.NewGuid().ToString("N");
                var result=await posters.ProbePreservingPoster(path,id,token,beforeCommit:beforeCommit);lifetime.Check(token);
                var info=new FileInfo(path);
                var asset=new MediaAsset(id,info.FullName,Path.GetFileNameWithoutExtension(path),info.Length,result.Duration,result.Width,result.Height,result.Codec,result.Color,result.Decoder);
                library.Upsert(asset);library.RememberAssetAudio(asset,result.HasAudio);summary.Members.Add(id);summary.LastId=id;
                if(existing==null)summary.Added++;else summary.Updated++;
            }
            catch(LumaWall.Automation.OperationFault) {throw;}
            catch(OperationCanceledException) when(token.IsCancellationRequested||lifetime.IsShutdown) {throw;}
            catch(Exception ex)
            {
                AppLog.Error("Library","Import failed: "+Path.GetFileName(path),ex);
                lifetime.Check(token);summary.Failed++;
                // Keep a previously imported reference when a refreshed file is temporarily busy or unreadable.
                var previous=library.FindAssetByPath(path);if(previous!=null)summary.Members.Add(previous.Id);
                if(summary.Errors.Count<3)summary.Errors.Add(Path.GetFileName(path)+"："+(ex is OperationCanceledException?UiLanguage.Text("读取媒体超时"):translate(ex.Message)));
            }
        }
    }

}
