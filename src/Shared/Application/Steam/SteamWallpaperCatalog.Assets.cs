using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Steam;

namespace LumaWall.Application.Steam;

public sealed partial class SteamWallpaperCatalog
{
    private const int MaxAssets = 4096, MaxAssetFiles = 20_000, MaxAssetFolders = 2048, MaxAssetDepth = 16;

    public Task<SteamAssetDiscoveryResult> InspectAssetsAsync(SteamWallpaperProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Task.Run(() => InspectAssets(project, cancellationToken), cancellationToken);
    }

    private static SteamAssetDiscoveryResult InspectAssets(SteamWallpaperProject selected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var project = CurrentProject(selected, token);
        if (project.Status == SteamProjectStatus.Supported && project.MediaPath != null)
            return new([CreateAsset(project, project.MediaPath, null, null, token)], []);
        if (project.Type is not ("scene" or "web")) return new([], [new(project.ProjectDirectory, "不支持此项目类型。")]);
        var assets = new List<SteamWallpaperAsset>();
        var issues = new List<SteamDiscoveryIssue>();
        string? preview = DeclaredPreview(project, token);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((project.ProjectDirectory, 0));
        int entries = 0, files = 0, folders = 0, packages = 0;
        bool exhausted = false;
        void Issue(string path, string message) { if (issues.Count < 256) issues.Add(new(path, message)); }
        while (!exhausted && queue.TryDequeue(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (++folders > MaxAssetFolders) { Issue(folder.Path, "项目目录超过 2048 个，剩余目录未扫描。"); break; }
            try
            {
                CheckPath(folder.Path);
                foreach (string path in Directory.EnumerateFileSystemEntries(folder.Path))
                {
                    token.ThrowIfCancellationRequested();
                    // Count rejected links/directories too; a directory full of
                    // skipped entries must not evade the scan budget.
                    if (++entries > MaxAssetFiles)
                    { Issue(path, "项目文件超过 20000 个，剩余文件未扫描。"); exhausted = true; break; }
                    try
                    {
                        CheckPath(path);
                        if (Directory.Exists(path))
                        {
                            if (folder.Depth >= MaxAssetDepth) { Issue(path, "目录超过 16 层，未扫描。"); continue; }
                            if (queue.Count + folders >= MaxAssetFolders) { Issue(path, "项目目录超过 2048 个，剩余目录未扫描。"); continue; }
                            queue.Enqueue((path, folder.Depth + 1)); continue;
                        }
                        if (++files > MaxAssetFiles || assets.Count >= MaxAssets)
                        { Issue(path, "项目素材过多，剩余文件未扫描。"); exhausted = true; break; }
                        if (Paths.Equals(path, preview)) continue;
                        string extension = Path.GetExtension(path).ToLowerInvariant();
                        if (MediaFormats.Supported(path) || extension == ".tex")
                            assets.Add(CreateAsset(project, path, null, null, token));
                        else if (extension == ".pkg")
                        {
                            if (++packages > 128) { Issue(path, "素材包超过 128 个，剩余素材包未扫描。"); continue; }
                            using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            foreach (var entry in SteamPackageReader.Read(package, token))
                            {
                                token.ThrowIfCancellationRequested();
                                if (assets.Count >= MaxAssets) { Issue(path, "项目素材超过 4096 个，剩余素材未扫描。"); exhausted = true; break; }
                                if (preview != null && Paths.Equals(ProjectFile(project.ProjectDirectory, entry.Name), preview)) continue;
                                if (MediaFormats.Supported(entry.Name) || Path.GetExtension(entry.Name).Equals(".tex", StringComparison.OrdinalIgnoreCase))
                                    assets.Add(CreateAsset(project, null, path, entry, token, package));
                            }
                        }
                    }
                    catch (Exception error) when (FileError(error)) { Issue(path, error.Message); }
                }
            }
            catch (Exception error) when (FileError(error)) { Issue(folder.Path, error.Message); }
        }
        return new(assets.OrderBy(a => !a.CanImport).ThenBy(a => a.RelativePath, Paths).ToArray(), issues.ToArray());
    }

    private static SteamWallpaperProject CurrentProject(SteamWallpaperProject selected, CancellationToken token)
    {
        string directory = SafePath(selected.ProjectDirectory);
        var project = ReadProject(directory, token);
        if (project.Status == SteamProjectStatus.Invalid) throw new InvalidDataException(project.Message);
        if (project.SourceId != selected.SourceId || project.Type != selected.Type)
            throw new IOException("项目已变化，请重新扫描。");
        return project;
    }

    private static string? DeclaredPreview(SteamWallpaperProject project, CancellationToken token)
    {
        using var json = JsonDocument.Parse(ReadText(Path.Combine(project.ProjectDirectory, "project.json"), MaxJsonBytes, token));
        if (!json.RootElement.TryGetProperty("preview", out var value) || value.ValueKind != JsonValueKind.String) return null;
        try { return ProjectFile(project.ProjectDirectory, value.GetString()!); }
        catch (Exception error) when (FileError(error)) { return null; }
    }

    private static SteamWallpaperAsset CreateAsset(SteamWallpaperProject project, string? mediaPath, string? packagePath,
        SteamPackageReader.Entry? entry, CancellationToken token, Stream? openPackage = null)
    {
        string source = packagePath ?? mediaPath!;
        CheckPath(source);
        var file = new FileInfo(source);
        if (!file.Exists) throw new FileNotFoundException("素材文件不存在。");
        string relativeFile = Path.GetRelativePath(project.ProjectDirectory, source).Replace('\\', '/');
        ProjectFile(project.ProjectDirectory, relativeFile);
        string relative = entry == null ? relativeFile : relativeFile + "!/" + entry.Name;
        string extension = Path.GetExtension(entry?.Name ?? source).ToLowerInvariant();
        bool texture = extension == ".tex";
        var kind = texture ? SteamAssetKind.Texture : entry == null ? SteamAssetKind.LooseFile : SteamAssetKind.PackageFile;
        string id = entry == null && !texture && Paths.Equals(project.MediaPath, source) ? project.SourceId :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.SourceId + "|" + relative.ToUpperInvariant()))).ToLowerInvariant();
        string title = Path.GetFileNameWithoutExtension(entry?.Name ?? source);
        long bytes = entry?.Bytes ?? file.Length;
        string message = ""; int width = 0, height = 0;
        SteamProjectStatus status = SteamProjectStatus.Supported;
        if (bytes == 0) { status = SteamProjectStatus.Invalid; message = "素材文件为空。"; }
        else if (texture)
        {
            try
            {
                using var ownInput = openPackage == null ? new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                using var range = new SteamAssetRangeStream(openPackage ?? ownInput!, entry?.Offset ?? 0, bytes);
                var info = SteamTextureReader.Inspect(range, token);
                extension = info.Extension; width = info.Width; height = info.Height;
            }
            catch (NotSupportedException error) { status = SteamProjectStatus.Unsupported; message = error.Message; }
            catch (Exception error) when (FileError(error)) { status = SteamProjectStatus.Invalid; message = error.Message; }
        }
        return new(project, id, relative, title, kind, mediaPath, packagePath, entry?.Name, extension, bytes, file.LastWriteTimeUtc, status, message)
        { ContainerBytes = file.Length, Width = width, Height = height };
    }

    private static SteamWallpaperAsset CurrentAsset(SteamWallpaperAsset selected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var project = CurrentProject(selected.Project, token);
        if (project.Type is not ("video" or "image" or "scene" or "web"))
            throw new InvalidDataException("不支持此项目类型。");
        string original = selected.PackagePath ?? selected.MediaPath ?? throw new InvalidDataException("素材路径缺失。");
        string source = ProjectFile(project.ProjectDirectory, Path.GetRelativePath(project.ProjectDirectory, SafePath(original)));
        CheckPath(source);
        if (project.Type is "scene" or "web" && Paths.Equals(source, DeclaredPreview(project, token)))
            throw new InvalidDataException("项目预览图不是场景素材。");
        SteamPackageReader.Entry? entry = null;
        using var package = selected.PackagePath == null ? null : new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (package != null)
        {
            if (!Path.GetExtension(source).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("素材包扩展名无效。");
            entry = SteamPackageReader.Read(package, token).SingleOrDefault(e => Paths.Equals(e.Name, selected.PackageEntry))
                ?? throw new IOException("包内素材已变化，请重新扫描。");
            string? preview = DeclaredPreview(project, token);
            if (preview != null && Paths.Equals(ProjectFile(project.ProjectDirectory, entry.Name), preview))
                throw new InvalidDataException("项目预览图不是场景素材。");
        }
        string name = entry?.Name ?? source;
        if (!MediaFormats.Supported(name) && !Path.GetExtension(name).Equals(".tex", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("此文件不是可导入的图片或视频素材。");
        if (project.Type is "video" or "image" && (!Paths.Equals(project.MediaPath, source) || entry != null))
            throw new InvalidDataException("仅可导入此项目声明的主素材。");
        var current = CreateAsset(project, package == null ? source : null, package == null ? null : source, entry, token, package);
        if (!current.CanImport) throw new InvalidDataException(current.Message);
        if (!selected.CanImport || current.SourceId != selected.SourceId || current.Kind != selected.Kind ||
            current.RelativePath != selected.RelativePath || current.Bytes != selected.Bytes ||
            current.ContainerBytes != selected.ContainerBytes || current.ModifiedUtc != selected.ModifiedUtc ||
            current.OutputExtension != selected.OutputExtension || current.Width != selected.Width || current.Height != selected.Height)
            throw new IOException("素材已变化，请重新扫描。");
        return current;
    }
}
