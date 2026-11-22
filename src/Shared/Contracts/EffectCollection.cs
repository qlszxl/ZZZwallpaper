using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

[Flags]
public enum EffectKind
{
    Color=1,
    RgbSplit=2,
    Softening=4, // Retired identifier; never reused.
    Glow=8,
    Vignette=16,
    Noise=32,
    Scanlines=64,
    Glitch=128,
    LargeGlitch=256,
    Dots=512,
    DotRipple=1024,
    Blur=2048,
    Distortion=4096,
    AudioReactive=8192,
    HideTaskbar=16384,
    ZoomTunnel=32768,
    Particles=65536,
    WaterWave=131072,
    EdgeOutline=262144,
    Pixelate=524288,
    SunPrism=1048576,
    LightFlare=8388608, // Appended; retired identifiers are never reused.
    Transform=16777216, // Appended visual layer.
    MaskLibrary=2097152,
    ScanCards=4194304, // Retired wire/JSON identifier; never an available effect.

}

// Added membership is separate from enable switches, so disabling an effect
// keeps its settings card. Strengths remain stored when a card is removed.
public sealed record EffectCollection
{
    public const EffectKind Retired=EffectKind.Softening|EffectKind.ScanCards;
    public const EffectKind All=((EffectKind)4194303&~EffectKind.Softening)|EffectKind.LightFlare|EffectKind.Transform;
    private EffectKind added;
    public EffectKind Added {get=>added;init=>added=value&~Retired;}
    public bool ColorEnabled {get;init;}=true;
    public bool HideTaskbarEnabled {get;init;}=true;
    public TaskbarStyle TaskbarStyle {get;init;}=TaskbarStyle.Hidden;
    public TaskbarVisualOptions TaskbarVisual {get;init;}=new();
    // Legacy v28 data only. The withdrawn taskbar-space feature has no runtime consumer.
    public bool ReleaseTaskbarSpace {get;init;}
    // Null uses the central display order, or inherits the display for an asset.
    // Sparse maps override selected layers; every profile renders in stack order.
    private Dictionary<EffectKind,int>? layers;
    public Dictionary<EffectKind,int>? Layers {
        get=>layers;
        init=>layers=value?.Where(pair=>(pair.Key&Retired)==0).ToDictionary(pair=>pair.Key,pair=>pair.Value);
    }
    [JsonIgnore] public bool HasCustomOrder=>Layers!=null;
    public int GetLayer(EffectKind kind)
    {
        int fallback=EffectOrder.DefaultLayer(kind);
        return Layers?.GetValueOrDefault(kind,fallback)??fallback;
    }
    public EffectKind[] OrderedKinds()=>EffectOrder.Default.OrderBy(GetLayer).ToArray();
    public EffectCollection WithLayer(EffectKind kind,int layer)
    {
        _=EffectOrder.DefaultLayer(kind);
        if(layer<EffectOrder.MinLayer||layer>EffectOrder.MaxLayer)throw new ArgumentOutOfRangeException(nameof(layer));
        if(HasCustomOrder&&GetLayer(kind)==layer)return this;
        var layers=EffectOrder.Default.ToDictionary(item=>item,GetLayer);layers[kind]=layer;
        return this with {Layers=layers};
    }
    public EffectCollection Move(EffectKind kind,int targetIndex)
    {
        _=EffectOrder.DefaultLayer(kind);
        if(targetIndex<0||targetIndex>=EffectOrder.Default.Count)throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var order=OrderedKinds().ToList();order.Remove(kind);order.Insert(targetIndex,kind);
        return this with {Layers=order.Select((item,index)=>(item,index)).ToDictionary(pair=>pair.item,pair=>pair.index*10)};
    }
    public EffectCollection Move(EffectKind kind,EffectKind target)
    {
        _=EffectOrder.DefaultLayer(kind);_=EffectOrder.DefaultLayer(target);
        if(kind==target)return this;
        var order=OrderedKinds().ToList();order.Remove(kind);
        return Move(kind,order.IndexOf(target));
    }
    public EffectCollection ResetOrder()=>Layers==null?this:this with {Layers=null};
    public double HideTaskbar {get;init;}=1;
    public double Color {get;init;}=1;
    public double RgbSplit {get;init;}=1;
    [JsonIgnore] public double Softening {get=>0;init {}}
    public double Glow {get;init;}=1;
    public double Vignette {get;init;}=1;
    public double Noise {get;init;}=1;
    public double Scanlines {get;init;}=1;
    public double Glitch {get;init;}=1;
    public double LargeGlitch {get;init;}=1;
    public double Dots {get;init;}=1;
    public double DotRipple {get;init;}=1;
    public double Blur {get;init;}=1;
    public double Distortion {get;init;}=1;
    public double AudioReactive {get;init;}=1;
    public double ZoomTunnel {get;init;}=1;
    public double Particles {get;init;}=1;
    public double WaterWave {get;init;}=1;
    public double EdgeOutline {get;init;}=1;
    public double Pixelate {get;init;}=1;
    public double SunPrism {get;init;}=1;
    public double LightFlare {get;init;}=1;
    public double Transform {get;init;}=1;
    [JsonPropertyName("ScanCards")]
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? RetiredWarehouseStrength {get=>null;init {}}
    public double Strength(EffectKind kind)=>kind switch {
        EffectKind.MaskLibrary=>1,
        EffectKind.HideTaskbar=>HideTaskbar,
        EffectKind.ZoomTunnel=>ZoomTunnel,
        EffectKind.Particles=>Particles,
        EffectKind.WaterWave=>WaterWave,
        EffectKind.EdgeOutline=>EdgeOutline,
        EffectKind.Pixelate=>Pixelate,
        EffectKind.SunPrism=>SunPrism,
        EffectKind.LightFlare=>LightFlare,
        EffectKind.Transform=>Transform,

        EffectKind.Color=>Color,
        EffectKind.RgbSplit=>RgbSplit,
        EffectKind.Softening=>Softening,
        EffectKind.Glow=>Glow,
        EffectKind.Vignette=>Vignette,
        EffectKind.Noise=>Noise,
        EffectKind.Scanlines=>Scanlines,
        EffectKind.Glitch=>Glitch,
        EffectKind.LargeGlitch=>LargeGlitch,
        EffectKind.Dots=>Dots,
        EffectKind.DotRipple=>DotRipple,
        EffectKind.Blur=>Blur,
        EffectKind.Distortion=>Distortion,
        EffectKind.AudioReactive=>AudioReactive,
        _=>throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public EffectCollection WithStrength(EffectKind kind,double value)=>kind switch {
        EffectKind.MaskLibrary=>this,
        EffectKind.HideTaskbar=>this with {HideTaskbar=value},
        EffectKind.ZoomTunnel=>this with {ZoomTunnel=value},
        EffectKind.Particles=>this with {Particles=value},
        EffectKind.WaterWave=>this with {WaterWave=value},
        EffectKind.EdgeOutline=>this with {EdgeOutline=value},
        EffectKind.Pixelate=>this with {Pixelate=value},
        EffectKind.SunPrism=>this with {SunPrism=value},
        EffectKind.LightFlare=>this with {LightFlare=value},
        EffectKind.Transform=>this with {Transform=value},

        EffectKind.Color=>this with {Color=value},
        EffectKind.RgbSplit=>this with {RgbSplit=value},
        EffectKind.Softening=>this with {Softening=value},
        EffectKind.Glow=>this with {Glow=value},
        EffectKind.Vignette=>this with {Vignette=value},
        EffectKind.Noise=>this with {Noise=value},
        EffectKind.Scanlines=>this with {Scanlines=value},
        EffectKind.Glitch=>this with {Glitch=value},
        EffectKind.LargeGlitch=>this with {LargeGlitch=value},
        EffectKind.Dots=>this with {Dots=value},
        EffectKind.DotRipple=>this with {DotRipple=value},
        EffectKind.Blur=>this with {Blur=value},
        EffectKind.Distortion=>this with {Distortion=value},
        EffectKind.AudioReactive=>this with {AudioReactive=value},
        _=>throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public EffectCollection Validate()
    {
        if((Added&~All)!=0)throw new ArgumentException("效果类型无效");
        if(!Enum.IsDefined(TaskbarStyle))throw new ArgumentException("任务栏样式无效");
        (TaskbarVisual??throw new ArgumentException("任务栏外观配置不能为空")).Validate();
        EffectOrder.Validate(Layers);
        foreach(var kind in Enum.GetValues<EffectKind>().Where(kind=>(kind&All)!=0))if(!double.IsFinite(Strength(kind))||Strength(kind)<0||Strength(kind)>1)throw new ArgumentException("效果强度须为 0–100%");
        return this;
    }
}
