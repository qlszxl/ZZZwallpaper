using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Steam;

namespace LumaWall.Application.Steam;

/// <summary>Local project inspection only. Never downloads, launches projects, or probes user directories implicitly.</summary>
public sealed partial class SteamWallpaperCatalog
{
    private const int MaxProjects = 10_000, MaxRoots = 128, MaxJsonBytes = 1024 * 1024, MaxVdfBytes = 2 * 1024 * 1024;
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public Task<SteamDiscoveryResult> DiscoverAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        // Bound caller enumeration too; an unbounded root list is not a filesystem scan.
        var inputs = roots.Take(MaxRoots + 1).ToArray();
        if (inputs.Length > MaxRoots) throw new ArgumentException("一次最多检查 128 个 Steam 来源目录。", nameof(roots));
        return Task.Run(() => Discover(inputs, cancellationToken), cancellationToken);
    }

    private static SteamDiscoveryResult Discover(string[] inputs, CancellationToken token)
    {
        var pending = new Queue<string>(inputs);
        var visited = new HashSet<string>(Paths);
        var projectPaths = new HashSet<string>(Paths);
        var collections = new HashSet<string>(Paths);
        var issues = new List<SteamDiscoveryIssue>();
        void Issue(string path, Exception error) { if (issues.Count < 256) issues.Add(new(path, error.Message)); }
        while (pending.TryDequeue(out string? input))
        {
            token.ThrowIfCancellationRequested();
            string root;
            try
            {
                root = SafePath(input);
                if (!visited.Add(root)) continue;
                if (visited.Count > MaxRoots) { issues.Add(new(root, "Steam 库数量超过 128，剩余目录未扫描。")); break; }
                if (!Directory.Exists(root)) continue;
                CheckPath(root);
                if (File.Exists(Path.Combine(root, "project.json"))) { AddProject(root); continue; }
                if (Path.GetFileName(root).Equals("431960", StringComparison.OrdinalIgnoreCase)) { AddCollection(root); continue; }
                if (Path.GetFileName(root).Equals("myprojects", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(root).Equals("defaultprojects", StringComparison.OrdinalIgnoreCase)) { AddCollection(root); continue; }
                string apps = Path.GetFileName(root).Equals("steamapps", StringComparison.OrdinalIgnoreCase) ? root : Path.Combine(root, "steamapps");
                AddCollection(Path.Combine(apps, "workshop", "content", "431960"));
                AddCollection(Path.Combine(apps, "common", "wallpaper_engine", "projects", "myprojects"));
                AddCollection(Path.Combine(apps, "common", "wallpaper_engine", "projects", "defaultprojects"));
                // Also accept the Wallpaper Engine installation or projects folder.
                AddCollection(Path.Combine(root, "projects", "myprojects"));
                AddCollection(Path.Combine(root, "projects", "defaultprojects"));
                AddCollection(Path.Combine(root, "myprojects"));
                AddCollection(Path.Combine(root, "defaultprojects"));
                foreach (string vdf in new[] { Path.Combine(apps, "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
                {
                    if (!File.Exists(vdf)) continue;
                    try
                    {
                        foreach (string library in SteamLibraryFolders.Parse(ReadText(vdf, MaxVdfBytes, token), token))
                        {
                            if (pending.Count + visited.Count >= MaxRoots) { issues.Add(new(vdf, "Steam 库数量超过 128，剩余目录未扫描。")); break; }
                            pending.Enqueue(library);
                        }
                    }
                    catch (Exception error) when (FileError(error)) { Issue(vdf, error); }
                }
            }
            catch (Exception error) when (FileError(error)) { Issue(input ?? "", error); }
        }
        var projects = new List<SteamWallpaperProject>();
        foreach (string directory in projectPaths.OrderBy(p => p, Paths))
        {
            token.ThrowIfCancellationRequested();
            projects.Add(ReadProject(directory, token));
        }
        return new(projects.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.ProjectDirectory, Paths).ToArray(), issues.ToArray());

        void AddProject(string path)
        {
            if (projectPaths.Count >= MaxProjects)
            {
                if (!issues.Any(i => i.Message == "项目超过 10000 个，剩余项目未扫描。")) issues.Add(new(path, "项目超过 10000 个，剩余项目未扫描。"));
                return;
            }
            projectPaths.Add(path);
        }
        void AddCollection(string path)
        {
            if (!collections.Add(path) || !Directory.Exists(path)) return;
            try
            {
                CheckPath(path);
                int seen = 0;
                foreach (string directory in Directory.EnumerateDirectories(path))
                {
                    token.ThrowIfCancellationRequested();
                    if (++seen > MaxProjects || projectPaths.Count >= MaxProjects) { AddProject(directory); break; }
                    try { CheckPath(directory); if (File.Exists(Path.Combine(directory, "project.json"))) AddProject(directory); }
                    catch (Exception error) when (FileError(error)) { Issue(directory, error); }
                }
            }
            catch (Exception error) when (FileError(error)) { Issue(path, error); }
        }
    }

    private static SteamWallpaperProject ReadProject(string directory, CancellationToken token)
    {
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).ToLowerInvariant();
        string? workshop = WorkshopId(directory);
        string title = Path.GetFileName(directory), type = "";
        string? preview = null;
        try
        {
            CheckPath(directory);
            using var json = JsonDocument.Parse(ReadText(Path.Combine(directory, "project.json"), MaxJsonBytes, token), new() { MaxDepth = 32 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("project.json 必须是对象。");
            // Reject ambiguous duplicate fields, rather than allowing two parsers to choose differently.
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in json.RootElement.EnumerateObject())
                if (!keys.Add(property.Name)) throw new InvalidDataException("project.json 含重复字段。");
            string Field(string name)
            {
                if (!json.RootElement.TryGetProperty(name, out var value)) return "";
                if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"project.json 的 {name} 必须是文字。");
                return value.GetString()!;
            }
            string named = Field("title").Trim();
            if (named.Length > 0) title = named.Length <= 200 ? named : named[..200];
            if (title.Any(char.IsControl)) throw new InvalidDataException("项目名称包含控制字符。");
            type = Field("type").Trim().ToLowerInvariant();
            // Preview metadata is optional, never an import candidate. Invalid,
            // linked, animated, or oversized previews are omitted independently.
            try
            {
                string candidate = ProjectFile(directory, Field("preview"));
                if (MediaFormats.IsImage(candidate))
                {
                    CheckPath(candidate);
                    var image = new FileInfo(candidate);
                    if (image.Exists && image.Length is > 0 and <= 16 * 1024 * 1024) preview = candidate;
                }
            }
            catch (Exception error) when (FileError(error)) { }
            if (type is not ("video" or "image"))
                return new(directory, id, workshop, title, type, null, preview, SteamProjectStatus.Unsupported,
                    type is "scene" or "web" ? "可选择项目内素材。" : "不支持此项目类型。", 0, default);
            string relative = Field("file");
            string media = ProjectFile(directory, relative);
            if (!MediaFormats.Supported(media) || (type == "image") != MediaFormats.IsImage(media))
                throw new InvalidDataException("项目声明的主文件不是此类型支持的图片或视频。");
            CheckPath(media);
            var info = new FileInfo(media);
            if (!info.Exists) throw new FileNotFoundException("项目主素材文件不存在。");
            if (info.Length == 0) throw new InvalidDataException("项目主素材文件为空。");
            return new(directory, id, workshop, title, type, media, preview, SteamProjectStatus.Supported,
                "", info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception error) when (FileError(error))
        {
            return new(directory, id, workshop, title, type, null, preview, SteamProjectStatus.Invalid, error.Message, 0, default);
        }
    }

    private static string? WorkshopId(string directory)
    {
        string name = Path.GetFileName(directory);
        string? parent = Path.GetDirectoryName(directory);
        if (name.Length > 0 && name.All(char.IsAsciiDigit) && parent != null && Path.GetFileName(parent) == "431960") return name;
        return null;
    }

    private static string ProjectFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 32767 || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Contains('\0')) throw new InvalidDataException("项目主素材必须使用项目内的相对路径。");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.Any(char.IsControl)))
            throw new InvalidDataException("项目主素材路径无效或越出项目目录。");
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("项目主素材路径越出项目目录。");
        return full;
    }

    private static string SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Contains('\0') || (OperatingSystem.IsWindows() && path.AsSpan(2).Contains(':')))
            throw new ArgumentException("请选择本机完整目录，不支持网络、设备或相对路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static void CheckPath(string path)
    {
        path = SafePath(path);
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Steam 来源或目标路径不能包含符号链接或目录联接。");
    }
    private static string ReadText(string path, int maximum, CancellationToken token)
    {
        CheckPath(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new InvalidDataException("项目说明或 Steam 库列表过大。");
        byte[] bytes = new byte[checked((int)input.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int read = input.Read(bytes, offset, Math.Min(81920, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException("项目说明读取未完成。");
            offset += read;
        }
        string text = new UTF8Encoding(false, true).GetString(bytes);
        return text.TrimStart('\uFEFF');
    }
    private static bool FileError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
        JsonException or NotSupportedException or System.Security.SecurityException;
}
