namespace LumaWall.Contracts.Steam;

public enum SteamProjectStatus { Supported, Unsupported, Invalid }

public sealed record SteamWallpaperProject(
    string ProjectDirectory, string SourceId, string? WorkshopId, string Title, string Type,
    string? MediaPath, string? PreviewPath, SteamProjectStatus Status, string Message, long Bytes, DateTime ModifiedUtc);

public sealed record SteamDiscoveryIssue(string Path, string Message);
public sealed record SteamDiscoveryResult(SteamWallpaperProject[] Projects, SteamDiscoveryIssue[] Issues);

// Stored only after the existing media importer has successfully committed.
// It describes provenance; it grants no permission to delete a source file.
public sealed record SteamWallpaperSource(
    string SourceId, string ProjectDirectory, string? WorkshopId, string ProjectType,
    string OriginalMediaPath, string ImportedPath, bool Copied, string? ContentSha256)
{
    public long Bytes { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string? ProjectSourceId { get; init; }
    public string? RelativePath { get; init; }
    public string? PackagePath { get; init; }
}

public sealed record SteamPreparedImport(string Path, SteamWallpaperSource Source, bool Copied, bool Reused);

public static class SteamSourceIdentity
{
    public static string SettingKey(string assetId) => "media.steam.v1:" + assetId;

    public static bool IsSteamManagedOriginal(string path, SteamWallpaperSource? source = null)
    {
        if (IsManagedPath(path)) return true;
        try
        {
            return source is { Copied: false } && Path.IsPathFullyQualified(path) &&
                Path.GetFullPath(path).Equals(Path.GetFullPath(source.ImportedPath), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(path).Equals(Path.GetFullPath(source.OriginalMediaPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static bool IsManagedPath(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var parts = Path.GetFullPath(path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool Is(int offset, string value) => i + offset < parts.Length && parts[i + offset].Equals(value, StringComparison.OrdinalIgnoreCase);
                if (Is(0, "steamapps") && Is(1, "workshop") && Is(2, "content") && Is(3, "431960") &&
                    i + 5 < parts.Length && parts[i + 4].Length > 0 && parts[i + 4].All(char.IsAsciiDigit)) return true;
                if (Is(0, "wallpaper_engine") && Is(1, "projects") && (Is(2, "myprojects") || Is(2, "defaultprojects")) &&
                    i + 4 < parts.Length) return true;
            }
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        return false;
    }
}
