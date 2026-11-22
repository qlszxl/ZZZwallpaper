using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record ZoomTunnelOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=1;
    public double Period {get;init;}=4;
    public double Zoom {get;init;}=4;
    public double CenterX {get;init;}=0.5;
    public double CenterY {get;init;}=0.5;
    public ZoomTunnelOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(Period,1,20);
        EffectRange.Check(Zoom,1.1,8);
        EffectRange.Check(CenterX,0,1);
        EffectRange.Check(CenterY,0,1);
        return this;
    }
}

public sealed record RainRipplesOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=0.7;
    public double Density {get;init;}=5;
    public double Speed {get;init;}=0.6;
    public double Radius {get;init;}=0.8;
    public double Width {get;init;}=0.12;
    public bool Outline {get;init;}=true;
    public double OutlineStrength {get;init;}=.65;
    public double OutlineWidth {get;init;}=2;
    public string Color {get;init;}="#FFFFFF";
    public RainRipplesOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(Density,2,10);
        EffectRange.Check(Speed,0,3);
        EffectRange.Check(Radius,0.2,1);
        EffectRange.Check(Width,0.04,0.3);EffectRange.Check(OutlineStrength,0,1);EffectRange.Check(OutlineWidth,.5,10);EffectColors.Validate(Color);
        return this;
    }
}

public sealed record WaterWaveOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=0.7;
    public double Amplitude {get;init;}=12;
    public double Frequency {get;init;}=5;
    public double Speed {get;init;}=0.3;
    public double Angle {get;init;}=25;
    public WaterWaveOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(Amplitude,0,40);
        EffectRange.Check(Frequency,1,30);
        EffectRange.Check(Speed,0,3);
        EffectRange.Check(Angle,-180,180);
        return this;
    }
}

public sealed record EdgeOutlineOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=1;
    public double Width {get;init;}=1.2;
    public double Threshold {get;init;}=0.15;
    public int Mode {get;init;}=0;
    // Null preserves the white/black choice in configurations saved before v21.
    public string? Color {get;init;}
    [JsonIgnore] public string ResolvedColor => Color??(Mode==1?"#000000":"#FFFFFF");
    public EdgeOutlineOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(Width,0.5,4);
        EffectRange.Check(Threshold,0,1);
        EffectRange.Check(Mode,0,2);
        EffectColors.Validate(ResolvedColor);
        return this;
    }
}

public sealed record PixelateOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=1;
    public double BlockSize {get;init;}=16;
    public PixelateOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(BlockSize,2,128);
        return this;
    }
}

public sealed record SunPrismOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=0.45;
    public double Angle {get;init;}=-35;
    public double Width {get;init;}=0.3;
    public double Position {get;init;}=0.2;
    public double Speed {get;init;}=0.03;
    public int Shape {get;init;}
    public double Curvature {get;init;}
    public double Arc {get;init;}=180;
    public double Radius {get;init;}=.4;
    public double Repeat {get;init;}=1;
    public string[] Colors {get;init;}=["#FF4040","#FFAA33","#FFFF55","#55FF88","#44BBFF","#6655FF","#DD66FF"];
    // Empty positions preserve the evenly spaced palette of older configurations.
    public double[] ColorPositions {get;init;}=[];
    public double CenterX {get;init;}=.5;
    public double CenterY {get;init;}=.5;
    public double MoveX {get;init;}
    public double MoveY {get;init;}
    public double MoveSpeedX {get;init;}=.05;
    public double MoveSpeedY {get;init;}=.05;
    public double MovePhase {get;init;}=90;
    public double RotationSpeed {get;init;}
    public double ConeAngle {get;init;}=45;
    public int ConeCorner {get;init;}
    public double ConeDirection {get;init;}=45;
    public double ConeLength {get;init;}=1;
    public double ConeSwing {get;init;}=8;
    public double[] GetColorPositions()=>ColorPositions is {Length:>0}?ColorPositions.ToArray():Enumerable.Range(0,Colors.Length).Select(i=>(double)i/(Colors.Length-1)).ToArray();
    public SunPrismOptions Validate()
    {
        EffectRange.Check(Strength,0,1);
        EffectRange.Check(Angle,-180,180);
        EffectRange.Check(Width,0.05,1);
        EffectRange.Check(Position,-1,1);
        EffectRange.Check(Speed,0,1);EffectRange.Check(Shape,0,5);EffectRange.Check(Curvature,-2,2);EffectRange.Check(Arc,1,360);EffectRange.Check(Radius,.05,1.5);EffectRange.Check(Repeat,1,12);
        if(Colors is null||Colors.Length<2||Colors.Length>12)throw new ArgumentException("阳光色散须有 2–12 个颜色");foreach(var color in Colors)EffectColors.Validate(color);
        EffectRange.Check(CenterX,0,1);EffectRange.Check(CenterY,0,1);EffectRange.Check(MoveX,0,1);EffectRange.Check(MoveY,0,1);
        EffectRange.Check(MoveSpeedX,0,2);EffectRange.Check(MoveSpeedY,0,2);EffectRange.Check(MovePhase,-180,180);EffectRange.Check(RotationSpeed,-180,180);EffectRange.Check(ConeAngle,1,170);EffectRange.Check(ConeCorner,0,3);EffectRange.Check(ConeDirection,0,90);EffectRange.Check(ConeLength,.1,2);EffectRange.Check(ConeSwing,0,45);
        if(ColorPositions is null||(ColorPositions.Length!=0&&ColorPositions.Length!=Colors.Length))throw new ArgumentException("渐变关键点数量必须与颜色一致");
        double previous=-1;foreach(double position in ColorPositions) {EffectRange.Check(position,0,1);if(position<=previous)throw new ArgumentException("渐变关键点位置必须按顺序递增");previous=position;}

        return this;
    }
}

internal static class EffectRange
{
    public static void Check(double value,double min,double max) {if(!double.IsFinite(value)||value<min||value>max)throw new ArgumentException("效果参数超出范围");}
}

public sealed partial record VideoEffects
{
    public ZoomTunnelOptions ZoomTunnel {get;init;}=new();
    public RainRipplesOptions RainRipples {get;init;}=new();
    public WaterWaveOptions WaterWave {get;init;}=new();
    public EdgeOutlineOptions EdgeOutline {get;init;}=new();
    public PixelateOptions Pixelate {get;init;}=new();
    public SunPrismOptions SunPrism {get;init;}=new();
    [JsonIgnore] private bool ExtendedActive => (Particles.Enabled&&((!Particles.IsRain&&Particles.Opacity>0)||(Particles.SubLayer.Enabled&&Particles.SubLayer.Opacity>0)))|| (ZoomTunnel.Enabled&&ZoomTunnel.Strength>0)||(RainRipples.Enabled&&(RainRipples.Strength>0||(RainRipples.Outline&&RainRipples.OutlineStrength>0)))||(WaterWave.Enabled&&WaterWave.Strength>0&&WaterWave.Amplitude>0)||(EdgeOutline.Enabled&&EdgeOutline.Strength>0)||(Pixelate.Enabled&&Pixelate.Strength>0)||(SunPrism.Enabled&&SunPrism.Strength>0)||Transform.Active||(LightFlare.Enabled&&LightFlare.Strength>0&&LightFlare.Brightness>0);
    private void ValidateExtended()
    {
        if(ZoomTunnel is null||RainRipples is null||WaterWave is null||EdgeOutline is null||Pixelate is null||SunPrism is null||LightFlare is null||Transform is null)throw new ArgumentException("缺少扩展效果参数");
        ZoomTunnel.Validate();
        RainRipples.Validate();
        WaterWave.Validate();
        EdgeOutline.Validate();
        Pixelate.Validate();
        SunPrism.Validate();
        LightFlare.Validate();Transform.Validate();
    }
    private string ExtendedWire() => FormattableString.Invariant($" {(ZoomTunnel.Enabled?1:0)} {ZoomTunnel.Strength:F6} {ZoomTunnel.Period:F6} {ZoomTunnel.Zoom:F6} {ZoomTunnel.CenterX:F6} {ZoomTunnel.CenterY:F6} {(RainRipples.Enabled?1:0)} {RainRipples.Strength:F6} {RainRipples.Density:F6} {RainRipples.Speed:F6} {RainRipples.Radius:F6} {RainRipples.Width:F6} {(WaterWave.Enabled?1:0)} {WaterWave.Strength:F6} {WaterWave.Amplitude:F6} {WaterWave.Frequency:F6} {WaterWave.Speed:F6} {WaterWave.Angle:F6} {(EdgeOutline.Enabled?1:0)} {EdgeOutline.Strength:F6} {EdgeOutline.Width:F6} {EdgeOutline.Threshold:F6} {EdgeOutline.Mode} {(Pixelate.Enabled?1:0)} {Pixelate.Strength:F6} {Pixelate.BlockSize:F6} {(SunPrism.Enabled?1:0)} {SunPrism.Strength:F6} {SunPrism.Angle:F6} {SunPrism.Width:F6} {SunPrism.Position:F6} {SunPrism.Speed:F6}");
}
