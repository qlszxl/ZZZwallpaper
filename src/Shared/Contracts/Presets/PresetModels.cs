namespace LumaWall.Contracts.Presets;

public sealed record PresetSelection
{
    public bool Playing { get; init; } = true;
    public bool Library { get; init; }
    public bool Effects { get; init; } = true;
    public bool Widgets { get; init; } = true;
    public bool IncludeWidgetPrivateData { get; init; }
    public bool Any => Playing || Library || Effects || Widgets;
}

/// <summary>Application snapshot. File paths are local here; ZIP manifests replace them with owned resource references.</summary>
public sealed record PresetDocument
{
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "新预设";
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public PresetSelection Selection { get; init; } = new();
    public string[] Screens { get; init; } = [];
    public MediaAsset[] Assets { get; init; } = [];
    public ScreenBinding[] Bindings { get; init; } = [];
    public Dictionary<string, PlaybackSettings> Playback { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Volumes { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> SoundEnabled { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, WallpaperProfile> ScreenProfiles { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, AssetEffects> AssetEffects { get; init; } = new(StringComparer.Ordinal);
    public WidgetConfiguration? Widgets { get; init; }
}

public sealed record PresetSummary(string Id, string Name, DateTimeOffset CreatedUtc, PresetSelection Selection,
    int AssetsCount, int BindingsCount, int WidgetsCount, long ZipBytes)
{
    public string[] Screens { get; init; } = [];
}

public sealed record PresetApplyResult(string[] Screens, bool WidgetsChanged, bool EffectsChanged, bool LibraryChanged, bool PlayingChanged);

public sealed record PresetImportPreview(string Name, PresetSelection Selection, int MediaCount, long MediaBytes, long PackageBytes, string Fingerprint);

public sealed record PresetImportOptions
{
    public string? MediaDirectory { get; init; }
    public MediaAsset[] ExistingAssets { get; init; } = [];
    public string? ExpectedFingerprint { get; init; }
}
