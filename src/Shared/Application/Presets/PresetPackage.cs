using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;

namespace LumaWall.Application.Presets;

public static class PresetPackageLimits
{
    public const long MaxPackageBytes = 32L * 1024 * 1024 * 1024;
    public const long MaxResourceBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxManifestBytes = 16 * 1024 * 1024;
    public const int MaxEmbeddedResourceBytes = 32 * 1024 * 1024;
    public const int MaxEmbeddedTotalBytes = 256 * 1024 * 1024;
    public const int MaxEntries = 10001;
    public const int MaxAssets = 5000;
    public const int MaxNameLength = 80;
}

internal sealed record PresetResource(string Path, long Bytes, string Sha256, string Kind);
internal sealed record PresetManifest(string Format, int Version, JsonObject Document, PresetResource[] Resources);

internal static partial class PresetPackage
{
    internal const string ResourcePrefix = "preset-resource:";
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 64, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };
    private static readonly Regex ResourceName = new(@"^resources/[a-f0-9]{64}\.[a-z0-9]{1,8}$", RegexOptions.CultureInvariant);

    internal static string RequireName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > PresetPackageLimits.MaxNameLength || name.Any(char.IsControl)) throw new InvalidDataException("预设名称须为 1–80 个字符。");
        return name;
    }

    internal static void ValidateDocument(PresetDocument document, bool validateResourceData = true)
    {
        RequireName(document.Name);
        if (document.Version != 1 || document.Selection == null || !document.Selection.Any || document.Assets == null || document.Screens == null || document.Bindings == null || document.Playback == null || document.Volumes == null || document.SoundEnabled == null || document.ScreenProfiles == null || document.AssetEffects == null)
            throw new InvalidDataException("不支持或不完整的预设格式。");
        if (document.Assets.Length > PresetPackageLimits.MaxAssets || document.Screens.Length > 64 || document.Bindings.Length > 64) throw new InvalidDataException("预设条目过多。");
        static bool Id(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl);
        if (document.Screens.Any(s => !Id(s)) || document.Screens.Distinct(StringComparer.Ordinal).Count() != document.Screens.Length || document.Assets.Any(a => a == null || !Id(a.Id) || a.Name == null || a.Name.Length > 512 || a.Path == null || a.Bytes < 0 || a.Bytes > PresetPackageLimits.MaxResourceBytes || !double.IsFinite(a.Duration) || a.Duration < 0 || a.Width < 0 || a.Height < 0 || a.Codec == null || a.Color == null || a.Decoder == null) || document.Assets.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != document.Assets.Length)
            throw new InvalidDataException("预设素材或显示器标识无效。");
        var assets = document.Assets.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var screens = document.Screens.ToHashSet(StringComparer.Ordinal);
        if (document.Bindings.Any(b => b == null || !screens.Contains(b.ScreenId) || !assets.Contains(b.AssetId) || !Enum.IsDefined(b.Fill)) || document.Bindings.Select(b => b.ScreenId).Distinct(StringComparer.Ordinal).Count() != document.Bindings.Length) throw new InvalidDataException("预设播放引用缺少素材或显示器。");
        foreach (var (screen, playback) in document.Playback)
        {
            if (!screens.Contains(screen) || playback == null || playback.Version != 2 || playback.FolderId != null || playback.AssetIds == null || playback.CustomPlaylists == null || playback.CustomPlaylists.Any(p => p == null || p.AssetIds == null) || playback.AssetIds.Concat(playback.CustomPlaylists.SelectMany(p => p.AssetIds)).Any(id => !assets.Contains(id))) throw new InvalidDataException("预设播放队列引用无效。");
            playback.Validate();
        }
        if (document.Volumes.Any(v => !screens.Contains(v.Key) || v.Value is < 0 or > 100)) throw new InvalidDataException("预设音量无效。");
        if (document.SoundEnabled.Keys.Any(s => !screens.Contains(s))) throw new InvalidDataException("预设静音显示器无效。");
        foreach (var (screen, profile) in document.ScreenProfiles)
        {
            if (!screens.Contains(screen) || profile == null || !Enum.IsDefined(profile.Fill)) throw new InvalidDataException("预设效果显示器无效。");
            if (profile.Effects == null) throw new InvalidDataException("预设效果缺失。");
            if (validateResourceData) profile.Effects.Validate();
        }
        foreach (var (id, effects) in document.AssetEffects)
        {
            if (!assets.Contains(id) || effects == null) throw new InvalidDataException("预设素材效果引用无效。");
            if (validateResourceData) effects.Validate();
        }
        if (validateResourceData) document.Widgets?.Validate();
        if (document.Widgets != null && (document.Widgets.Widgets == null || document.Widgets.LayoutGroups == null)) throw new InvalidDataException("预设组件配置不完整。");
        if (document.Widgets != null && (document.Widgets.Widgets.Any(w => !screens.Contains(w.Screen)) || document.Widgets.LayoutGroups.Any(g => !screens.Contains(g.Screen)))) throw new InvalidDataException("预设组件显示器无效。");
        if ((!document.Selection.Playing && (document.Bindings.Length > 0 || document.Playback.Count > 0 || document.Volumes.Count > 0 || document.SoundEnabled.Count > 0)) || (!document.Selection.Effects && (document.ScreenProfiles.Count > 0 || document.AssetEffects.Count > 0)) || (!document.Selection.Widgets && document.Widgets != null) || (!document.Selection.Library && !document.Selection.Playing && document.Assets.Length > 0)) throw new InvalidDataException("预设内容与所选类别不一致。");
    }

    // The resource schema is explicit: arbitrary strings can never become file paths.
    internal static IEnumerable<(JsonObject Owner, string Key)> EmbeddedFields(JsonObject document)
    {
        static IEnumerable<(JsonObject, string)> Effects(JsonObject effects)
        {
            if (effects["Masks"] is JsonArray masks) foreach (var item in masks.OfType<JsonObject>()) yield return (item, "Image");
            if (effects["Particles"] is JsonObject particles)
            {
                yield return (particles, "Image");
                if (particles["SubLayer"] is JsonObject sub) yield return (sub, "Image");
            }
            if (effects["ScanCards"] is JsonObject scan)
            {
                if (scan["Images"] is JsonArray images) foreach (var image in images.OfType<JsonObject>()) { yield return (image, "Data"); yield return (image, "MediaData"); }
                if (scan["CenterImage"] is JsonObject center) { yield return (center, "Data"); yield return (center, "MediaData"); }
            }
        }
        if (document["ScreenProfiles"] is JsonObject profiles) foreach (var profile in profiles.Select(p => p.Value).OfType<JsonObject>()) if (profile["Effects"] is JsonObject effects) foreach (var field in Effects(effects)) yield return field;
        if (document["AssetEffects"] is JsonObject specifics) foreach (var profile in specifics.Select(p => p.Value).OfType<JsonObject>()) if (profile["Effects"] is JsonObject effects) foreach (var field in Effects(effects)) yield return field;
        if (document["Bindings"] is JsonArray bindings) foreach (var binding in bindings.OfType<JsonObject>()) if (binding["Effects"] is JsonObject effects) foreach (var field in Effects(effects)) yield return field;
        if (document["Widgets"] is JsonObject widgets)
        {
            if (widgets["Widgets"] is JsonArray items) foreach (var widget in items.OfType<JsonObject>())
            {
                yield return (widget, "Icon");
                if (widget["Visual"] is JsonObject visual) yield return (visual, "Image");
            }
            if (widgets["Shortcuts"] is JsonArray cards) foreach (var card in cards.OfType<JsonObject>()) yield return (card, "Icon");
        }
    }

    internal static async Task WriteAsync(PresetDocument document, string destination, CancellationToken cancellationToken)
    {
        ValidateDocument(document);
        var node = JsonSerializer.SerializeToNode(document, JsonOptions)!.AsObject();
        var resources = new Dictionary<string, PresetResource>(StringComparer.Ordinal);
        long total = 0, embeddedTotal = 0;
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
        foreach (var asset in node["Assets"]!.AsArray().OfType<JsonObject>())
        {
            string path = asset["Path"]!.GetValue<string>();
            path = MediaFormats.LocalImportPath(path);
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > PresetPackageLimits.MaxResourceBytes) throw new InvalidDataException("壁纸文件不存在、过大或不是普通本地文件。");
            string hash;
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
            string name = "resources/" + hash + Path.GetExtension(path).ToLowerInvariant();
            if (!resources.ContainsKey(name))
            {
                total = checked(total + info.Length); if (total > PresetPackageLimits.MaxPackageBytes) throw new InvalidDataException("预设资源总大小超过 32 GB。");
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var target = entry.Open();
                await CopyVerifiedAsync(input, target, info.Length, hash, cancellationToken);
                resources.Add(name, new(name, info.Length, hash, "media"));
            }
            asset["Path"] = ResourcePrefix + name; asset["Bytes"] = info.Length;
        }
        foreach (var (owner, key) in EmbeddedFields(node))
        {
            string? data = owner[key]?.GetValue<string>(); if (string.IsNullOrEmpty(data)) continue;
            // Resource strings include both PNG/base64 and the P1: compressed
            // pixel codec. Keep the exact encoding instead of stripping its tag.
            if (data.Length > PresetPackageLimits.MaxEmbeddedResourceBytes) throw new InvalidDataException("预设内嵌资源过大。");
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            if (bytes.Length > PresetPackageLimits.MaxEmbeddedResourceBytes) throw new InvalidDataException("预设内嵌资源过大。");
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), name = "resources/" + hash + ".bin";
            if (!resources.ContainsKey(name))
            {
                total = checked(total + bytes.Length); embeddedTotal = checked(embeddedTotal + bytes.Length);
                if (total > PresetPackageLimits.MaxPackageBytes || embeddedTotal > PresetPackageLimits.MaxEmbeddedTotalBytes) throw new InvalidDataException("预设内嵌资源总量过大。");
                await using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); await target.WriteAsync(bytes, cancellationToken);
                resources.Add(name, new(name, bytes.Length, hash, "embedded"));
            }
            owner[key] = ResourcePrefix + name;
        }
        if (resources.Count + 1 > PresetPackageLimits.MaxEntries) throw new InvalidDataException("预设文件数量过多。");
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new PresetManifest("ZZZwallpaper.Preset", 1, node, resources.Values.ToArray()), JsonOptions);
        if (manifest.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("预设配置过大。");
        await using var manifestStream = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
        await manifestStream.WriteAsync(manifest, cancellationToken);
    }

    internal static async Task<PresetManifest> ExtractAsync(string source, string staging, CancellationToken cancellationToken)
    {
        var info = new FileInfo(source);
        if (!info.Exists || info.Length > PresetPackageLimits.MaxPackageBytes + PresetPackageLimits.MaxManifestBytes + 16 * 1024 * 1024) throw new InvalidDataException("预设 ZIP 文件过大或不存在。");
        using var zip = ZipFile.OpenRead(source);
        if (zip.Entries.Count is < 1 or > PresetPackageLimits.MaxEntries) throw new InvalidDataException("预设文件数量无效。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!entries.TryAdd(entry.FullName, entry) || (entry.FullName != "manifest.json" && !ResourceName.IsMatch(entry.FullName)) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("预设包含重复、越界或不允许的文件路径。");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("缺少预设清单或清单过大。");
        byte[] json;
        await using (var input = manifestEntry.Open())
        {
            using var memory = new MemoryStream(); await CopyBoundedAsync(input, memory, PresetPackageLimits.MaxManifestBytes, cancellationToken); json = memory.ToArray();
        }
        var manifest = ParseManifest(json);
        if (entries.Count != manifest.Resources.Length + 1) throw new InvalidDataException("预设文件与清单不一致。");
        foreach (var resource in manifest.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(resource.Path, out var entry) || entry.Length != resource.Bytes) throw new InvalidDataException("预设资源缺失或大小不匹配。");
            string path = Path.Combine(staging, Path.GetFileName(resource.Path));
            await using var input = entry.Open();
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await CopyVerifiedAsync(input, target, resource.Bytes, resource.Sha256, cancellationToken);
        }
        await File.WriteAllBytesAsync(Path.Combine(staging, "manifest.json"), json, cancellationToken);
        return manifest;
    }

    internal static PresetManifest ParseManifest(byte[] json)
    {
        if (json.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("预设清单过大。");
        using (var document = JsonDocument.Parse(json, new() { MaxDepth = 64 }))
        {
            void Unique(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("预设 JSON 包含重复字段。"); Unique(property.Value); }
                }
                else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
            }
            Unique(document.RootElement);
        }
        var manifest = JsonSerializer.Deserialize<PresetManifest>(json, JsonOptions) ?? throw new InvalidDataException("预设清单无效。");
        if (manifest.Format != "ZZZwallpaper.Preset" || manifest.Version != 1 || manifest.Document == null || manifest.Resources == null || manifest.Resources.Length + 1 > PresetPackageLimits.MaxEntries) throw new InvalidDataException("不支持的预设格式版本。");
        var resources = new Dictionary<string, PresetResource>(StringComparer.Ordinal);
        long total = 0, embeddedTotal = 0;
        foreach (var resource in manifest.Resources)
        {
            if (resource == null || resource.Path == null || !ResourceName.IsMatch(resource.Path) || resource.Bytes < 0 || resource.Bytes > PresetPackageLimits.MaxResourceBytes || resource.Sha256 != Path.GetFileNameWithoutExtension(resource.Path) || resource.Kind is not ("media" or "embedded") || !resources.TryAdd(resource.Path, resource)) throw new InvalidDataException("预设资源清单无效。");
            if (resource.Kind == "media" && !MediaFormats.Supported(resource.Path) || resource.Kind == "embedded" && (!resource.Path.EndsWith(".bin", StringComparison.Ordinal) || resource.Bytes > PresetPackageLimits.MaxEmbeddedResourceBytes)) throw new InvalidDataException("预设资源类型不允许。");
            total = checked(total + resource.Bytes); if (resource.Kind == "embedded") embeddedTotal = checked(embeddedTotal + resource.Bytes);
            if (total > PresetPackageLimits.MaxPackageBytes || embeddedTotal > PresetPackageLimits.MaxEmbeddedTotalBytes) throw new InvalidDataException("预设资源总量超过限制。");
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Reference(JsonNode? value, string kind, bool optional)
        {
            string? text = value?.GetValue<string>(); if (optional && string.IsNullOrEmpty(text)) return;
            if (text == null || !text.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resources.TryGetValue(text[ResourcePrefix.Length..], out var resource) || resource.Kind != kind) throw new InvalidDataException("预设引用缺少资源。");
            used.Add(resource.Path);
        }
        if (manifest.Document["Assets"] is not JsonArray assets) throw new InvalidDataException("预设素材列表缺失。");
        foreach (var asset in assets.OfType<JsonObject>()) Reference(asset["Path"], "media", false);
        long expandedEmbeddedBytes = 0;
        foreach (var (owner, key) in EmbeddedFields(manifest.Document))
        {
            Reference(owner[key], "embedded", true);
            string? reference = owner[key]?.GetValue<string>();
            if (!string.IsNullOrEmpty(reference)) expandedEmbeddedBytes = checked(expandedEmbeddedBytes + resources[reference[ResourcePrefix.Length..]].Bytes);
            if (expandedEmbeddedBytes > PresetPackageLimits.MaxEmbeddedTotalBytes) throw new InvalidDataException("预设展开后的图片数据超过限制。");
        }
        if (used.Count != resources.Count) throw new InvalidDataException("预设包含未引用的资源。");
        return manifest;
    }

    // Rebuild with the standard ZIP implementation and bounded streams. Large
    // media never enters memory or an extracted temporary file during rename.
    internal static async Task RenameArchiveAsync(string source, string destination, byte[] expectedManifest, byte[] renamedManifest, CancellationToken cancellationToken)
    {
        var manifest = ParseManifest(expectedManifest);
        if (renamedManifest.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("预设配置过大。");
        // Deletion may race the background copy. Keep a readable snapshot without
        // locking the archive against deletion after its metadata has been removed.
        await using var snapshot = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, true);
        using var input = new ZipArchive(snapshot, ZipArchiveMode.Read, true);
        var entries = CheckArchiveEntries(input, manifest);
        await using (var stream = entries["manifest.json"].Open())
        {
            using var memory = new MemoryStream();
            await CopyBoundedAsync(stream, memory, PresetPackageLimits.MaxManifestBytes, cancellationToken);
            if (!memory.ToArray().AsSpan().SequenceEqual(expectedManifest)) throw new InvalidDataException("本地预设 ZIP 与配置清单不一致。");
        }
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using (var output = new ZipArchive(file, ZipArchiveMode.Create, true))
        {
            foreach (var resource in manifest.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var from = entries[resource.Path].Open();
                await using var to = output.CreateEntry(resource.Path, resource.Kind == "media" ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open();
                await CopyVerifiedAsync(from, to, resource.Bytes, resource.Sha256, cancellationToken);
            }
            await using var target = output.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
            await target.WriteAsync(renamedManifest, cancellationToken);
        }
        await file.FlushAsync(cancellationToken); file.Flush(true);
    }

    internal static void CheckStoredArchive(string path, byte[] expectedManifest)
    {
        var manifest = ParseManifest(expectedManifest);
        using var zip = ZipFile.OpenRead(path);
        var entries = CheckArchiveEntries(zip, manifest);
        using var input = entries["manifest.json"].Open();
        using var memory = new MemoryStream();
        byte[] buffer = new byte[81920]; int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (memory.Length + count > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("预设清单过大。");
            memory.Write(buffer, 0, count);
        }
        if (!memory.ToArray().AsSpan().SequenceEqual(expectedManifest)) throw new InvalidDataException("本地预设 ZIP 与配置清单不一致。");
    }

    private static Dictionary<string, ZipArchiveEntry> CheckArchiveEntries(ZipArchive zip, PresetManifest manifest)
    {
        if (zip.Entries.Count != manifest.Resources.Length + 1) throw new InvalidDataException("预设文件与清单不一致。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
            if (!entries.TryAdd(entry.FullName, entry) || (entry.FullName != "manifest.json" && !ResourceName.IsMatch(entry.FullName)) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("预设包含重复、越界或不允许的文件路径。");
        if (!entries.TryGetValue("manifest.json", out var json) || json.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("缺少预设清单或清单过大。");
        foreach (var resource in manifest.Resources)
            if (!entries.TryGetValue(resource.Path, out var entry) || entry.Length != resource.Bytes) throw new InvalidDataException("预设资源缺失或大小不匹配。");
        return entries;
    }

    internal static PresetDocument Materialize(PresetManifest manifest, string resourceDirectory, bool verifyMedia = true, CancellationToken cancellationToken = default, IReadOnlyDictionary<string,string>? locations = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var node = manifest.Document.DeepClone().AsObject();
        var verifiedMedia = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in node["Assets"]!.AsArray().OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string reference = asset["Path"]!.GetValue<string>()[ResourcePrefix.Length..];
            string path = locations == null ? Path.Combine(resourceDirectory, Path.GetFileName(reference)) : locations[reference];
            CheckLocalPath(path, true);
            var expected = manifest.Resources.Single(r => r.Path == reference);
            if (asset["Bytes"]?.GetValue<long>() != expected.Bytes) throw new InvalidDataException("预设素材大小与资源清单不一致。");
            var info = new FileInfo(path); if (!info.Exists || info.Length != expected.Bytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("预设壁纸资源缺失或已改变。");
            if (verifyMedia && verifiedMedia.Add(path))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920]; int read;
                while ((read = file.Read(buffer)) != 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != expected.Sha256) throw new InvalidDataException("本地预设壁纸资源校验失败。");
            }
            asset["Path"] = path;
        }
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (owner, key) in EmbeddedFields(node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? reference = owner[key]?.GetValue<string>(); if (string.IsNullOrEmpty(reference)) continue;
            string name = reference[ResourcePrefix.Length..];
            if (!cache.TryGetValue(name, out var data))
            {
                var resource = manifest.Resources.Single(r => r.Path == name);
                string path = locations == null ? Path.Combine(resourceDirectory, Path.GetFileName(name)) : locations[name];
                CheckLocalPath(path, false);
                var info = new FileInfo(path); if (!info.Exists || info.Length != resource.Bytes || info.Length > PresetPackageLimits.MaxEmbeddedResourceBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("预设图片资源缺失。");
                byte[] bytes = File.ReadAllBytes(path); if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != resource.Sha256) throw new InvalidDataException("预设图片资源校验失败。");
                data = new UTF8Encoding(false, true).GetString(bytes); cache.Add(name, data);
            }
            owner[key] = data;
        }
        var result = node.Deserialize<PresetDocument>(JsonOptions) ?? throw new InvalidDataException("预设配置无法读取。");
        // Deserialize with the strict package options first, then migrate old
        // effect schemas through the same supported path as the library. Unknown
        // fields must not disappear inside the more permissive migration reader.
        static VideoEffects Migrate(VideoEffects effects) => effects.Version == VideoEffects.Original.Version ? effects :
            VideoEffects.FromJson(JsonSerializer.Serialize(effects,JsonOptions));
        result = result with
        {
            ScreenProfiles = result.ScreenProfiles.ToDictionary(p=>p.Key,p=>p.Value with {Effects=Migrate(p.Value.Effects)},StringComparer.Ordinal),
            AssetEffects = result.AssetEffects.ToDictionary(p=>p.Key,p=>p.Value with {Effects=Migrate(p.Value.Effects)},StringComparer.Ordinal),
            Bindings = result.Bindings.Select(b=>b with {Effects=Migrate(b.Effects)}).ToArray()
        };
        ValidateDocument(result); return result;
    }

    internal static async Task CopyVerifiedAsync(Stream source, Stream destination, long expectedBytes, string expectedHash, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920]; long count = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            count = checked(count + read); if (count > expectedBytes) throw new InvalidDataException("预设资源超过声明大小。");
            hash.AppendData(buffer, 0, read); await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (count != expectedBytes || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != expectedHash) throw new InvalidDataException("预设资源校验失败。");
    }
    private static async Task CopyBoundedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920]; long count = 0;
        while (true) { int read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break; count += read; if (count > limit) throw new InvalidDataException("预设清单超过限制。"); await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
    }
}
