using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;

namespace LumaWall.Application.Presets;

public sealed partial class PresetService
{
    public const string ImportedMediaFolderName = "ZZZwallpaper.PresetResources";
    private sealed record ResourceLocations(int Version, Dictionary<string,string> Paths);
    private sealed record ResourceStamp(string Hash,long Bytes,long Modified);
    private sealed record ImportJournal(int Version,string Id,string MediaParent,string[] Names);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,ResourceStamp> verifiedResources = new(StringComparer.OrdinalIgnoreCase);

    public Task<PresetImportPreview> PreviewPackageAsync(string zipPath, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var source = OpenPackage(zipPath);
        using var archive = new ZipArchive(source,ZipArchiveMode.Read,true);
        var inspected = await PresetPackage.InspectArchiveAsync(archive,cancellationToken);
        var media = inspected.Manifest.Resources.Where(r=>r.Kind=="media").ToArray();
        return new PresetImportPreview(inspected.Manifest.Document["Name"]!.GetValue<string>(),
            inspected.Manifest.Document["Selection"]!.Deserialize<PresetSelection>(PresetPackage.JsonOptions)!,
            media.Length,media.Sum(r=>r.Bytes),source.Length,PresetPackage.Fingerprint(inspected.Json,archive,source.Length));
    },cancellationToken);

    /// <summary>Use a library snapshot from its owning thread. All media hashing runs in the background.</summary>
    public Task<PresetDocument> PrepareApplyAsync(string id, IReadOnlyList<MediaAsset> existingAssets, CancellationToken cancellationToken = default)
    {
        var candidates = existingAssets.ToArray();
        return Task.Run(async () =>
        {
            PresetManifest manifest; Dictionary<string,string> locations;
            lock (files.Gate)
            {
                RecoverRenames(); string directory = PresetDirectory(id); CheckDirectory(directory);
                manifest = PresetPackage.ParseManifest(ReadLocalFile(Path.Combine(directory,"manifest.json"),PresetPackageLimits.MaxManifestBytes));
                locations = ReadLocations(directory,manifest) ?? manifest.Resources.ToDictionary(r=>r.Path,r=>Path.Combine(resources,Path.GetFileName(r.Path)),StringComparer.Ordinal);
            }
            var cache = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in manifest.Resources.Where(r=>r.Kind=="media"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Resolve against the library before touching the old path: a
                // moved/re-associated original may already exist at a new path.
                var match = await FindExistingAsync(resource,candidates,cache,cancellationToken);
                if (match != null) locations[resource.Path] = Path.GetFullPath(match.Path);
                else await VerifyResourceAsync(locations[resource.Path],resource,cancellationToken);
            }
            var document = PresetPackage.Materialize(manifest,resources,false,cancellationToken,locations);
            RememberResources(manifest,document); return document;
        },cancellationToken);
    }

    private MediaAsset[] SavedMediaCandidates()
    {
        var result = new List<MediaAsset>();
        lock (files.Gate)
        {
            RecoverRenames(); CheckDirectory(presets);
            foreach (string directory in Directory.EnumerateDirectories(presets))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory),"N",out _)) continue;
                try
                {
                    CheckDirectory(directory);
                    var manifest = PresetPackage.ParseManifest(ReadLocalFile(Path.Combine(directory,"manifest.json"),PresetPackageLimits.MaxManifestBytes));
                    var locations = ReadLocations(directory,manifest);
                    foreach (var resource in manifest.Resources.Where(r=>r.Kind=="media"))
                    {
                        string path = locations?.GetValueOrDefault(resource.Path) ?? Path.Combine(resources,Path.GetFileName(resource.Path));
                        result.Add(new(resource.Sha256,path,Path.GetFileName(path),resource.Bytes,0,0,0,"","",""));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { }
            }
        }
        return result.DistinctBy(a=>a.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static FileStream OpenPackage(string path)
    {
        path = Path.GetFullPath(path); PresetPackage.CheckLocalPath(path,false); CheckLocalFile(path,MaximumArchiveBytes);
        return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
    }

    private static Dictionary<string,string>? ReadLocations(string directory, PresetManifest manifest)
    {
        string path = Path.Combine(directory,"locations.json");
        return File.Exists(path) ? ParseLocations(ReadLocalFile(path,PresetPackageLimits.MaxManifestBytes),manifest) : null;
    }

    private static Dictionary<string,string> ParseLocations(byte[] json, PresetManifest manifest)
    {
        // This sidecar is generated locally and is never accepted from a ZIP.
        var locations = JsonSerializer.Deserialize<ResourceLocations>(json,PresetPackage.JsonOptions);
        if (locations == null || locations.Version != 1 || locations.Paths == null || locations.Paths.Count != manifest.Resources.Length ||
            manifest.Resources.Any(r=>!locations.Paths.ContainsKey(r.Path))) throw new InvalidDataException("本地预设资源位置记录不完整。");
        foreach (var resource in manifest.Resources) PresetPackage.CheckLocalPath(locations.Paths[resource.Path],resource.Kind=="media");
        return locations.Paths;
    }

    private void RememberResources(PresetManifest manifest, PresetDocument document)
    {
        var assets = manifest.Document["Assets"]!.AsArray();
        for (int i=0;i<document.Assets.Length;i++)
        {
            string reference = assets[i]!["Path"]!.GetValue<string>()[PresetPackage.ResourcePrefix.Length..];
            var resource = manifest.Resources.Single(r=>r.Path==reference); var asset = document.Assets[i];
            verifiedResources[Path.GetFullPath(asset.Path)] = new(resource.Sha256,resource.Bytes,new FileInfo(asset.Path).LastWriteTimeUtc.Ticks);
        }
    }

    private async Task<PresetSummary> SaveReferencesAsync(PresetDocument document, CancellationToken cancellationToken)
    {
        string staging = NewStaging();
        try
        {
            var prepared = await PresetPackage.ReferenceAsync(document,staging,cancellationToken);
            await PromoteEmbeddedAsync(prepared.Manifest,prepared.Locations,cancellationToken);
            string candidate = Path.Combine(staging,"candidate"); Directory.CreateDirectory(candidate);
            string id = Guid.NewGuid().ToString("N"); var summary = Summary(id,document,0);
            await WriteLocalPresetAsync(candidate,prepared.Manifest,prepared.Locations,summary,cancellationToken);
            lock (files.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested(); CheckDirectory(presets);
                Directory.Move(candidate,Path.Combine(presets,id));
            }
            RememberResources(prepared.Manifest,document);
            return summary;
        }
        finally { RemoveStaging(staging); }
    }

    private async Task PromoteEmbeddedAsync(PresetManifest manifest, Dictionary<string,string> locations, CancellationToken cancellationToken)
    {
        foreach (var resource in manifest.Resources.Where(r=>r.Kind=="embedded"))
        {
            cancellationToken.ThrowIfCancellationRequested(); CheckDirectory(resources);
            string source = locations[resource.Path], destination = Path.Combine(resources,Path.GetFileName(resource.Path));
            if (File.Exists(destination)) { await VerifyResourceAsync(destination,resource,cancellationToken); File.Delete(source); }
            else
            {
                try { File.Move(source,destination); }
                catch (IOException) when (File.Exists(destination)) { await VerifyResourceAsync(destination,resource,cancellationToken); File.Delete(source); }
            }
            locations[resource.Path] = destination;
        }
    }

    private static async Task WriteLocalPresetAsync(string directory, PresetManifest manifest, Dictionary<string,string> locations, PresetSummary summary, CancellationToken cancellationToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest,PresetPackage.JsonOptions);
        byte[] mapping = JsonSerializer.SerializeToUtf8Bytes(new ResourceLocations(1,locations),PresetPackage.JsonOptions);
        if (json.Length > PresetPackageLimits.MaxManifestBytes || mapping.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("本地预设配置过大。");
        await WriteDurableAsync(Path.Combine(directory,"manifest.json"),json,cancellationToken);
        await WriteDurableAsync(Path.Combine(directory,"locations.json"),mapping,cancellationToken);
        await WriteDurableAsync(Path.Combine(directory,"info.json"),JsonSerializer.SerializeToUtf8Bytes(summary,PresetPackage.JsonOptions),cancellationToken);
    }

    private static async Task<MediaAsset?> FindExistingAsync(PresetResource resource, IEnumerable<MediaAsset> candidates, Dictionary<string,string> cache, CancellationToken cancellationToken)
    {
        foreach (var asset in candidates.Where(a=>a.Bytes==resource.Bytes&&string.Equals(Path.GetExtension(a.Path),Path.GetExtension(resource.Path),StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string path = Path.GetFullPath(asset.Path); PresetPackage.CheckLocalPath(path,true);
                var info = new FileInfo(path); if (!info.Exists || info.Length != resource.Bytes) continue;
                if (!cache.TryGetValue(path,out string? hash))
                {
                    await using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(input,cancellationToken)).ToLowerInvariant(); cache.Add(path,hash);
                }
                if (hash==resource.Sha256) return asset;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }

    private async Task<PresetSummary> ImportReferencesAsync(string zipPath, PresetImportOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Keep the verified sender file open throughout extraction. No private
        // ZIP copy, extracted media or temporary video is written to the data drive.
        await using var source = OpenPackage(zipPath);
        using var archive = new ZipArchive(source,ZipArchiveMode.Read,true);
        var inspected = await PresetPackage.InspectArchiveAsync(archive,cancellationToken);
        if (options.ExpectedFingerprint != null && options.ExpectedFingerprint != PresetPackage.Fingerprint(inspected.Json,archive,source.Length))
            throw new InvalidDataException("预设 ZIP 在预览后已改变，请重新选择。");
        var manifest = inspected.Manifest;
        var locations = new Dictionary<string,string>(StringComparer.Ordinal);
        var cache = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var copied = new List<PresetResource>();
        var candidates = (options.ExistingAssets ?? []).Concat(SavedMediaCandidates()).ToArray();
        foreach (var resource in manifest.Resources.Where(r=>r.Kind=="media"))
        {
            var existing = await FindExistingAsync(resource,candidates,cache,cancellationToken);
            if (existing != null) locations.Add(resource.Path,Path.GetFullPath(existing.Path)); else copied.Add(resource);
        }
        string id = Guid.NewGuid().ToString("N"),staging = NewStaging();
        string? mediaStage = null, mediaFinal = null; ImportJournal? journal = null; bool committed = false;
        lock (files.Gate) files.Imports.Add(staging);
        try
        {
            if (copied.Count > 0)
            {
                string selected = Path.GetFullPath(options.MediaDirectory ?? resources);
                PresetPackage.CheckLocalPath(selected,false); EnsureDirectory(selected);
                string parent = Path.Combine(selected,ImportedMediaFolderName); EnsureDirectory(parent); PresetPackage.CheckLocalPath(parent,false);
                mediaStage = Path.Combine(parent,".import-"+id); mediaFinal = Path.Combine(parent,id);
                if (Directory.Exists(mediaStage) || Directory.Exists(mediaFinal)) throw new IOException("预设媒体目录已存在。");
                journal = new(1,id,parent,copied.Select(r=>Path.GetFileName(r.Path)).ToArray());
                await WriteDurableAsync(Path.Combine(staging,"import.pending"),JsonSerializer.SerializeToUtf8Bytes(journal,PresetPackage.JsonOptions),cancellationToken);
                File.Move(Path.Combine(staging,"import.pending"),Path.Combine(staging,"import.json"));
                Directory.CreateDirectory(mediaStage);
                // Publish the tiny ownership marker before honoring cancellation.
                // It is the only authority recovery uses when removing media.
                await WriteDurableAsync(Path.Combine(mediaStage,"owner.pending"),JsonSerializer.SerializeToUtf8Bytes(new {Version=1,Id=id},PresetPackage.JsonOptions),CancellationToken.None);
                File.Move(Path.Combine(mediaStage,"owner.pending"),Path.Combine(mediaStage,"owner.json"));
            }
            foreach (var resource in manifest.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var input = inspected.Entries[resource.Path].Open();
                if (resource.Kind=="media" && locations.ContainsKey(resource.Path))
                    await PresetPackage.CopyVerifiedAsync(input,Stream.Null,resource.Bytes,resource.Sha256,cancellationToken);
                else
                {
                    string target = Path.Combine(resource.Kind=="media" ? mediaStage! : staging,Path.GetFileName(resource.Path));
                    await using var output = new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                    await PresetPackage.CopyVerifiedAsync(input,output,resource.Bytes,resource.Sha256,cancellationToken);
                    locations.Add(resource.Path,target);
                }
            }
            var document = PresetPackage.Materialize(manifest,resources,false,cancellationToken,locations);
            cancellationToken.ThrowIfCancellationRequested();
            await PromoteEmbeddedAsync(manifest,locations,cancellationToken);
            if (mediaStage != null)
            {
                PresetPackage.CheckLocalPath(mediaStage,false); Directory.Move(mediaStage,mediaFinal!);
                foreach (var resource in copied) locations[resource.Path] = Path.Combine(mediaFinal!,Path.GetFileName(resource.Path));
            }
            string candidate = Path.Combine(staging,"candidate"); Directory.CreateDirectory(candidate);
            var summary = Summary(id,document,0);
            await WriteLocalPresetAsync(candidate,manifest,locations,summary,cancellationToken);
            lock (files.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested(); CheckDirectory(presets);
                Directory.Move(candidate,Path.Combine(presets,id)); committed = true;
            }
            return summary;
        }
        finally
        {
            try
            {
                if (!committed && journal != null) RemoveImportedBundle(journal);
                try { RemoveStaging(staging); }
                catch (Exception error) when (committed && error is IOException or UnauthorizedAccessException)
                { LumaWall.Diagnostics.AppLog.Warning("Presets","Import committed; recovery cleanup will retry",error); }
            }
            finally { lock (files.Gate) files.Imports.Remove(staging); }
        }
    }

    private void RecoverImports()
    {
        foreach (string staging in Directory.EnumerateDirectories(stagingRoot))
        {
            if (files.Imports.Contains(staging) || !File.Exists(Path.Combine(staging,"import.json"))) continue;
            CheckDirectory(staging);
            var journal = JsonSerializer.Deserialize<ImportJournal>(ReadLocalFile(Path.Combine(staging,"import.json"),PresetPackageLimits.MaxManifestBytes),PresetPackage.JsonOptions)
                ?? throw new InvalidDataException("预设导入恢复记录无效。");
            ValidateImportJournal(journal);
            string directory = PresetDirectory(journal.Id);
            if (Directory.Exists(directory)) ReadLocalPreset(directory,journal.Id);
            else RemoveImportedBundle(journal);
            RemoveStaging(staging);
        }
    }

    private static void ValidateImportJournal(ImportJournal journal)
    {
        if (journal.Version!=1 || !Guid.TryParseExact(journal.Id,"N",out _) || !Path.IsPathFullyQualified(journal.MediaParent) ||
            Path.GetFileName(journal.MediaParent)!=ImportedMediaFolderName || journal.Names==null || journal.Names.Length>PresetPackageLimits.MaxAssets ||
            journal.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=journal.Names.Length ||
            journal.Names.Any(name=>!Regex.IsMatch(name,@"^[a-f0-9]{64}\.[a-z0-9]{1,8}$",RegexOptions.CultureInvariant)||!MediaFormats.Supported(name)))
            throw new InvalidDataException("预设导入恢复路径无效。");
        PresetPackage.CheckLocalPath(journal.MediaParent,false);
    }

    private static void RemoveImportedBundle(ImportJournal journal)
    {
        ValidateImportJournal(journal);
        foreach (string name in new[] {journal.Id,".import-"+journal.Id})
        {
            string directory = Path.Combine(journal.MediaParent,name); if (!Directory.Exists(directory)) continue;
            PresetPackage.CheckLocalPath(directory,false);
            string marker = Path.Combine(directory,"owner.json");
            if (!File.Exists(marker))
            {
                // A crash between CreateDirectory and the first durable marker.
                var incomplete = Directory.EnumerateFileSystemEntries(directory).ToArray();
                if (incomplete.Length==0) { Directory.Delete(directory); continue; }
                if (incomplete.Length==1 && Path.GetFileName(incomplete[0])=="owner.pending" && (File.GetAttributes(incomplete[0])&FileAttributes.ReparsePoint)==0 && !Directory.Exists(incomplete[0]))
                { File.Delete(incomplete[0]); Directory.Delete(directory); continue; }
                throw new IOException("预设临时资源缺少所有权记录，已保留。");
            }
            using var owner = JsonDocument.Parse(ReadLocalFile(marker,4096));
            if (owner.RootElement.GetProperty("Version").GetInt32()!=1 || owner.RootElement.GetProperty("Id").GetString()!=journal.Id) throw new IOException("预设资源所有权记录不一致，已保留。");
            var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            if (entries.Any(p=>Directory.Exists(p)||(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0||
                Path.GetFileName(p)!="owner.json"&&!journal.Names.Contains(Path.GetFileName(p),StringComparer.Ordinal)))
                throw new IOException("预设资源目录含额外文件，已保留。");
            foreach (string path in entries) File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
