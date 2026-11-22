namespace LumaWall.Contracts;

// The visual stack always runs complete passes in this order unless overridden.
// Card presentation and the renderer share this single starting sequence.
public static class EffectOrder
{
    public const int MinLayer=-999,MaxLayer=999;
    public const EffectKind VisualKinds=EffectCollection.All&~(EffectKind.AudioReactive|EffectKind.HideTaskbar|EffectKind.MaskLibrary);
    public static IReadOnlyList<EffectKind> Default {get;}=Array.AsReadOnly(new[] {
        EffectKind.Transform,EffectKind.WaterWave,EffectKind.Pixelate,EffectKind.ZoomTunnel,EffectKind.Blur,EffectKind.Distortion,
        EffectKind.Color,EffectKind.RgbSplit,EffectKind.Glitch,EffectKind.LargeGlitch,
        EffectKind.Dots,EffectKind.DotRipple,EffectKind.Glow,EffectKind.Vignette,EffectKind.Scanlines,
        EffectKind.Noise,EffectKind.EdgeOutline,EffectKind.SunPrism,EffectKind.Particles,EffectKind.LightFlare
    });
    public static bool Supports(EffectKind kind)=>Default.Contains(kind);
    public static int DefaultLayer(EffectKind kind)
    {
        // Keep the former softening gap, including when loading sparse old maps.
        if(kind==EffectKind.Transform)return -10;
        for(int i=1;i<Default.Count;i++)if(Default[i]==kind)return (i<=5?i-1:i)*10;
        throw new ArgumentOutOfRangeException(nameof(kind),"此项目不属于视觉效果栈。");
    }
    public static Dictionary<EffectKind,int>? Clone(IReadOnlyDictionary<EffectKind,int>? layers)
    {
        Validate(layers);
        return layers==null?null:Default.Where(layers.ContainsKey).ToDictionary(kind=>kind,kind=>layers[kind]);
    }
    public static bool Equivalent(IReadOnlyDictionary<EffectKind,int>? left,IReadOnlyDictionary<EffectKind,int>? right)=>
        left==null?right==null:right!=null&&left.Count==right.Count&&left.All(pair=>right.TryGetValue(pair.Key,out int value)&&value==pair.Value);
    public static Dictionary<EffectKind,int>? Merge(IReadOnlyDictionary<EffectKind,int>? baseline,IReadOnlyDictionary<EffectKind,int>? specific)
    {
        Validate(specific);
        if(specific==null)return Clone(baseline);
        var result=Clone(baseline)??new();foreach(var pair in specific)result[pair.Key]=pair.Value;
        return Clone(result);
    }
    public static Dictionary<EffectKind,int>? CopySelected(EffectCollection source,EffectCollection target,EffectKind kinds)
    {
        if((kinds&VisualKinds)==0)return Clone(target.Layers);
        if((kinds&VisualKinds)==VisualKinds)return Clone(source.Layers);
        if(source.Layers==null&&target.Layers==null)return null;
        var result=Clone(target.Layers)??new();
        foreach(var kind in Default.Where(kind=>(kinds&kind)!=0))result[kind]=source.GetLayer(kind);
        return Clone(result);
    }
    // A draft exposes effective display + asset layers. Persist only its layer
    // delta into the asset, without capturing inherited parameter values.
    public static Dictionary<EffectKind,int>? ApplyDelta(EffectCollection current,EffectCollection before,EffectCollection after)
    {
        if(Equivalent(before.Layers,after.Layers))return Clone(current.Layers);
        if(after.Layers==null)return null;
        var result=Clone(current.Layers)??new();
        foreach(var kind in Default)if(before.GetLayer(kind)!=after.GetLayer(kind))result[kind]=after.GetLayer(kind);
        return Clone(result);
    }
    public static void Validate(IReadOnlyDictionary<EffectKind,int>? layers)
    {
        if(layers!=null&&(layers.Count>Default.Count||layers.Any(pair=>!Supports(pair.Key)||pair.Value<MinLayer||pair.Value>MaxLayer)))
            throw new ArgumentException("效果层级须为 -999–999，且只支持视觉效果。");
    }
}
