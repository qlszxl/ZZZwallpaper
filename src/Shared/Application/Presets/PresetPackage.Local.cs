using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;

namespace LumaWall.Application.Presets;

internal static partial class PresetPackage
{
    internal static void CheckLocalPath(string path, bool media)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains('\0') || path.AsSpan(2).Contains(':'))
            throw new InvalidDataException("预设资源必须使用本机普通文件。");
        if (media) _ = MediaFormats.LocalImportPath(path);
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("预设资源路径不能包含链接。");
            current = Path.GetDirectoryName(current);
        }
    }

    // Saving a local preset hashes original media but never copies it. Only
    // bounded embedded data receives an owned file; the portable schema stays v1.
    internal static async Task<(PresetManifest Manifest, Dictionary<string,string> Locations)> ReferenceAsync(PresetDocument document, string embeddedDirectory, CancellationToken cancellationToken)
    {
        ValidateDocument(document);
        var node = JsonSerializer.SerializeToNode(document, JsonOptions)!.AsObject();
        var resources = new Dictionary<string,PresetResource>(StringComparer.Ordinal);
        var locations = new Dictionary<string,string>(StringComparer.Ordinal);
        var hashed = new Dictionary<string,(string Hash,long Bytes)>(StringComparer.OrdinalIgnoreCase);
        long total = 0, embeddedTotal = 0;
        foreach (var asset in node["Assets"]!.AsArray().OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = MediaFormats.LocalImportPath(asset["Path"]!.GetValue<string>()); CheckLocalPath(path, true);
            if (!hashed.TryGetValue(path, out var value))
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > PresetPackageLimits.MaxResourceBytes) throw new InvalidDataException("壁纸文件不存在或过大。");
                await using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
                value = (Convert.ToHexString(await SHA256.HashDataAsync(input,cancellationToken)).ToLowerInvariant(),input.Length);
                hashed.Add(path,value);
            }
            string name = "resources/" + value.Hash + Path.GetExtension(path).ToLowerInvariant();
            if (resources.TryAdd(name,new(name,value.Bytes,value.Hash,"media"))) { total = checked(total + value.Bytes); locations.Add(name,path); }
            asset["Path"] = ResourcePrefix + name; asset["Bytes"] = value.Bytes;
        }
        foreach (var (owner,key) in EmbeddedFields(node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? data = owner[key]?.GetValue<string>(); if (string.IsNullOrEmpty(data)) continue;
            if (data.Length > PresetPackageLimits.MaxEmbeddedResourceBytes) throw new InvalidDataException("预设内嵌资源过大。");
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            if (bytes.Length > PresetPackageLimits.MaxEmbeddedResourceBytes) throw new InvalidDataException("预设内嵌资源过大。");
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), name = "resources/" + hash + ".bin";
            if (resources.TryAdd(name,new(name,bytes.Length,hash,"embedded")))
            {
                total = checked(total + bytes.Length); embeddedTotal = checked(embeddedTotal + bytes.Length);
                if (embeddedTotal > PresetPackageLimits.MaxEmbeddedTotalBytes) throw new InvalidDataException("预设内嵌资源总量过大。");
                string path = Path.Combine(embeddedDirectory,Path.GetFileName(name));
                await File.WriteAllBytesAsync(path,bytes,cancellationToken); locations.Add(name,path);
            }
            owner[key] = ResourcePrefix + name;
        }
        if (total > PresetPackageLimits.MaxPackageBytes || resources.Count + 1 > PresetPackageLimits.MaxEntries) throw new InvalidDataException("预设资源总量超过限制。");
        var manifest = new PresetManifest("ZZZwallpaper.Preset",1,node,resources.Values.ToArray());
        _ = ParseManifest(JsonSerializer.SerializeToUtf8Bytes(manifest,JsonOptions));
        return (manifest,locations);
    }

    internal static async Task<(PresetManifest Manifest, byte[] Json, Dictionary<string,ZipArchiveEntry> Entries)> InspectArchiveAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        if (zip.Entries.Count is < 1 or > PresetPackageLimits.MaxEntries) throw new InvalidDataException("预设文件数量无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            if (!names.Add(entry.FullName) || (entry.FullName != "manifest.json" && !ResourceName.IsMatch(entry.FullName)) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("预设包含重复、越界或不允许的文件路径。");
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null || manifestEntry.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("缺少预设清单或清单过大。");
        using var memory = new MemoryStream();
        await using (var input = manifestEntry.Open()) await CopyBoundedAsync(input,memory,PresetPackageLimits.MaxManifestBytes,cancellationToken);
        byte[] json = memory.ToArray(); var manifest = ParseManifest(json);
        var entries = CheckArchiveEntries(zip,manifest);
        // Validate non-resource document structure before asking for disk space.
        // Resource-bearing values still contain portable references here.
        // Validate those actual image/video bytes only after streamed extraction.
        ValidateDocument(manifest.Document.Deserialize<PresetDocument>(JsonOptions) ?? throw new InvalidDataException("预设配置无法读取。"),false);
        return (manifest,json,entries);
    }

    internal static string Fingerprint(byte[] manifest, ZipArchive zip, long bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(manifest);
        hash.AppendData(Encoding.UTF8.GetBytes("\n" + bytes));
        foreach (var entry in zip.Entries) hash.AppendData(Encoding.UTF8.GetBytes("\n" + entry.FullName + "|" + entry.Length + "|" + entry.CompressedLength));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static async Task WriteReferencedArchiveAsync(PresetManifest manifest, byte[] json, IReadOnlyDictionary<string,string> locations, string destination, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
        using (var archive = new ZipArchive(file,ZipArchiveMode.Create,true))
        {
            foreach (var resource in manifest.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = locations[resource.Path]; CheckLocalPath(path,resource.Kind=="media");
                await using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
                await using var output = archive.CreateEntry(resource.Path,resource.Kind=="media"?CompressionLevel.NoCompression:CompressionLevel.Optimal).Open();
                await CopyVerifiedAsync(input,output,resource.Bytes,resource.Sha256,cancellationToken);
            }
            await using var target = archive.CreateEntry("manifest.json",CompressionLevel.Optimal).Open();
            await target.WriteAsync(json,cancellationToken);
        }
        await file.FlushAsync(cancellationToken);
    }
}
