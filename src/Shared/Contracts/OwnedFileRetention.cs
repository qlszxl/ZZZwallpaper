namespace LumaWall.Contracts;

// Only direct, recognizable generated files are eligible. Never recurse through
// directories or links; caller-selected originals and exports are not caches.
public static class OwnedFileRetention
{
    public static bool SafeDirectory(string directory)
    {
        try {
            for(var current=new DirectoryInfo(Path.GetFullPath(directory));current!=null;current=current.Parent)
                if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)return false;
            return Directory.Exists(directory);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) {return false;}
    }
    public static bool HexName(string name,string extension,int digits=32)=>
        name.EndsWith(extension,StringComparison.OrdinalIgnoreCase)&&name.Length==digits+extension.Length&&name[..digits].All(char.IsAsciiHexDigit);

    public static int Prune(string directory,Func<string,bool> owned,int maxFiles,long maxBytes,TimeSpan maxAge,
        IEnumerable<string>? protectedPaths=null,TimeSpan? recentGrace=null)
    {
        if(maxFiles<0||maxBytes<0)throw new ArgumentOutOfRangeException(nameof(maxFiles));
        if(!SafeDirectory(directory))return 0;
        try {
            var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var keep=(protectedPaths??[]).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files=new DirectoryInfo(root).GetFiles().Where(f=>owned(f.Name)&&(f.Attributes&FileAttributes.ReparsePoint)==0)
                .OrderByDescending(f=>f.LastWriteTimeUtc).ThenBy(f=>f.Name,StringComparer.Ordinal).ToArray();
            var now=DateTime.UtcNow;int count=files.Count(f=>keep.Contains(f.FullName)),deleted=0;
            long bytes=files.Where(f=>keep.Contains(f.FullName)).Sum(f=>f.Length);
            foreach(var file in files) {
                if(keep.Contains(file.FullName))continue;
                var observedWrite=file.LastWriteTimeUtc;
                bool recent=recentGrace is { } grace&&now-file.LastWriteTimeUtc<grace;
                if(recent||(now-file.LastWriteTimeUtc<=maxAge&&count<maxFiles&&bytes+file.Length<=maxBytes)) {count++;bytes+=file.Length;continue;}
                try {
                    if(!SafeDirectory(root))break;
                    file.Refresh();
                    if(!file.Exists||(file.Attributes&FileAttributes.ReparsePoint)!=0||!string.Equals(file.DirectoryName,root,StringComparison.OrdinalIgnoreCase))continue;
                    // Another preview may have reused this cache after enumeration.
                    if(file.LastWriteTimeUtc>observedWrite) {count++;bytes+=file.Length;continue;}
                    file.Delete();deleted++;
                } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {count++;bytes+=file.Length;}
            }
            return deleted;
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {return 0;}
    }
    public static void Touch(string path)
    {
        try {
            // Amortize timestamp writes on rapidly repeated render commands.
            if(File.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddMinutes(-1))File.SetLastWriteTimeUtc(path,DateTime.UtcNow);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
    public static void Pixels(string directory,string extension,string? current=null)
    {
        if(current!=null)Touch(current);
        Prune(directory,n=>HexName(n,extension,64),128,128L*1024*1024,TimeSpan.FromDays(7),
            current==null?null:[current],TimeSpan.FromMinutes(2));
        Prune(directory,n=>HexName(n,".tmp"),0,0,TimeSpan.Zero,recentGrace:TimeSpan.FromDays(1));
    }
}
