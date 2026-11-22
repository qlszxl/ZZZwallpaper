using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Application;
using LumaWall.Contracts.Presets;
using LumaWall.Contracts.Sharing;
using LumaWall.Diagnostics;

namespace LumaWall.Application.Presets;

/// <summary>ZIP and list operations use files only. Capture/Apply must run on the store's owning thread.</summary>
public sealed partial class PresetService
{
    private readonly IPresetStore store;
    private readonly string root, presets, resources, stagingRoot;
    private sealed class FileState
    {
        internal readonly object Gate = new();
        internal readonly SemaphoreSlim Rename = new(1, 1);
        internal readonly HashSet<string> Imports = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed record RenameJournal(int Version, string Id);
    private sealed record LocalPreset(byte[] Manifest, byte[] Info, PresetSummary Summary, byte[]? Locations = null);
    private static readonly ConcurrentDictionary<string, FileState> FileStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileState files;
    public PresetService(IPresetStore store, string dataRoot)
    {
        this.store = store;
        root = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(root, Path.GetFullPath(store.Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("预设目录必须位于当前素材库数据目录。");
        presets = Path.Combine(root, "presets"); resources = Path.Combine(root, "preset-resources"); stagingRoot = Path.Combine(root, "preset-staging");
        files = FileStates.GetOrAdd(root, _ => new());
        foreach (string path in new[] { root, presets, resources, stagingRoot }) EnsureDirectory(path);
        lock (files.Gate) { RecoverRenames(); RecoverImports(); RemoveRetiredBuiltIns(); }
    }

    public PresetDocument Capture(string name, string[] screens, PresetSelection selection, IEnumerable<ScreenBinding>? currentBindings = null, string? primaryScreen = null)
    {
        name = PresetPackage.RequireName(name);
        if (selection == null || !selection.Any) throw new ArgumentException("请至少选择一类预设内容。");
        screens = screens.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
        var snapshot = store.CapturePreset(screens, true);
        var bindings = (currentBindings ?? snapshot.Bindings).Where(b => screens.Contains(b.ScreenId, StringComparer.Ordinal)).ToArray();
        var wanted = bindings.Select(b => b.AssetId).Concat(snapshot.Playback.Values.SelectMany(p => p.AssetIds.Concat((p.CustomPlaylists ?? []).SelectMany(l => l.AssetIds)))).ToHashSet(StringComparer.Ordinal);
        var assets = snapshot.Assets.Where(a => selection.Library || selection.Playing && wanted.Contains(a.Id)).ToArray();
        var ids = assets.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var widgets = selection.Widgets && snapshot.Widgets != null ? ProjectWidgets(snapshot.Widgets, screens, selection.IncludeWidgetPrivateData, primaryScreen) : null;
        var document = snapshot with
        {
            Name = name, CreatedUtc = DateTimeOffset.UtcNow, Selection = selection, Screens = screens, Assets = assets,
            Bindings = selection.Playing ? bindings.Select(b => b with { Effects = VideoEffects.Original, Revision = 0 }).ToArray() : [],
            Playback = selection.Playing ? snapshot.Playback : new(), Volumes = selection.Playing ? snapshot.Volumes : new(), SoundEnabled = selection.Playing ? snapshot.SoundEnabled : new(),
            ScreenProfiles = selection.Effects ? snapshot.ScreenProfiles.ToDictionary(p => p.Key, p => !selection.Playing && bindings.FirstOrDefault(b => b.ScreenId == p.Key) is { } current && snapshot.AssetEffects.TryGetValue(current.AssetId, out var specific) ? p.Value with { Effects = specific.Resolve(p.Value.Effects) } : p.Value, StringComparer.Ordinal) : new(),
            AssetEffects = selection.Effects ? snapshot.AssetEffects.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) : new(), Widgets = widgets
        };
        PresetPackage.ValidateDocument(document); return document;
    }

    private static WidgetConfiguration ProjectWidgets(WidgetConfiguration state, string[] screens, bool includePrivate, string? primaryScreen = null)
    {
        state.Validate();
        string primary = primaryScreen ?? screens.FirstOrDefault() ?? "";
        var chosen = state.Widgets.Where(w => screens.Contains(EffectiveWidgetScreen(state, w, primary), StringComparer.Ordinal)).Select(w => w with { Screen = EffectiveWidgetScreen(state, w, primary) }).ToArray();
        var groups = state.LayoutGroups.Where(g => chosen.Any(w => w.LayoutGroupId == g.Id)).ToArray();
        var result = ShareProjection.Widgets(state, chosen, new() { Layout = true, Parameters = true, Visibility = true, Images = true, Text = includePrivate, Targets = includePrivate });
        var emptyGroups = state.LayoutGroups.Where(g => !state.Widgets.Any(w => w.LayoutGroupId == g.Id) && screens.Contains(string.IsNullOrEmpty(g.Screen) ? primary : g.Screen, StringComparer.Ordinal))
            .Select(g => g with { Id = Guid.NewGuid().ToString("N"), Name = includePrivate ? g.Name : "组件组", Screen = string.IsNullOrEmpty(g.Screen) ? primary : g.Screen });
        return (result with
        {
            Widgets = result.Widgets.Select((w, i) => w with { Screen = string.IsNullOrEmpty(chosen[i].Screen) ? primary : chosen[i].Screen }).ToArray(),
            LayoutGroups = result.LayoutGroups.Select((g, i) => g with { Screen = string.IsNullOrEmpty(groups[i].Screen) ? primary : groups[i].Screen }).Concat(emptyGroups).ToArray()
        }).Validate();
    }

    public IReadOnlyList<PresetSummary> List()
    {
        lock (files.Gate)
        {
        RecoverRenames(); RecoverImports();
        CheckDirectory(presets); var list = new List<PresetSummary>();
        foreach (string directory in Directory.EnumerateDirectories(presets))
        {
            string id = Path.GetFileName(directory); if (!Guid.TryParseExact(id, "N", out _) || RetiredBuiltInIds.Contains(id)) continue;
            try
            {
                CheckDirectory(directory);
                string metadata = Path.Combine(directory, "info.json"), archive = Path.Combine(directory, "preset.zip");
                if ((!File.Exists(archive) && !File.Exists(Path.Combine(directory,"locations.json"))) || new FileInfo(metadata).Length > 64 * 1024) continue;
                var summary = JsonSerializer.Deserialize<PresetSummary>(File.ReadAllText(metadata), PresetPackage.JsonOptions);
                if (summary != null && summary.Id == id) list.Add(summary);
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        }
        return list.OrderByDescending(p => p.CreatedUtc).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToArray();
        }
    }

    public PresetDocument Read(string id, CancellationToken cancellationToken = default)
    {
        return ReadDocument(id, true, cancellationToken);
    }

    /// <summary>Details only: checks paths, sizes and embedded images, without hashing large wallpaper media. Applying still uses Read.</summary>
    public PresetDocument Inspect(string id, CancellationToken cancellationToken = default) => ReadDocument(id, false, cancellationToken);

    private PresetDocument ReadDocument(string id, bool verifyMedia, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] json; Dictionary<string,string>? locations;
        lock (files.Gate)
        {
            RecoverRenames();
            string directory = PresetDirectory(id); CheckDirectory(directory); CheckDirectory(resources);
            json = ReadLocalFile(Path.Combine(directory, "manifest.json"), PresetPackageLimits.MaxManifestBytes);
            locations = ReadLocations(directory, PresetPackage.ParseManifest(json));
        }
        var manifest = PresetPackage.ParseManifest(json);
        var document = PresetPackage.Materialize(manifest, resources, verifyMedia, cancellationToken, locations);
        if (verifyMedia) RememberResources(manifest,document);
        return document;
    }

    public Task<PresetSummary> RenameAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        name = PresetPackage.RequireName(name);
        string directory = PresetDirectory(id);
        // JSON, ZIP metadata and durable commit work also stay off the caller's UI thread.
        return Task.Run(async () =>
        {
            await files.Rename.WaitAsync(cancellationToken);
            string? staging = null;
            try
            {
                LocalPreset original;
                lock (files.Gate) { RecoverRenames(); original = ReadLocalPreset(directory, id); }
                cancellationToken.ThrowIfCancellationRequested();
                if (original.Summary.Name == name) return original.Summary;
                var manifest = PresetPackage.ParseManifest(original.Manifest);
                manifest.Document["Name"] = name;
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, PresetPackage.JsonOptions);
                if (json.Length > PresetPackageLimits.MaxManifestBytes) throw new InvalidDataException("预设配置过大。");
                staging = NewStaging();
                string candidate = Path.Combine(staging, "candidate"); Directory.CreateDirectory(candidate);
                string archive = Path.Combine(candidate, "preset.zip");
                if (original.Locations == null)
                    await PresetPackage.RenameArchiveAsync(Path.Combine(directory, "preset.zip"), archive, original.Manifest, json, cancellationToken);
                else await WriteDurableAsync(Path.Combine(candidate,"locations.json"),original.Locations,cancellationToken);
                var summary = original.Summary with { Name = name, ZipBytes = original.Locations == null ? new FileInfo(archive).Length : 0 };
                await WriteDurableAsync(Path.Combine(candidate, "manifest.json"), json, cancellationToken);
                await WriteDurableAsync(Path.Combine(candidate, "info.json"), JsonSerializer.SerializeToUtf8Bytes(summary, PresetPackage.JsonOptions), cancellationToken);
                lock (files.Gate)
                {
                    // Another operation may have deleted or replaced this preset while the ZIP was being written.
                    var current = ReadLocalPreset(directory, id);
                    if (!current.Manifest.AsSpan().SequenceEqual(original.Manifest) || !current.Info.AsSpan().SequenceEqual(original.Info) || !(current.Locations ?? []).AsSpan().SequenceEqual(original.Locations ?? []))
                        throw new IOException("预设已被其他操作修改，请重试。");
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] journal = JsonSerializer.SerializeToUtf8Bytes(new RenameJournal(1, id), PresetPackage.JsonOptions);
                    string pendingJournal = Path.Combine(staging, "rename.pending");
                    using (var file = new FileStream(pendingJournal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { file.Write(journal); file.Flush(true); }
                    // Recovery never observes an empty or partially written published journal.
                    File.Move(pendingJournal, Path.Combine(staging, "rename.json"));
                    // From the first move onward, finish or roll back without honoring cancellation.
                    try
                    {
                        Directory.Move(directory, Path.Combine(staging, "original"));
                        Directory.Move(candidate, directory);
                    }
                    catch
                    {
                        if (!Directory.Exists(directory) && Directory.Exists(Path.Combine(staging, "original")))
                            Directory.Move(Path.Combine(staging, "original"), directory);
                        throw;
                    }
                    // A cleanup failure must not report a successfully committed rename as failed.
                    try { RemoveRenameStaging(staging); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { AppLog.Warning("Presets", "Rename committed; recovery cleanup will retry", error); }
                }
                return summary;
            }
            finally
            {
                // A journal is retained after an interrupted swap so recovery can preserve the original.
                try { if (staging != null && Directory.Exists(staging) && !File.Exists(Path.Combine(staging, "rename.json"))) RemoveRenameStaging(staging); }
                finally { files.Rename.Release(); }
            }
        }, cancellationToken);
    }

    public Task<PresetSummary> SaveAsync(PresetDocument document, CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveReferencesAsync(document,cancellationToken),cancellationToken);

    public Task<PresetSummary> ImportAsync(string zipPath, CancellationToken cancellationToken = default) => ImportAsync(zipPath,new PresetImportOptions(),cancellationToken);

    public Task<PresetSummary> ImportAsync(string zipPath, PresetImportOptions options, CancellationToken cancellationToken = default) =>
        Task.Run(() => ImportReferencesAsync(zipPath,options,cancellationToken),cancellationToken);

    private static async Task VerifyResourceAsync(string path, PresetResource resource, CancellationToken cancellationToken)
    {
        PresetPackage.CheckLocalPath(path,resource.Kind=="media");
        var info = new FileInfo(path); if (!info.Exists || info.Length != resource.Bytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("已有预设资源与内容标识不一致。");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant() != resource.Sha256) throw new InvalidDataException("已有预设资源校验失败。");
    }

    public async Task ExportAsync(string id, string zipPath, CancellationToken cancellationToken = default)
    {
        FileStream? snapshot = null; byte[]? localManifest = null; Dictionary<string,string>? locations = null;
        lock (files.Gate)
        {
            RecoverRenames();
            string directory = PresetDirectory(id); CheckDirectory(directory);
            string source = Path.Combine(directory, "preset.zip");
            if (File.Exists(Path.Combine(directory,"locations.json")))
            {
                var local = ReadLocalPreset(directory,id); localManifest = local.Manifest;
                locations = ParseLocations(local.Locations!,PresetPackage.ParseManifest(localManifest));
            }
            else {
            CheckLocalFile(source, MaximumArchiveBytes);
            // An export already in progress retains its coherent old snapshot if a rename commits.
            snapshot = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, true);
            }
        }
        if (localManifest != null)
        {
            // The immutable metadata snapshot stays coherent across rename/delete.
            var manifest = PresetPackage.ParseManifest(localManifest);
            await Task.Run(() => PresetPackage.Materialize(manifest,resources,false,cancellationToken,locations),cancellationToken);
            await AtomicExportAsync(zipPath,temporary=>PresetPackage.WriteReferencedArchiveAsync(manifest,localManifest,locations!,temporary,cancellationToken),cancellationToken);
            return;
        }
        await using var input = snapshot;
        await AtomicExportAsync(zipPath, async temporary =>
        {
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input!.CopyToAsync(output, cancellationToken);
        }, cancellationToken);
    }

    public Task ExportAsync(PresetDocument document, string zipPath, CancellationToken cancellationToken = default) =>
        AtomicExportAsync(zipPath, temporary => PresetPackage.WriteAsync(document, temporary, cancellationToken), cancellationToken);

    private static async Task AtomicExportAsync(string destination, Func<string, Task> write, CancellationToken cancellationToken)
    {
        destination = Path.GetFullPath(destination);
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("请选择 ZIP 保存位置。");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能覆盖链接文件。");
        string temporary = Path.Combine(parent, ".preset-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { await write(temporary); cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Delete(string id)
    {
        lock (files.Gate)
        {
        RecoverRenames();
        string directory = PresetDirectory(id); if (!Directory.Exists(directory)) return;
        CheckDirectory(directory);
        CheckPresetFiles(directory);
        Directory.Delete(directory, true);
        // Content-addressed resources intentionally survive: library rows and
        // undo history can still reference them after a preset is deleted.
        }
    }

    public PresetApplyResult Apply(string id, IReadOnlyDictionary<string, string> screenMap, PresetSelection? selection = null, string? expectedRevision = null, string? primaryScreen = null) => Apply(Read(id), screenMap, selection, expectedRevision, primaryScreen);

    public PresetApplyResult Apply(PresetDocument document, IReadOnlyDictionary<string, string> screenMap, PresetSelection? selection = null, string? expectedRevision = null, string? primaryScreen = null)
    {
        PresetPackage.ValidateDocument(document);
        var requested = selection ?? document.Selection;
        var chosen = new PresetSelection
        {
            Playing = requested.Playing && document.Selection.Playing, Library = requested.Library && document.Selection.Library,
            Effects = requested.Effects && document.Selection.Effects, Widgets = requested.Widgets && document.Selection.Widgets,
            IncludeWidgetPrivateData = requested.IncludeWidgetPrivateData && document.Selection.IncludeWidgetPrivateData
        };
        if (!chosen.Any) throw new ArgumentException("请选择预设中包含的内容。");
        var mapping = screenMap.Where(p => document.Screens.Contains(p.Key, StringComparer.Ordinal)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (screenMap.Keys.Any(k => !document.Screens.Contains(k, StringComparer.Ordinal)) || mapping.Values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 128 || v.Any(char.IsControl)) || mapping.Values.Distinct(StringComparer.Ordinal).Count() != mapping.Count) throw new ArgumentException("显示器映射无效或重复。");
        if (mapping.Count == 0 && !chosen.Library) throw new ArgumentException("请至少映射一个显示器，或只导入素材库。");
        string[] screens = mapping.Values.ToArray();
        var wanted = document.Bindings.Where(b => mapping.ContainsKey(b.ScreenId)).Select(b => b.AssetId)
            .Concat(document.Playback.Where(p => mapping.ContainsKey(p.Key)).SelectMany(p => p.Value.AssetIds.Concat((p.Value.CustomPlaylists ?? []).SelectMany(l => l.AssetIds)))).ToHashSet(StringComparer.Ordinal);
        var selectedAssets = document.Assets.Where(a => chosen.Library || chosen.Playing && wanted.Contains(a.Id)).ToArray();
        var existingAssets = store.CapturePreset([],true).Assets;
        foreach (var asset in selectedAssets)
        {
            PresetPackage.CheckLocalPath(asset.Path,true);
            var info = new FileInfo(asset.Path);
            if (!verifiedResources.TryGetValue(Path.GetFullPath(asset.Path),out var stamp) || !info.Exists || info.Length != asset.Bytes || info.Length != stamp.Bytes || info.LastWriteTimeUtc.Ticks != stamp.Modified)
                throw new InvalidDataException("预设资源已变化，请重新读取预设后再应用。");
        }
        var assetMap = selectedAssets.ToDictionary(a => a.Id, a => existingAssets.FirstOrDefault(e=>string.Equals(e.Path,a.Path,StringComparison.OrdinalIgnoreCase))?.Id ??
            "preset-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(a.Path).ToUpperInvariant()))).ToLowerInvariant(), StringComparer.Ordinal);
        var assets = selectedAssets.Select(a => a with { Id = assetMap[a.Id] }).DistinctBy(a => a.Id).ToArray();
        var playback = chosen.Playing ? document.Playback.Where(p => mapping.ContainsKey(p.Key)).ToDictionary(p => mapping[p.Key], p => p.Value with
        {
            FolderId = null, AssetIds = p.Value.AssetIds.Select(id => assetMap[id]).ToList(),
            CustomPlaylists = (p.Value.CustomPlaylists ?? []).Select(l => l with { AssetIds = l.AssetIds.Select(id => assetMap[id]).ToList() }).ToList()
        }, StringComparer.Ordinal) : new();
        WidgetConfiguration? widgets = null;
        if (chosen.Widgets && document.Widgets != null && mapping.Count > 0)
        {
            var projected = ProjectWidgets(document.Widgets, mapping.Keys.ToArray(), chosen.IncludeWidgetPrivateData);
            projected = projected with
            {
                Widgets = projected.Widgets.Select(w => w with { Screen = mapping[w.Screen] }).ToArray(),
                LayoutGroups = projected.LayoutGroups.Select(g => g with { Screen = mapping[g.Screen] }).ToArray()
            };
            var current = store.CapturePreset(screens, false).Widgets ?? new();
            widgets = MergeWidgets(current, projected, screens, primaryScreen ?? screens.FirstOrDefault() ?? "");
        }
        var applied = document with
        {
            Selection = chosen, Screens = screens, Assets = assets,
            Bindings = chosen.Playing ? document.Bindings.Where(b => mapping.ContainsKey(b.ScreenId)).Select(b => b with { ScreenId = mapping[b.ScreenId], AssetId = assetMap[b.AssetId], Effects = VideoEffects.Original }).ToArray() : [],
            Playback = playback,
            Volumes = chosen.Playing ? document.Volumes.Where(p => mapping.ContainsKey(p.Key)).ToDictionary(p => mapping[p.Key], p => p.Value, StringComparer.Ordinal) : new(),
            SoundEnabled = chosen.Playing ? document.SoundEnabled.Where(p => mapping.ContainsKey(p.Key)).ToDictionary(p => mapping[p.Key], p => p.Value, StringComparer.Ordinal) : new(),
            ScreenProfiles = chosen.Effects ? document.ScreenProfiles.Where(p => mapping.ContainsKey(p.Key)).ToDictionary(p => mapping[p.Key], p => !chosen.Playing && document.Bindings.FirstOrDefault(b => b.ScreenId == p.Key) is { } current && document.AssetEffects.TryGetValue(current.AssetId, out var specific) ? p.Value with { Effects = specific.Resolve(p.Value.Effects) } : p.Value, StringComparer.Ordinal) : new(),
            AssetEffects = chosen.Effects ? document.AssetEffects.Where(p => assetMap.ContainsKey(p.Key)).GroupBy(p => assetMap[p.Key]).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal) : new(),
            Widgets = widgets
        };
        store.ApplyPreset(applied, expectedRevision);
        return new(screens, widgets != null, chosen.Effects && mapping.Count > 0, assets.Length > 0, chosen.Playing && mapping.Count > 0);
    }

    private static string EffectiveWidgetScreen(WidgetConfiguration state, WidgetInstance widget, string primary)
    {
        string screen = state.LayoutGroups.FirstOrDefault(g => g.Id == widget.LayoutGroupId)?.Screen ?? widget.Screen;
        return string.IsNullOrEmpty(screen) ? primary : screen;
    }

    private static WidgetConfiguration MergeWidgets(WidgetConfiguration current, WidgetConfiguration incoming, string[] screens, string primary)
    {
        var keep = current.Widgets.Where(w => !screens.Contains(EffectiveWidgetScreen(current, w, primary), StringComparer.Ordinal)).ToArray();
        var keepGroups = current.LayoutGroups.Where(g => !screens.Contains(string.IsNullOrEmpty(g.Screen) ? primary : g.Screen, StringComparer.Ordinal) || keep.Any(w => w.LayoutGroupId == g.Id)).ToArray();
        var layoutMap = incoming.LayoutGroups.ToDictionary(g => g.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        var groups = current.Groups.ToList();
        var cards = current.Shortcuts.ToList();
        var groupMap = new Dictionary<string,string>(StringComparer.Ordinal);
        var cardMap = new Dictionary<string,string>(StringComparer.Ordinal);
        // Keep editable bindings independent on the other displays.
        // A widget can point to a saved card outside its own shortcut group.
        var reserved = keep.Select(w=>w.Group).ToHashSet(StringComparer.Ordinal);
        reserved.Add("common");
        var keptCardIds = keep.Select(w=>w.ShortcutId).ToHashSet(StringComparer.Ordinal);
        reserved.UnionWith(current.Shortcuts.Where(c=>keptCardIds.Contains(c.Id)).Select(c=>c.Group));
        var localCards = current.Shortcuts.OrderBy(c=>c.Order).ToLookup(c=>c.Group);
        var presetCards = incoming.Shortcuts.OrderBy(c=>c.Order).ToLookup(c=>c.Group);
        foreach(var group in incoming.Groups)
        {
            var source = presetCards[group.Id].ToArray();
            var contents = source.Select(c=>(c.Order,c.ContentKey())).ToArray();
            var match = current.Groups.FirstOrDefault(g=>!reserved.Contains(g.Id) && g.Name==group.Name && g.Order==group.Order &&
                localCards[g.Id].Select(c=>(c.Order,c.ContentKey())).SequenceEqual(contents));
            string groupId = match?.Id ?? Guid.NewGuid().ToString("N");
            groupMap.Add(group.Id,groupId);
            reserved.Add(groupId);
            if(match!=null)
            {
                foreach(var (from,to) in source.Zip(localCards[groupId]))cardMap.Add(from.Id,to.Id);
                continue;
            }
            groups.Add(group with {Id=groupId});
            foreach(var card in source)
            {
                var copy=card with {Id=Guid.NewGuid().ToString("N"),Group=groupId};
                cardMap.Add(card.Id,copy.Id);
                cards.Add(copy);
            }
        }
        return (current with
        {
            Widgets = keep.Concat(incoming.Widgets.Select(w => w with { Id = Guid.NewGuid().ToString("N"), Group = groupMap[w.Group], LayoutGroupId = w.LayoutGroupId == null ? null : layoutMap[w.LayoutGroupId], ShortcutId = w.ShortcutId == null ? null : cardMap[w.ShortcutId] })).ToArray(),
            LayoutGroups = keepGroups.Concat(incoming.LayoutGroups.Select(g => g with { Id = layoutMap[g.Id] })).ToArray(),
            Groups = groups.ToArray(),
            Shortcuts = cards.ToArray()
        }).WithoutUnusedShortcuts().Validate();
    }

    private static PresetSummary Summary(string id, PresetDocument document, long bytes) => new(id, document.Name, document.CreatedUtc, document.Selection, document.Assets.Length, document.Bindings.Length, document.Widgets?.Widgets.Length ?? 0, bytes) { Screens = document.Screens };
    private const long MaximumArchiveBytes = PresetPackageLimits.MaxPackageBytes + PresetPackageLimits.MaxManifestBytes + 16 * 1024 * 1024;

    private static void CheckLocalFile(string path, long maximum)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > maximum || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("本地预设文件缺失、过大或是链接。");
    }

    private static byte[] ReadLocalFile(string path, int maximum)
    {
        CheckLocalFile(path, maximum);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) throw new InvalidDataException("本地预设文件过大。");
        byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }

    private static void CheckPresetFiles(string directory)
    {
        CheckDirectory(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            if (Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Path.GetFileName(path) is not ("preset.zip" or "manifest.json" or "info.json" or "locations.json"))
                throw new IOException("本地预设包含额外文件或链接。");
    }

    private static LocalPreset ReadLocalPreset(string directory, string id)
    {
        CheckPresetFiles(directory);
        byte[] json = ReadLocalFile(Path.Combine(directory, "manifest.json"), PresetPackageLimits.MaxManifestBytes);
        byte[] metadata = ReadLocalFile(Path.Combine(directory, "info.json"), 64 * 1024);
        var manifest = PresetPackage.ParseManifest(json);
        var summary = JsonSerializer.Deserialize<PresetSummary>(metadata, PresetPackage.JsonOptions);
        string archive = Path.Combine(directory, "preset.zip"),locationFile = Path.Combine(directory,"locations.json");
        byte[]? locations = File.Exists(locationFile) ? ReadLocalFile(locationFile,PresetPackageLimits.MaxManifestBytes) : null;
        if (locations == null) CheckLocalFile(archive, MaximumArchiveBytes);
        else { _ = ParseLocations(locations,manifest); if (File.Exists(archive)) throw new InvalidDataException("本地预设格式不明确。"); }
        if (summary != null) PresetPackage.RequireName(summary.Name);
        if (summary == null || summary.Id != id ||
            summary.Name != manifest.Document["Name"]?.GetValue<string>() ||
            summary.CreatedUtc != manifest.Document["CreatedUtc"]?.GetValue<DateTimeOffset>() ||
            summary.ZipBytes != (locations == null ? new FileInfo(archive).Length : 0))
            throw new InvalidDataException("本地预设名称、身份或清单不一致。");
        if (locations == null) PresetPackage.CheckStoredArchive(archive, json);
        return new(json, metadata, summary, locations);
    }

    private static async Task WriteDurableAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await file.WriteAsync(bytes, cancellationToken); await file.FlushAsync(cancellationToken); file.Flush(true);
    }

    private void CheckRenameStaging(string staging)
    {
        string full = Path.GetFullPath(staging);
        if (!string.Equals(Path.GetDirectoryName(full), stagingRoot, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new IOException("预设改名恢复目录超出范围。");
        CheckDirectory(stagingRoot); CheckDirectory(full);
        foreach (string path in Directory.EnumerateFileSystemEntries(full))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("预设改名恢复目录包含链接。");
            string name = Path.GetFileName(path);
            if (Directory.Exists(path)) { if (name is not ("candidate" or "original")) throw new IOException("预设改名恢复目录包含未知内容。"); CheckPresetFiles(path); }
            else if (name is not ("rename.json" or "rename.pending")) throw new IOException("预设改名恢复目录包含未知内容。");
        }
    }

    private void RemoveRenameStaging(string staging)
    {
        CheckRenameStaging(staging);
        // Delete only the three known ordinary files; never recurse through untrusted paths.
        foreach (string name in new[] { "original", "candidate" })
        {
            string directory = Path.Combine(staging, name);
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
        string pending = Path.Combine(staging, "rename.pending"); if (File.Exists(pending)) File.Delete(pending);
        string journal = Path.Combine(staging, "rename.json"); if (File.Exists(journal)) File.Delete(journal);
        Directory.Delete(staging);
    }

    private void RecoverRenames()
    {
        CheckDirectory(stagingRoot); CheckDirectory(presets);
        foreach (string staging in Directory.EnumerateDirectories(stagingRoot))
        {
            string journalPath = Path.Combine(staging, "rename.json");
            if (!File.Exists(journalPath)) continue;
            CheckRenameStaging(staging);
            var journal = JsonSerializer.Deserialize<RenameJournal>(ReadLocalFile(journalPath, 4096), PresetPackage.JsonOptions);
            if (journal == null || journal.Version != 1 || !Guid.TryParseExact(journal.Id, "N", out _)) throw new InvalidDataException("预设改名恢复记录无效。");
            string current = PresetDirectory(journal.Id), original = Path.Combine(staging, "original"), candidate = Path.Combine(staging, "candidate");
            if (Directory.Exists(current))
            {
                // Never overwrite a committed or subsequently changed canonical preset.
                ReadLocalPreset(current, journal.Id);
                if (Directory.Exists(original) && Directory.Exists(candidate)) throw new IOException("预设改名恢复存在冲突，已保留全部副本。");
            }
            else
            {
                if (!Directory.Exists(original)) throw new IOException("预设改名恢复缺少原始副本，已保留临时文件。");
                ReadLocalPreset(original, journal.Id); Directory.Move(original, current);
            }
            RemoveRenameStaging(staging);
        }
    }

    private string PresetDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("预设标识无效。");
        CheckDirectory(presets); return Path.Combine(presets, id);
    }
    private static void EnsureDirectory(string path) { Directory.CreateDirectory(path); CheckDirectory(path); }
    private static void CheckDirectory(string path) { if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("预设目录不存在或是链接。"); }
    private string NewStaging() { CheckDirectory(stagingRoot); string path = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private void RemoveStaging(string path)
    {
        string full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) != stagingRoot || !Guid.TryParseExact(Path.GetFileName(full), "N", out _)) throw new IOException("临时目录超出范围。");
        if (Directory.Exists(full))
        {
            CheckDirectory(full);
            var entries = Directory.EnumerateFileSystemEntries(full).ToArray();
            foreach (string item in entries)
            {
                if ((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0) throw new IOException("预设临时目录含链接，已保留。");
                if (Directory.Exists(item))
                {
                    if (Path.GetFileName(item)!="candidate") throw new IOException("预设临时目录含额外目录，已保留。");
                    CheckPresetFiles(item);
                }
                else if (Path.GetFileName(item) is not ("import.json" or "import.pending") &&
                    !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(item),@"^[a-f0-9]{64}\.bin$"))
                    throw new IOException("预设临时目录含额外文件，已保留。");
            }
            foreach (string item in entries)
                if (Directory.Exists(item)) { foreach (string file in Directory.EnumerateFiles(item)) File.Delete(file); Directory.Delete(item); }
                else File.Delete(item);
            Directory.Delete(full);
        }
    }
}
