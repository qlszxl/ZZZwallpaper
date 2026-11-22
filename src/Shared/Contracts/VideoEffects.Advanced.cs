using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record RippleColorOptions
{
    public int Mode {get;init;}
    public string Color {get;init;}="#FFFFFF";
    public double HueMin {get;init;}
    public double HueMax {get;init;}=360;
    public double SaturationMin {get;init;}=.7;
    public double SaturationMax {get;init;}=1;
    public double ValueMin {get;init;}=.7;
    public double ValueMax {get;init;}=1;
    public double Rate {get;init;}=1;
    public RippleColorOptions Validate()
    {
        EffectRange.Check(Mode,0,3);EffectColors.Validate(Color);EffectRange.Check(HueMin,0,360);EffectRange.Check(HueMax,0,360);
        EffectRange.Check(SaturationMin,0,1);EffectRange.Check(SaturationMax,SaturationMin,1);EffectRange.Check(ValueMin,0,1);EffectRange.Check(ValueMax,ValueMin,1);EffectRange.Check(Rate,0,10);return this;
    }
}
public static class EffectColors
{
    public static void Validate(string? color) {if(color is null||color.Length!=7||color[0]!='#'||!color.AsSpan(1).ToString().All(Uri.IsHexDigit))throw new ArgumentException("颜色须为 #RRGGBB");}
    public static string Wire(string color) {Validate(color);return Convert.ToInt32(color[1..],16).ToString(System.Globalization.CultureInfo.InvariantCulture);}
}
public sealed partial record VideoEffects
{
    public bool ColoredNoise {get;init;}
    public RippleColorOptions RippleColor {get;init;}=new();
    public int BlurMethod {get;init;}
    public int BlurSamples {get;init;}=65;
    public double BlurDirection {get;init;}
    public double BlurCenterX {get;init;}=.5;
    public double BlurCenterY {get;init;}=.5;
    private void ValidateAdvanced()
    {
        if(RippleColor is null)throw new ArgumentException("缺少点阵颜色参数");RippleColor.Validate();
        EffectRange.Check(BlurSamples,5,257);
        EffectRange.Check(BlurMethod,0,4);EffectRange.Check(BlurDirection,-180,180);EffectRange.Check(BlurCenterX,0,1);EffectRange.Check(BlurCenterY,0,1);ValidateMasks();
    }
    private string AdvancedWire()=>FormattableString.Invariant($" {(ColoredNoise?1:0)} {RippleColor.Mode} {EffectColors.Wire(RippleColor.Color)} {RippleColor.HueMin:F6} {RippleColor.HueMax:F6} {RippleColor.SaturationMin:F6} {RippleColor.SaturationMax:F6} {RippleColor.ValueMin:F6} {RippleColor.ValueMax:F6} {RippleColor.Rate:F6} {BlurMethod} {BlurDirection:F6} {BlurCenterX:F6} {BlurCenterY:F6} {(RainRipples.Outline?1:0)} {RainRipples.OutlineStrength:F6} {RainRipples.OutlineWidth:F6} {EffectColors.Wire(RainRipples.Color)} {SunPrism.Shape} {SunPrism.Curvature:F6} {SunPrism.Arc:F6} {SunPrism.Radius:F6} {SunPrism.Repeat:F6} {SunPrism.Colors.Length}")+string.Concat(SunPrism.Colors.Select(c=>" "+EffectColors.Wire(c)))+MasksWire();
    private string SunAnimationWire()=>FormattableString.Invariant($" {SunPrism.CenterX:F6} {SunPrism.CenterY:F6} {SunPrism.MoveX:F6} {SunPrism.MoveY:F6} {SunPrism.MoveSpeedX:F6} {SunPrism.MoveSpeedY:F6} {SunPrism.MovePhase:F6} {SunPrism.RotationSpeed:F6} {SunPrism.ConeAngle:F6}")+string.Concat(SunPrism.GetColorPositions().Select(p=>" "+p.ToString("G17",System.Globalization.CultureInfo.InvariantCulture)));
    private string SunConeWire()=>FormattableString.Invariant($" {SunPrism.ConeCorner} {SunPrism.ConeDirection:F6} {SunPrism.ConeLength:F6} {SunPrism.ConeSwing:F6}");
}
