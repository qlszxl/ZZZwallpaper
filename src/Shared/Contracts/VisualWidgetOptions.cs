namespace LumaWall.Contracts;

public sealed record VisualWidgetOptions
{
    public int Width {get;init;}=320;
    public int Height {get;init;}=120;
    public string Text {get;init;}="新文本";
    public double FontSize {get;init;}=28;
    public bool Bold {get;init;}=true;
    public int Alignment {get;init;}=1;
    public string TextColor {get;init;}="#FFFFFF";
    public string FillColor {get;init;}="#101010";
    public bool Background {get;init;}
    public double FillOpacity {get;init;}=.7;
    public double Radius {get;init;}=20;
    public double Padding {get;init;}=12;
    public double BorderWidth {get;init;}
    public string BorderColor {get;init;}="#D4F500";
    public string? Image {get;init;}
    public int StickerShape {get;init;}
    public int ImageFit {get;init;}
    public bool FlipHorizontal {get;init;}
    public int MaskShape {get;init;}
    public int Gradient {get;init;}
    public bool ClickThrough {get;init;}
    public int TimeStyle {get;init;}
    public TimeWidgetAppearance TimeAppearance {get;init;}=new();
    public static bool ValidColor(string? color)=>color is {Length:7}&&color[0]=='#'&&color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF")<0;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string,object> validatedImages=new();
    public void Validate()
    {
        if(TimeAppearance==null)throw new ArgumentException("缺少时间组件细节设置");TimeAppearance.Validate();
        if(Width is <24 or >16384||Height is <24 or >16384||Text==null||Text.Length>2000||Alignment is <0 or >2||StickerShape is <0 or >3||ImageFit is <0 or >2||MaskShape is <0 or >1||Gradient is <0 or >4||TimeStyle is <0 or >2)
            throw new ArgumentException("文本、贴纸或遮罩设置超出范围");
        if(!new[]{FontSize,FillOpacity,Radius,Padding,BorderWidth}.All(double.IsFinite)||FontSize is <8 or >240||FillOpacity is <0 or >1||Radius is <0 or >300||Padding is <0 or >100||BorderWidth is <0 or >20||!ValidColor(TextColor)||!ValidColor(FillColor)||!ValidColor(BorderColor))
            throw new ArgumentException("组件颜色或外观设置无效");
        if(Image!=null&&!validatedImages.TryGetValue(Image,out _)) {
            if(Image.Length>2_800_000)throw new ArgumentException("贴纸图片太大，请使用更小的图片");
            var bytes=Convert.FromBase64String(Image);
            if(bytes.Length<24||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new ArgumentException("贴纸须为 PNG");
            int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            if(w is <1 or >2048||h is <1 or >2048)throw new ArgumentException("贴纸尺寸太大");
            validatedImages.GetValue(Image,_=>new object());
        }
    }
}
