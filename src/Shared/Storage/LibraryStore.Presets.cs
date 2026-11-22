using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using LumaWall.Contracts.Application;

namespace LumaWall.Storage;

public sealed partial class LibraryStore : IPresetStore
{
    public PresetDocument CapturePreset(string[] screens, bool includeLibrary)
    {
        screens = screens.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
        var bindings = Bindings().Where(b => screens.Contains(b.ScreenId, StringComparer.Ordinal)).ToArray();
        var playback = new Dictionary<string, PlaybackSettings>(StringComparer.Ordinal);
        foreach (string screen in screens)
        {
            var settings = Playback(screen);
            if (settings.FolderId is { } folder)
            {
                // A portable queue contains files, never the sender's watched folder path.
                var ids = FolderAssets(folder).Select(a => a.Id).ToList();
                settings = settings with { FolderId = null, AssetIds = ids, PlaylistId = "preset-folder", CustomPlaylists = [new() { Id = "preset-folder", Name = "预设播放列表", AssetIds = ids }] };
            }
            playback[screen] = settings.Validate();
        }
        var wanted = bindings.Select(b => b.AssetId).Concat(playback.Values.SelectMany(p => p.AssetIds.Concat((p.CustomPlaylists ?? []).SelectMany(l => l.AssetIds)))).ToHashSet(StringComparer.Ordinal);
        var assets = Assets().Where(a => includeLibrary || wanted.Contains(a.Id)).ToArray();
        return new()
        {
            Screens = screens, Assets = assets, Bindings = bindings, Playback = playback,
            Volumes = screens.ToDictionary(s => s, s => WallpaperVolume(s), StringComparer.Ordinal),
            SoundEnabled = screens.ToDictionary(s => s, s => GetSetting("videoSound:" + s) != "0", StringComparer.Ordinal),
            ScreenProfiles = screens.ToDictionary(s => s, s => ScreenProfile(s) ?? new(FillMode.Cover, VideoEffects.Original), StringComparer.Ordinal),
            AssetEffects = assets.ToDictionary(a => a.Id, a => AssetProfile(a.Id), StringComparer.Ordinal),
            Widgets = WidgetConfiguration.FromJson(GetSetting("widgets.v1"))
        };
    }

    public void ApplyPreset(PresetDocument document, string? expectedRevision = null)
    {
        if (expectedRevision != null && ConfigurationRevision() is var actual && expectedRevision != actual)
            throw new ConfigurationConflictException(expectedRevision, actual, "app_get_state");
        document.Widgets?.Validate();
        foreach (var profile in document.ScreenProfiles.Values) profile.Effects.Validate();
        foreach (var effects in document.AssetEffects.Values) effects.Validate();
        var oldAssets = Assets();
        foreach (var asset in document.Assets)
        {
            _ = MediaFormats.LocalImportPath(asset.Path);
            for (string? path=Path.GetFullPath(asset.Path);path!=null;path=Path.GetDirectoryName(path))
                if ((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("预设素材路径不能包含链接。");
            if (!File.Exists(asset.Path) || new FileInfo(asset.Path).Length != asset.Bytes || !MediaFormats.Supported(asset.Path))
                throw new InvalidDataException("预设素材必须是已导入的本地资源。");
            if (oldAssets.Any(a => a.Id == asset.Id && !string.Equals(a.Path, asset.Path, StringComparison.OrdinalIgnoreCase)) || oldAssets.Any(a => string.Equals(a.Path, asset.Path, StringComparison.OrdinalIgnoreCase) && a.Id != asset.Id))
                throw new InvalidDataException("预设素材标识与已有素材冲突。");
        }
        var oldBindings = Bindings();
        if (document.Selection.Playing && document.Selection.Effects)
        {
            // Material effects belong to the asset, not a screen. Do not change
            // the picture on a screen the user deliberately left unmapped.
            foreach (var binding in oldBindings.Where(b => !document.Screens.Contains(b.ScreenId, StringComparer.Ordinal)))
            {
                if (!document.AssetEffects.TryGetValue(binding.AssetId, out var incoming)) continue;
                var screenEffects = (ScreenProfile(binding.ScreenId) ?? new(FillMode.Cover, VideoEffects.Original)).Effects;
                var before = AssetProfile(binding.AssetId).Resolve(screenEffects);
                var after = incoming.Resolve(screenEffects);
                if (before.ToJson() != after.ToJson() && PresetRenderSignature(before) != PresetRenderSignature(after))
                    throw new InvalidOperationException("预设素材也在其他屏幕使用，应用其画面效果会改变未选择的屏幕。请映射相关屏幕，或先停止该屏幕播放后再应用。");
            }
        }
        var baseline = document.Screens.ToDictionary(s => s, s => ScreenProfile(s) ?? new(FillMode.Cover, VideoEffects.Original), StringComparer.Ordinal);
        var clearAssetOverrides = document.Selection.Effects && !document.Selection.Playing
            ? oldBindings.Where(b => document.ScreenProfiles.ContainsKey(b.ScreenId)).Select(b => b.AssetId).Distinct(StringComparer.Ordinal).ToArray() : [];
        var profiles = new Dictionary<string, WallpaperProfile>(document.ScreenProfiles, StringComparer.Ordinal);
        // Asset overrides are shared across screens. Before clearing an override
        // on a target, preserve the exact effective image on every skipped screen.
        foreach (var binding in oldBindings.Where(b => clearAssetOverrides.Contains(b.AssetId, StringComparer.Ordinal) && !document.ScreenProfiles.ContainsKey(b.ScreenId)))
        {
            var profile = ScreenProfile(binding.ScreenId) ?? new(FillMode.Cover, VideoEffects.Original);
            profiles[binding.ScreenId] = profile with { Effects = AssetProfile(binding.AssetId).Resolve(profile.Effects) };
        }
        var scopes = new List<UndoScope>();
        foreach (var asset in document.Assets) scopes.Add(new("media", "id", asset.Id));
        foreach (string asset in document.AssetEffects.Keys) scopes.Add(new("asset_effects", "asset", asset));
        foreach (string asset in clearAssetOverrides) scopes.Add(new("asset_effects", "asset", asset));
        foreach (string screen in profiles.Keys.Concat(document.Selection.Playing ? document.Bindings.Select(b => b.ScreenId) : [])) { scopes.Add(new("screen_profile", "screen", screen)); scopes.Add(new("binding", "screen", screen)); }
        foreach (string screen in document.Screens)
        {
            if (document.Selection.Playing || document.Selection.Effects) scopes.Add(new("binding", "screen", screen));
            if (document.Selection.Playing)
                foreach (string prefix in new[] { "playback:", "videoVolume:", "videoSound:" }) scopes.Add(new("settings", "key", prefix + screen));
        }
        if (document.Widgets != null) scopes.Add(new("settings", "key", "widgets.v1"));
        var uniqueScopes = scopes.Distinct().ToArray();
        var beforeRows = uniqueScopes.Select(scope => ReadUndoRows(scope)).ToArray();
        BreakUndoGesture();
        using var history = CaptureUndo("应用预设：" + document.Name, "preset:" + Guid.NewGuid().ToString("N"), uniqueScopes);
        using var transaction = connection.BeginTransaction();
        effectTransaction = transaction;
        try
        {
            void Execute(string sql, params (string Key, object Value)[] values)
            {
                using var cmd = connection.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = sql;
                foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value);
                cmd.ExecuteNonQuery();
            }
            void Setting(string key, string value) => Execute("INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$key", key), ("$value", value));
            foreach (var asset in document.Assets)
                Execute("INSERT INTO media(id,path,name,bytes,duration,width,height,codec,color,decoder) VALUES($id,$path,$name,$bytes,$duration,$width,$height,$codec,$color,$decoder) ON CONFLICT(id) DO NOTHING",
                    ("$id", asset.Id), ("$path", asset.Path), ("$name", asset.Name), ("$bytes", asset.Bytes), ("$duration", asset.Duration), ("$width", asset.Width), ("$height", asset.Height), ("$codec", asset.Codec), ("$color", asset.Color), ("$decoder", asset.Decoder));
            foreach (var (id, effects) in document.AssetEffects)
                Execute("INSERT INTO asset_effects(asset,overrides,effects) VALUES($id,$overrides,$effects) ON CONFLICT(asset) DO UPDATE SET overrides=excluded.overrides,effects=excluded.effects", ("$id", id), ("$overrides", (long)effects.Overrides), ("$effects", EncodeEffects(effects.Effects)));
            foreach (string id in clearAssetOverrides) Execute("DELETE FROM asset_effects WHERE asset=$id", ("$id", id));
            foreach (var (screen, profile) in profiles)
                Execute("INSERT INTO screen_profile(screen,fill,effects) VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects", ("$screen", screen), ("$fill", (int)profile.Fill), ("$effects", EncodeEffects(profile.Effects)));
            if (document.Selection.Playing)
            {
                foreach (var binding in document.Bindings)
                {
                    var profile = document.ScreenProfiles.GetValueOrDefault(binding.ScreenId) ?? baseline[binding.ScreenId];
                    Execute("INSERT INTO screen_profile(screen,fill,effects) VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill", ("$screen", binding.ScreenId), ("$fill", (int)binding.Fill), ("$effects", EncodeEffects(profile.Effects)));
                }
                foreach (var (screen, playback) in document.Playback) Setting("playback:" + screen, JsonSerializer.Serialize(playback.Validate()));
                foreach (var (screen, volume) in document.Volumes)
                {
                    if (volume > 0) Setting("videoVolume:" + screen, Math.Clamp(volume, 1, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Setting("videoSound:" + screen, document.SoundEnabled.GetValueOrDefault(screen, volume > 0) ? "1" : "0");
                }
                foreach (string screen in document.Screens) Execute("DELETE FROM binding WHERE screen=$screen", ("$screen", screen));
            }
            var bindings = document.Selection.Playing ? document.Bindings : oldBindings.Where(b => profiles.ContainsKey(b.ScreenId)).ToArray();
            foreach (var binding in bindings)
            {
                var profile = profiles.GetValueOrDefault(binding.ScreenId) ?? baseline[binding.ScreenId];
                var effects = AssetProfile(binding.AssetId).Resolve(profile.Effects);
                Execute("INSERT INTO binding(screen,asset,fill,paused,revision,effects) VALUES($screen,$asset,$fill,$paused,$revision,$effects) ON CONFLICT(screen) DO UPDATE SET asset=excluded.asset,fill=excluded.fill,paused=excluded.paused,revision=excluded.revision,effects=excluded.effects",
                    ("$screen", binding.ScreenId), ("$asset", binding.AssetId), ("$fill", (int)(document.Selection.Playing ? binding.Fill : profile.Fill)), ("$paused", binding.UserPaused ? 1 : 0), ("$revision", binding.Revision + 1), ("$effects", EncodeEffects(effects)));
            }
            if (document.Widgets != null) Setting("widgets.v1", document.Widgets.ReconcileLocalState().ToJson());
            // The normal history queue evicts entries above 32 MiB. A preset is
            // one atomic action: never commit an action that would evict itself.
            long undoBytes = 0;
            for (int i = 0; i < uniqueScopes.Length; i++)
            {
                string after = ReadUndoRows(uniqueScopes[i], transaction);
                if (beforeRows[i] != after) undoBytes = checked(undoBytes + ((long)beforeRows[i].Length + after.Length) * 2);
                if (undoBytes > 32L * 1024 * 1024) throw new InvalidOperationException("预设配置过大，无法保留完整撤销记录，因此未应用。请减少组件图片或分次应用。");
            }
            transaction.Commit(); assetsByPath = null; PlaybackLibraryRevision++;
        }
        finally { effectTransaction = null; BreakUndoGesture(); }
    }

    private static string PresetRenderSignature(VideoEffects effects)
    {
        if (effects.IsNeutral) return "neutral";
        EffectKind active = 0;
        foreach (var kind in Enum.GetValues<EffectKind>())
            if ((kind & AssetEffects.Allowed) != 0 && !EffectProjection.Isolate(effects, kind).IsNeutral) active |= kind;
        // Audio can animate a currently neutral parameter. Retain all of its
        // available targets rather than comparing only a single static frame.
        var rendering = effects.ForRendering();
        if (rendering.AudioReactive && rendering.AudioAmount > 0) active |= effects.GetCollection().Added & AssetEffects.Allowed;
        var projected = AssetEffects.Copy(effects, VideoEffects.Original, active) with { Intensity = effects.Intensity };
        if ((active & EffectKind.Blur) != 0)
            projected = projected with { BlurMotion = effects.BlurMotion, BlurMotionMode = effects.BlurMotionMode, BlurMotionSpeed = effects.BlurMotionSpeed, BlurCoverage = effects.BlurCoverage, BlurScale = effects.BlurScale, BlurFeather = effects.BlurFeather, BlurAngle = effects.BlurAngle };
        var order = effects.GetCollection().OrderedKinds().Where(k => (k & active) != 0)
            .Concat(EffectOrder.Default.Where(k => (k & active) == 0)).Select((kind, index) => (kind, index)).ToDictionary(p => p.kind, p => p.index);
        projected = projected with { Collection = projected.GetCollection() with { Layers = order } };
        return projected.ToWire();
    }
}
