namespace LumaWall.Contracts.Steam;

public enum SteamAssetKind { LooseFile, PackageFile, Texture }

public sealed record SteamWallpaperAsset(
    SteamWallpaperProject Project, string SourceId, string RelativePath, string Title,
    SteamAssetKind Kind, string? MediaPath, string? PackagePath, string? PackageEntry,
    string OutputExtension, long Bytes, DateTime ModifiedUtc, SteamProjectStatus Status, string Message)
{
    public long ContainerBytes { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool RequiresExtraction => Kind != SteamAssetKind.LooseFile;
    public bool RequiresDestination => RequiresExtraction;
    public bool CanImport => Status == SteamProjectStatus.Supported;
    public bool IsImage => MediaFormats.IsImage("asset" + OutputExtension);
}

public sealed record SteamAssetDiscoveryResult(SteamWallpaperAsset[] Assets, SteamDiscoveryIssue[] Issues);
