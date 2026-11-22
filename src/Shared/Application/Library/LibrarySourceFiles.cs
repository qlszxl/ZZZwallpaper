using System.Text.Json;
using LumaWall.Contracts.Steam;

namespace LumaWall.Application;

public sealed record LibrarySourceFile(string Path, long Bytes, long ModifiedUtcTicks, long CreatedUtcTicks);

public interface ILibrarySourceFileOperations
{
    void Reveal(string path);
    void Delete(LibrarySourceFile source, bool permanently);
    void ManageSteam(string path, string? workshopId);
}

public static class LibrarySourceFiles
{
    public static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("素材路径必须是完整的文件路径。");
        string full = Path.GetFullPath(path);
        if (Path.GetPathRoot(full) == full || full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\.\", StringComparison.Ordinal) || full[Path.GetPathRoot(full)!.Length..].Contains(':'))
            throw new IOException("不支持对这个路径执行文件操作。");
        return Path.TrimEndingDirectorySeparator(full);
    }

    public static bool SamePath(string left, string right)
    {
        try { return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { return false; }
    }

    public static SteamWallpaperSource? ReadSteamSource(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SteamWallpaperSource>(json); }
        catch (JsonException) { throw new IOException("素材的 Steam 来源记录无法读取；请先重新关联来源，未执行文件删除。"); }
    }

    public static LibrarySourceFile Inspect(string path)
    {
        string full = Normalize(path);
        CheckPath(full);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("素材源文件已不存在。可使用“移除引用”清理库内记录。", full);
        return new(full, info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
    }

    public static void RequireUnchanged(LibrarySourceFile expected)
    {
        var current = Inspect(expected.Path);
        if (current != expected) throw new IOException("确认期间素材文件已改变，请重新打开删除确认。");
    }

    private static void CheckPath(string path)
    {
        string? cursor = path;
        while (cursor != null)
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                var attributes = File.GetAttributes(cursor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("为避免删除链接指向的其他文件，请直接打开原文件位置后管理。");
                if (cursor == path && (attributes & FileAttributes.Directory) != 0)
                    throw new IOException("只能删除选中的素材文件，不能删除整个文件夹。");
            }
            cursor = Path.GetDirectoryName(cursor);
        }
    }
}
