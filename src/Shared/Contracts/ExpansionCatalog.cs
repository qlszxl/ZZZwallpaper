using System.Collections.Frozen;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record ExpansionEntry(string Id, string Title, string Icon, string? Requires = null, string? Implementation = null, string? EntryPoint = null, bool Animated = false, PackageGrid? Grid = null) { public string? Description {get;init;} }
public sealed record ExpansionContribution(string Target, string Id, string Provides);

public sealed record ExpansionManifest(int ApiVersion, string Id, string Name)
{
    public string Version { get; init; } = "1.0.0";
    public Dictionary<string,string> Files { get; init; } = [];
    public ExpansionEntry[] Effects { get; init; } = [];
    public ExpansionEntry[] Widgets { get; init; } = [];
    public ParticleAppearance[] ParticleStyles {get;init;}=[];
    public ExpansionContribution[] Contributions { get; init; } = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ExpansionManifest Read(Stream source)
    {
        var pack = JsonSerializer.Deserialize<ExpansionManifest>(source, Json);
        if (pack is not { ApiVersion: 1 or 2 } || string.IsNullOrWhiteSpace(pack.Id) || string.IsNullOrWhiteSpace(pack.Name)
            || pack.Effects == null || pack.Widgets == null || pack.Contributions == null)
            throw new InvalidDataException("不支持的扩展包清单。");
        return pack;
    }
}

// A package provider owns its resource format and installation transaction.
public interface IExpansionPackage
{
    ExpansionManifest Descriptor { get; }
    bool IsInstalled { get; }
    string Summary { get; }
    string RemovalMessage { get; }
    string? CoverPath { get; }
    bool Provides(ExpansionContribution contribution);
    void Import(string path);
    void Remove();
}

public static class ExpansionCatalog
{
    public static ExpansionManifest Base { get; } = LoadBase();
    public static IReadOnlyDictionary<EffectKind, ExpansionEntry> Effects { get; } = Entries<EffectKind>(Base.Effects.Where(e=>e.Implementation==null).ToArray());
    public static IReadOnlyDictionary<WidgetKind, ExpansionEntry> Widgets { get; } = Entries<WidgetKind>(Base.Widgets.Where(e=>e.Implementation==null).ToArray());

    private static ExpansionManifest LoadBase()
    {
        using var file = File.OpenRead(Path.Combine(FeaturePacks.BaseDirectory, "expansion.json"));
        var manifest = ExpansionManifest.Read(file);
        if (manifest.Id != "base" || manifest.Effects.Length == 0 || manifest.Widgets.Length == 0)
            throw new InvalidDataException("基础扩展包缺少注册项。");
        return manifest;
    }

    private static IReadOnlyDictionary<T, ExpansionEntry> Entries<T>(ExpansionEntry[] entries) where T : struct, Enum
    {
        var result = new Dictionary<T, ExpansionEntry>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.Icon)
                || !Enum.TryParse<T>(entry.Id, out var kind) || kind.ToString() != entry.Id || !Enum.IsDefined(kind)
                || !result.TryAdd(kind, entry))
                throw new InvalidDataException("基础扩展包注册项无效或重复。");
        }
        return result.AsReadOnly();
    }

    public static ExpansionEntry Effect(EffectKind kind) => Effects[kind];
    public static ExpansionEntry Widget(WidgetKind kind) => Widgets[kind];

    // Resolve lazily because binding a provider validates its targets against the base catalog.
    public static IReadOnlyList<IExpansionPackage> Packages => LegacyMaterials.Current.IsInstalled?[..FeaturePacks.Addons,LegacyMaterials.Current]:FeaturePacks.Addons.ToArray();

    public static bool ProvidesEffect(EffectKind kind, string key) =>
        Provided(new("effect", Effect(kind).Id, key));
    public static bool WidgetAvailable(WidgetKind kind) => Widgets.TryGetValue(kind, out var entry)
        && (entry.Requires == null || Provided(new("widget", entry.Id, entry.Requires)));
    public static bool WidgetAvailable(WidgetInstance widget) => widget.Kind == WidgetKind.Extension
        ? FeaturePacks.Widgets.Any(item => item.Key == widget.PackageWidgetId)
        : WidgetAvailable(widget.Kind);

    // Filter a view of the saved layout. Importing the pack restores the same instances.
    public static WidgetConfiguration AvailableWidgets(WidgetConfiguration saved) =>
        Array.TrueForAll(saved.Widgets, WidgetAvailable) ? saved
            : saved with { Widgets = Array.FindAll(saved.Widgets, WidgetAvailable) };

    private static bool Provided(ExpansionContribution capability)
    {
        foreach (var provider in FeaturePacks.Addons)
            if (provider.IsInstalled && provider.Provides(capability)) return true;
        return false;
    }

    public static void Import(string path)
    {
        using var file=File.OpenRead(path);
        if(file.Length>96L*1024*1024)throw new InvalidDataException("扩展包超过大小限制。");
        using var zip=new ZipArchive(file,ZipArchiveMode.Read);
        var header=zip.GetEntry("expansion.json");
        if(header==null||header.Length is <=0 or >65536)throw new InvalidDataException("请导入新版功能扩展包。");
        using var data=header.Open();var manifest=ExpansionManifest.Read(data);
        if(manifest.ApiVersion!=2)throw new InvalidDataException("这是旧版素材包，请导入包含组件实现的新版扩展包。");
        FeaturePacks.Import(zip,manifest);
    }

    internal static FrozenSet<ExpansionContribution> Bind(ExpansionManifest pack)
    {
        var bindings = new HashSet<ExpansionContribution>();
        foreach (var binding in pack.Contributions)
        {
            bool supported = binding?.Target switch
            {
                "effect" => Effects.Values.Any(e => e.Id == binding.Id),
                "widget" => Widgets.Values.Any(w => w.Id == binding.Id),
                _ => false
            };
            if (!supported || string.IsNullOrWhiteSpace(binding!.Provides) || !bindings.Add(binding))
                throw new InvalidDataException("扩展包的对接目标无效或重复。");
        }
        return bindings.ToFrozenSet();
    }
}
