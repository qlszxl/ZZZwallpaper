namespace LumaWall.Contracts;

public sealed record LightFlareOptions
{
    public bool Enabled {get;init;}
    public double Strength {get;init;}=.7;
    public double Brightness {get;init;}=1;
    public double CenterX {get;init;}=.5;
    public double CenterY {get;init;}=.5;
    // Diameter, relative to the short side of the rendered image.
    public double Size {get;init;}=.2;
    public string Color {get;init;}="#FFFFFF";
    public string HaloColor {get;init;}="#80BFFF";
    public bool LinkColors {get;init;}
    public double CoreSize {get;init;}=.08;
    public double HaloStrength {get;init;}=.65;
    public double HaloRadius {get;init;}=.6;
    public double Softness {get;init;}=.7;
    // 0 screen, 1 add, 2 normal, 3 overlay, 4 soft light.
    public int BlendMode {get;init;}
    public int RayCount {get;init;}=8;
    public double RayLength {get;init;}=1;
    public double RayWidth {get;init;}=.025;
    public double Rotation {get;init;}
    public double Alternate {get;init;}=.5;
    public bool GhostsEnabled {get;init;}
    public int GhostCount {get;init;}=3;
    public double GhostSpacing {get;init;}=.65;
    public double GhostSize {get;init;}=.16;
    public double GhostStrength {get;init;}=.25;
    public double Dispersion {get;init;}=.6;
    public bool Animation {get;init;}
    public double PulseAmount {get;init;}=.1;
    public double PulsePeriod {get;init;}=4;
    public double RotationSpeed {get;init;}=3;

    public LightFlareOptions Validate()
    {
        EffectRange.Check(Strength,0,1);EffectRange.Check(Brightness,0,2);
        EffectRange.Check(CenterX,0,1);EffectRange.Check(CenterY,0,1);EffectRange.Check(Size,.01,.8);
        EffectColors.Validate(Color);EffectColors.Validate(HaloColor);
        EffectRange.Check(CoreSize,.01,.3);EffectRange.Check(HaloStrength,0,1);
        EffectRange.Check(HaloRadius,.1,1.5);EffectRange.Check(Softness,0,1);EffectRange.Check(BlendMode,0,4);
        if(RayCount is not (0 or 4 or 6 or 8 or 12))throw new ArgumentException("光刺数量须为 0、4、6、8 或 12");
        EffectRange.Check(RayLength,.1,2);EffectRange.Check(RayWidth,.005,.2);
        EffectRange.Check(Rotation,-180,180);EffectRange.Check(Alternate,0,1);
        EffectRange.Check(GhostCount,1,4);EffectRange.Check(GhostSpacing,.1,1.5);EffectRange.Check(GhostSize,.03,.5);
        EffectRange.Check(GhostStrength,0,1);EffectRange.Check(Dispersion,0,1);
        EffectRange.Check(PulseAmount,0,1);EffectRange.Check(PulsePeriod,1,20);EffectRange.Check(RotationSpeed,-30,30);
        return this;
    }

    public static LightFlareOptions Preset(int index)=>index switch {
        0=>new(),
        1=>new() {Color="#FFF8CB",HaloColor="#D5E969",CoreSize=.12,HaloStrength=.8,HaloRadius=.55,
            RayCount=4,RayLength=1.5,RayWidth=.012,Rotation=40,Alternate=.85,
            GhostsEnabled=true,GhostCount=3,GhostSpacing=.85,GhostSize=.13,GhostStrength=.32,Dispersion=.85},
        2=>new() {Color="#FFF8EA",HaloColor="#FFD89B",CoreSize=.2,HaloStrength=.75,HaloRadius=1,RayCount=0},
        _=>throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public sealed partial record VideoEffects
{
    public LightFlareOptions LightFlare {get;init;}=new();
    private string LightFlareWire()
    {
        var f=LightFlare;
        return FormattableString.Invariant($" {(f.Enabled?1:0)} {f.Strength:F6} {f.Brightness:F6} {f.CenterX:F6} {f.CenterY:F6} {f.Size:F6} {EffectColors.Wire(f.Color)} {EffectColors.Wire(f.LinkColors?f.Color:f.HaloColor)} {f.CoreSize:F6} {f.HaloStrength:F6} {f.HaloRadius:F6} {f.Softness:F6} {f.BlendMode} {f.RayCount} {f.RayLength:F6} {f.RayWidth:F6} {f.Rotation:F6} {f.Alternate:F6} {(f.GhostsEnabled?1:0)} {f.GhostCount} {f.GhostSpacing:F6} {f.GhostSize:F6} {f.GhostStrength:F6} {f.Dispersion:F6} {(f.Animation?1:0)} {f.PulseAmount:F6} {f.PulsePeriod:F6} {f.RotationSpeed:F6}");
    }
}
