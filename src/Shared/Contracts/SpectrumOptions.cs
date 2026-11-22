namespace LumaWall.Contracts;

public enum SpectrumShape { Bars, Wave, FilledWave, Circle, RadialBars, Dots, SegmentedBars }

public sealed record SpectrumOptions
{
    public SpectrumShape Shape {get;init;}
    public bool CenterEmphasis {get;init;}
    public SpectrumWeighting Weighting {get;init;}=new();
    public double StrokeWidth {get;init;}=2;
    public double RingRadius {get;init;}=.45;
    public bool Enabled {get;init;}=true;
    public double Opacity {get;init;}=1;
    public double Scale {get;init;}=1;
    public double OffsetX {get;init;}
    public double OffsetY {get;init;}
    public double Rotation {get;init;}
    public SpectrumOptions[] SubSpectra {get;init;}=[];
    public int Width {get;init;}=360;
    public int Height {get;init;}=100;
    public int Bands {get;init;}=32;
    public double Sensitivity {get;init;}=1.5;
    public int SmoothingMs {get;init;}=180;
    public bool Mirror {get;init;}
    public bool Background {get;init;}=true;
    public string Color {get;init;}="#D4F500";
    public string ColorEnd {get;init;}="#F5FF80";
    public bool Gradient {get;init;}
    public bool Glow {get;init;}
    public double GlowIntensity {get;init;}=.65;
    public double GlowRadius {get;init;}=18;
    public bool GlowFollowColor {get;init;}=true;
    public string GlowColor {get;init;}="#D4F500";
    public double Gap {get;init;}=.28;
    public double Radius {get;init;}=3;
    public double SegmentHeight {get;init;}=6;
    public double SegmentGap {get;init;}=3;
    public double MinimumHeight {get;init;}
    public double Padding {get;init;}=12;
    public int Direction {get;init;}
    public bool Reverse {get;init;}
    public bool Peaks {get;init;}
    public void Validate()=>Validate(false);
    private void Validate(bool child)
    {
        if(Weighting==null)throw new ArgumentException("缺少频谱加权设置");Weighting.Validate();
        if(!child&&Weighting.SyncWithMain)throw new ArgumentException("主频谱不能跟随自身的加权曲线");
        if(!Enum.IsDefined(Shape)||!new[]{StrokeWidth,RingRadius,Opacity,Scale,OffsetX,OffsetY,Rotation}.All(double.IsFinite)||StrokeWidth is <.5 or >20||RingRadius is <.05 or >.85||Opacity is <0 or >1||Scale is <.1 or >2||OffsetX is <-1 or >1||OffsetY is <-1 or >1||Rotation is <0 or >360||SubSpectra==null||SubSpectra.Length>(child?0:8))throw new ArgumentException("频谱图层设置无效");
        foreach(var layer in SubSpectra) {if(layer==null)throw new ArgumentException("子频谱不能为空");layer.Validate(true);}
        if(!double.IsFinite(GlowIntensity)||GlowIntensity is <0 or >1||!double.IsFinite(GlowRadius)||GlowRadius is <0 or >80||!VisualWidgetOptions.ValidColor(GlowColor))throw new ArgumentException("频谱辉光设置无效");
        if(!double.IsFinite(SegmentHeight)||SegmentHeight is <1 or >40||!double.IsFinite(SegmentGap)||SegmentGap is <0 or >20)throw new ArgumentException("频谱格子高度或间距超出范围");
        if(Width is <24 or >16384||Height is <24 or >16384||Bands is <1 or >256||!double.IsFinite(Sensitivity)||Sensitivity is <.25 or >4||SmoothingMs is <50 or >800)
            throw new ArgumentException("音频频谱设置超出范围");
        if(!new[]{Gap,Radius,MinimumHeight,Padding}.All(double.IsFinite)||Gap is <0 or >.9||Radius is <0 or >40||MinimumHeight is <0 or >20||Padding is <0 or >100||Direction is <0 or >3||!VisualWidgetOptions.ValidColor(Color)||!VisualWidgetOptions.ValidColor(ColorEnd))throw new ArgumentException("频谱外观设置无效");
    }
}
