using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record GlitchLayer
{
    public bool Enabled {get;init;}=true;
    public int Mode {get;init;}
    public double Strength {get;init;}=.4;
    public double Rate {get;init;}=7;
    public double Coverage {get;init;}=.2;
    public double Size {get;init;}=24;
    public double RgbOffset {get;init;}=24;
    [JsonIgnore] public bool Active => Enabled&&Strength>0&&Coverage>0&&(Mode!=3||RgbOffset>0);
    public void Validate()
    {
        static void Range(double value,double min,double max) {if(!double.IsFinite(value)||value<min||value>max)throw new ArgumentException("故障集群参数超出范围");}
        if(Mode<0||Mode>3)throw new ArgumentException("不支持的故障类型");
        Range(Strength,0,1);Range(Rate,0,40);Range(Coverage,0,1);Range(Size,2,240);Range(RgbOffset,0,80);
    }
    public string ToWire() => FormattableString.Invariant($" {(Enabled?1:0)} {Mode} {Strength:F6} {Rate:F6} {Coverage:F6} {Size:F6} {RgbOffset:F6}");
}
