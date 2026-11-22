using System.Globalization;
namespace LumaWall.Contracts;
public sealed record TaskbarVisualOptions
{
    public int TransparentOpacity {get;init;}
    public int FrostedOpacity {get;init;}=40;
    public int Blur {get;init;}=20;
    public string Color {get;init;}="#181818";
    public bool ShowBorder {get;init;}
    public TaskbarVisualOptions Validate()
    {
        if(TransparentOpacity is <0 or >100||FrostedOpacity is <0 or >100||Blur is <0 or >100)throw new ArgumentException("任务栏外观参数超出范围");
        if(Color==null||Color.Length!=7||Color[0]!='#'||!uint.TryParse(Color.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _))throw new ArgumentException("任务栏颜色须为 #RRGGBB");
        return this;
    }
    public string ToCommand(TaskbarStyle style)
    {
        Validate();int opacity=style==TaskbarStyle.Transparent?TransparentOpacity:FrostedOpacity;
        uint rgb=uint.Parse(Color.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return FormattableString.Invariant($"{(int)style+1},{opacity},{Blur},{rgb},{(ShowBorder?1:0)}");
    }
}
