namespace LumaWall.Automation;

public static class MediaAccessPolicy
{
    public static string ValidatePath(string path,IEnumerable<string> roots,IEnumerable<string> protectedRoots,bool directory=false)
    {
        if(!Path.IsPathFullyQualified(path)||path.StartsWith("\\\\")||path.Any(char.IsControl)||path.AsSpan(2).Contains(':'))throw new UnauthorizedAccessException("仅允许授权的本机完整路径");
        string full=Path.GetFullPath(path);
        static bool Within(string path,string root) {root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));return path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        if(protectedRoots.Any(r=>Within(full,r))||!roots.Any(r=>Within(full,r)))throw new UnauthorizedAccessException("路径不在用户授权素材目录内，或位于受保护的程序/数据目录");
        for(string? entry=full;entry!=null;entry=Path.GetDirectoryName(entry)) {
            if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("素材路径不允许符号链接或目录联接");
        }
        if(directory?!Directory.Exists(full):!File.Exists(full))throw new IOException("素材路径不存在");
        return full;
    }
}
