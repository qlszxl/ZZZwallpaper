namespace LumaWall.Contracts;

public sealed record TransformOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=1;
    public double Scale {get;init;}=1;
    public double ScaleX {get;init;}=1;
    public double ScaleY {get;init;}=1;
    public double Rotation {get;init;}
    public double PositionX {get;init;}
    public double PositionY {get;init;}
    public double SkewX {get;init;}
    public double SkewY {get;init;}
    public double CenterX {get;init;}=.5;
    public double CenterY {get;init;}=.5;
    // 0 black, 1 clamp, 2 mirror, 3 repeat.
    public int BorderMode {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Active=>Enabled&&Strength>0&&(Scale!=1||ScaleX!=1||ScaleY!=1||Rotation!=0||PositionX!=0||PositionY!=0||SkewX!=0||SkewY!=0);
    public TransformOptions Validate()
    {
        EffectRange.Check(Strength,0,1);EffectRange.Check(Scale,.1,4);
        EffectRange.Check(ScaleX,.25,4);EffectRange.Check(ScaleY,.25,4);
        EffectRange.Check(Rotation,-180,180);EffectRange.Check(PositionX,-1,1);EffectRange.Check(PositionY,-1,1);
        EffectRange.Check(SkewX,-60,60);EffectRange.Check(SkewY,-60,60);
        EffectRange.Check(CenterX,0,1);EffectRange.Check(CenterY,0,1);EffectRange.Check(BorderMode,0,3);
        return this;
    }
}

public sealed partial record VideoEffects
{
    public TransformOptions Transform {get;init;}=new();
    private string TransformWire()
    {
        var t=Transform;
        return FormattableString.Invariant($" {(t.Enabled?1:0)} {t.Strength:F6} {t.Scale:F6} {t.ScaleX:F6} {t.ScaleY:F6} {t.Rotation:F6} {t.PositionX:F6} {t.PositionY:F6} {t.SkewX:F6} {t.SkewY:F6} {t.CenterX:F6} {t.CenterY:F6} {t.BorderMode}");
    }
    private VideoEffects MigrateAudioZoom()
    {
        if(!AudioZoom&&!AudioRules.Any(r=>r.Parameter==nameof(AudioZoomAmount)))return this;
        var collection=GetCollection();var layers=EffectOrder.Clone(collection.Layers);
        // Zoom used to run before the visual stack. Keep existing layer values.
        if(layers!=null)layers[EffectKind.Transform]=Math.Min(-10,EffectOrder.Default.Where(k=>k!=EffectKind.Transform).Min(collection.GetLayer));
        return this with {Transform=new() {Enabled=true},Collection=collection with {Added=collection.Added|EffectKind.Transform,Layers=layers}};
    }
}
