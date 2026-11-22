using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public record ParticleLayerOptions
{
    public bool Enabled {get;init;}
    // IDs are shared by UI, MCP and the native renderer; see ParticleArtwork.All.
    public int Style {get;init;}
    public bool RandomAppearance {get;init;}
    public int[] RandomStyles {get;init;}=[0,6];
    [JsonIgnore] public bool IsRain=>Style==5&&!RandomAppearance;
    [JsonIgnore] public int RandomMask=>RandomAppearance?RandomStyles.Aggregate(0,(mask,style)=>mask|(1<<style)):0;
    public bool UsesStyle(int style)=>RandomAppearance?RandomStyles.Contains(style):Style==style;
    public int Count {get;init;}=120;
    public double Size {get;init;}=5;
    public double Speed {get;init;}=35;
    public double Direction {get;init;}=90;
    public double Opacity {get;init;}=.8;
    public bool ColorEnabled {get;init;}
    public string Color {get;init;}="#FFFFFF";
    public double Sway {get;init;}=.5;
    public double Rotation {get;init;}=20;
    public bool RandomRotation {get;init;}=true;
    public double Twinkle {get;init;}=.25;
    public bool Fade {get;init;}=true;
    public int Quality {get;init;}=1;
    public bool LocalRegion {get;init;}
    public double CenterX {get;init;}=.5;
    public double CenterY {get;init;}=.5;
    public double Width {get;init;}=1;
    public double Height {get;init;}=1;
    public int Emitter {get;init;}
    public int Emission {get;init;}
    public double Lifetime {get;init;}=8;
    public double BurstInterval {get;init;}=2;
    public double Spread {get;init;}=30;
    public double Gravity {get;init;}
    public double SizeRandom {get;init;}=.3;
    public double SpeedRandom {get;init;}=.2;
    public string Image {get;init;}="";
    public string ImageName {get;init;}="";
    public virtual ParticleLayerOptions Validate() {
        EffectRange.Check(Style,0,ParticleArtwork.LastStyle);EffectRange.Check(Count,1,2000);EffectRange.Check(Size,1,256);EffectRange.Check(Speed,0,600);EffectRange.Check(Direction,0,360);
        if(RandomStyles is null||RandomStyles.Length>ParticleArtwork.LastStyle||RandomStyles.Distinct().Count()!=RandomStyles.Length||RandomStyles.Any(s=>s<0||s>ParticleArtwork.LastStyle||s==5))throw new ArgumentException("随机粒子外观无效");
        if(RandomAppearance&&(RandomStyles.Length==0||(RandomStyles.Contains(7)&&string.IsNullOrEmpty(Image))))throw new ArgumentException("请选择有效的随机粒子外观；自定义图片须先上传");
        EffectRange.Check(Opacity,0,1);EffectColors.Validate(Color);EffectRange.Check(Sway,0,1);EffectRange.Check(Rotation,-180,180);EffectRange.Check(Twinkle,0,1);EffectRange.Check(Quality,0,2);
        EffectRange.Check(CenterX,0,1);EffectRange.Check(CenterY,0,1);EffectRange.Check(Width,.05,1);EffectRange.Check(Height,.05,1);
        EffectRange.Check(Emitter,0,2);EffectRange.Check(Emission,0,1);EffectRange.Check(Lifetime,.2,60);EffectRange.Check(BurstInterval,0,60);
        EffectRange.Check(Spread,0,360);EffectRange.Check(Gravity,-300,300);EffectRange.Check(SizeRandom,0,1);EffectRange.Check(SpeedRandom,0,1);
        if(Image is null||Image.Length>1_400_000||ImageName is null||ImageName.Length>128)throw new ArgumentException("粒子图片无效");
        if(Image.Length>0)ParticlePixels.Validate(Image);return this;
    }
}

public sealed record ParticleOptions : ParticleLayerOptions
{
    public ParticleLayerOptions SubLayer {get;init;}=new() {Count=60};
    public override ParticleOptions Validate() {
        base.Validate();
        if(SubLayer is null)throw new ArgumentException("缺少子粒子设置");
        SubLayer.Validate();
        if(SubLayer.Style==5)throw new ArgumentException("子粒子层不支持雨滴涟漪");
        return this;
    }
}

public sealed partial record VideoEffects
{
    public ParticleOptions Particles {get;init;}=new();
    private string ParticlesWire()=>ParticleLayerWire(Particles)+ParticleLayerWire(Particles.SubLayer with {Enabled=Particles.Enabled&&Particles.SubLayer.Enabled});
    private static string ParticleLayerWire(ParticleLayerOptions p)
    {
        if ((p.Style >= ParticleArtwork.FirstStyle || p.RandomStyles.Any(id => id >= ParticleArtwork.FirstStyle)) && !ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork"))
        {
            var candidates = p.RandomStyles.Where(id => id < ParticleArtwork.FirstStyle).ToArray();
            p = p with {Style = p.Style >= ParticleArtwork.FirstStyle ? 0 : p.Style, RandomStyles = candidates.Length == 0 ? [0] : candidates};
        }
        return FormattableString.Invariant($" {(p.Enabled&&!p.IsRain?1:0)} {p.Style} {p.Count} {p.Size:F6} {p.Speed:F6} {p.Direction:F6} {p.Opacity:F6} {EffectColors.Wire(p.ColorEnabled?p.Color:"#FFFFFF")} {p.Sway:F6} {p.Rotation:F6} {p.Twinkle:F6} {(p.Fade?1:0)} {p.Quality} {(p.LocalRegion?1:0)} {p.CenterX:F6} {p.CenterY:F6} {p.Width:F6} {p.Height:F6} {p.Emitter} {p.Emission} {p.Lifetime:F6} {p.BurstInterval:F6} {p.Spread:F6} {p.Gravity:F6} {p.SizeRandom:F6} {p.SpeedRandom:F6} {(p.Enabled&&p.UsesStyle(7)?ParticlePixels.Prepare(p.Image):"-")} {p.RandomMask} {(p.Enabled&&Enumerable.Range(ParticleArtwork.FirstStyle,ParticleArtwork.Count).Any(p.UsesStyle)?ParticleArtwork.Prepare():"-")} {(p.RandomRotation?1:0)}");
    }
}
