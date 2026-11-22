namespace LumaWall.Contracts;

// Defaults reproduce the approved 0.1.10.1 artwork. Layout and timer state are independent.
public sealed record TimeWidgetAppearance
{
    public int DigitFont {get;init;}=0;
    public double DigitSlant {get;init;}=12.407418527400743;
    public double DigitSpacing {get;init;}=1;
    public double DigitWidth {get;init;}=1;
    public bool NumberGradient {get;init;}=true;
    public bool NumberShadow {get;init;}=true;
    public double ShadowOpacity {get;init;}=0.7058823529411765;
    public double ShadowDistance {get;init;}=4;
    public double MainOffsetX {get;init;}=0;
    public double MainOffsetY {get;init;}=0;
    public double TextSlant {get;init;}=12.407418527400743;
    public double TitleScale {get;init;}=1;
    public double SecondaryScale {get;init;}=1;
    public double TitleOffsetY {get;init;}=0;
    public double DetailsOffsetY {get;init;}=0;
    public bool FollowAccent {get;init;}=true;
    public string AccentColor {get;init;}="#F4E600";
    public string SecondaryColor {get;init;}="#B0B0B0";
    public string DecorationColor {get;init;}="#696969";
    public bool ShowBrand {get;init;}=true;
    public bool ShowFrame {get;init;}=true;
    public double GrooveDepth {get;init;}=1;
    public double PanelRadius {get;init;}=1;
    public bool ShowTexture {get;init;}=true;
    public double TextureOpacity {get;init;}=1;
    public double TextureSpacing {get;init;}=7;
    public bool ShowAccentLine {get;init;}=true;
    public bool ShowDividers {get;init;}=true;
    public double DividerOpacity {get;init;}=1;
    public bool ShowScrews {get;init;}=true;
    public double DialScale {get;init;}=1;
    public int TickMode {get;init;}=0;
    public int DialNumbers {get;init;}=0;
    public double HandWidth {get;init;}=1;
    public double HourHandLength {get;init;}=1;
    public double MinuteHandLength {get;init;}=1;
    public string SecondHandColor {get;init;}="#696969";
    public double SecondsScale {get;init;}=1;
    public bool ShowDigitalTime {get;init;}=true;
    public bool ShowCountdownTitle {get;init;}=true;
    public bool ShowDayUnit {get;init;}=true;
    public bool PadDays {get;init;}=true;
    public bool ShowRemainingTime {get;init;}=true;
    public bool ShowTargetDate {get;init;}=true;
    public bool ShowProgress {get;init;}=true;
    public double ProgressThickness {get;init;}=1;
    public string ProgressTrackColor {get;init;}="#181818";
    public bool ShowPercent {get;init;}=true;
    public int RingSegments {get;init;}=64;
    public double RingThickness {get;init;}=1;
    public double RingStartAngle {get;init;}=0;
    public bool RingClockwise {get;init;}=true;
    public string RingTrackColor {get;init;}="#343434";
    public bool ShowRingEndpoint {get;init;}=true;
    public bool ShowStatus {get;init;}=true;
    public bool ShowRound {get;init;}=true;
    public bool ShowButtons {get;init;}=true;
    public bool ShowResetButton {get;init;}=true;
    public bool ShowSkipButton {get;init;}=true;
    public double ButtonScale {get;init;}=1;
    public double ButtonSpacing {get;init;}=1;
    public double ButtonIconScale {get;init;}=1;
    public double ButtonOffsetY {get;init;}=0;
    public void Validate() {
        if(!double.IsFinite(DigitSlant)||DigitSlant<-15||DigitSlant>25)throw new ArgumentException("数字倾斜超出范围");
        if(!double.IsFinite(DigitSpacing)||DigitSpacing<0||DigitSpacing>12)throw new ArgumentException("数字字距超出范围");
        if(!double.IsFinite(DigitWidth)||DigitWidth<0.6||DigitWidth>1.25)throw new ArgumentException("数字横向比例超出范围");
        if(!double.IsFinite(ShadowOpacity)||ShadowOpacity<0||ShadowOpacity>1)throw new ArgumentException("阴影不透明度超出范围");
        if(!double.IsFinite(ShadowDistance)||ShadowDistance<0||ShadowDistance>12)throw new ArgumentException("阴影距离超出范围");
        if(!double.IsFinite(MainOffsetX)||MainOffsetX<-60||MainOffsetX>60)throw new ArgumentException("主数字水平偏移超出范围");
        if(!double.IsFinite(MainOffsetY)||MainOffsetY<-60||MainOffsetY>60)throw new ArgumentException("主数字垂直偏移超出范围");
        if(!double.IsFinite(TextSlant)||TextSlant<-15||TextSlant>25)throw new ArgumentException("文字倾斜超出范围");
        if(!double.IsFinite(TitleScale)||TitleScale<0.5||TitleScale>1.5)throw new ArgumentException("标题大小超出范围");
        if(!double.IsFinite(SecondaryScale)||SecondaryScale<0.5||SecondaryScale>1.5)throw new ArgumentException("辅助文字大小超出范围");
        if(!double.IsFinite(TitleOffsetY)||TitleOffsetY<-40||TitleOffsetY>40)throw new ArgumentException("标题垂直偏移超出范围");
        if(!double.IsFinite(DetailsOffsetY)||DetailsOffsetY<-40||DetailsOffsetY>40)throw new ArgumentException("辅助文字垂直偏移超出范围");
        if(!double.IsFinite(GrooveDepth)||GrooveDepth<0||GrooveDepth>2)throw new ArgumentException("凹槽深度超出范围");
        if(!double.IsFinite(PanelRadius)||PanelRadius<0||PanelRadius>2)throw new ArgumentException("内框圆角比例超出范围");
        if(!double.IsFinite(TextureOpacity)||TextureOpacity<0||TextureOpacity>1)throw new ArgumentException("斜纹不透明度超出范围");
        if(!double.IsFinite(TextureSpacing)||TextureSpacing<4||TextureSpacing>24)throw new ArgumentException("斜纹间距超出范围");
        if(!double.IsFinite(DividerOpacity)||DividerOpacity<0||DividerOpacity>1)throw new ArgumentException("分隔线不透明度超出范围");
        if(!double.IsFinite(DialScale)||DialScale<0.65||DialScale>1.05)throw new ArgumentException("圆盘大小超出范围");
        if(!double.IsFinite(HandWidth)||HandWidth<0.5||HandWidth>2)throw new ArgumentException("指针粗细超出范围");
        if(!double.IsFinite(HourHandLength)||HourHandLength<0.5||HourHandLength>1.2)throw new ArgumentException("时针长度超出范围");
        if(!double.IsFinite(MinuteHandLength)||MinuteHandLength<0.5||MinuteHandLength>1.1)throw new ArgumentException("分针长度超出范围");
        if(!double.IsFinite(SecondsScale)||SecondsScale<0.5||SecondsScale>1.5)throw new ArgumentException("小号秒数大小超出范围");
        if(!double.IsFinite(ProgressThickness)||ProgressThickness<0.5||ProgressThickness>2)throw new ArgumentException("进度条粗细超出范围");
        if(!double.IsFinite(RingThickness)||RingThickness<0.5||RingThickness>1.5)throw new ArgumentException("圆环粗细超出范围");
        if(!double.IsFinite(RingStartAngle)||RingStartAngle<0||RingStartAngle>360)throw new ArgumentException("圆环起始角度超出范围");
        if(!double.IsFinite(ButtonScale)||ButtonScale<0.6||ButtonScale>1.2)throw new ArgumentException("按钮大小超出范围");
        if(!double.IsFinite(ButtonSpacing)||ButtonSpacing<0.65||ButtonSpacing>1.1)throw new ArgumentException("按钮间距超出范围");
        if(!double.IsFinite(ButtonIconScale)||ButtonIconScale<0.6||ButtonIconScale>1.5)throw new ArgumentException("按钮图标大小超出范围");
        if(!double.IsFinite(ButtonOffsetY)||ButtonOffsetY<-20||ButtonOffsetY>20)throw new ArgumentException("按钮垂直偏移超出范围");
        if(DigitFont is <0 or >1)throw new ArgumentException("数字字体超出范围");
        if(TickMode is <0 or >2)throw new ArgumentException("刻度超出范围");
        if(DialNumbers is <0 or >2)throw new ArgumentException("表盘数字超出范围");
        if(RingSegments is <12 or >120)throw new ArgumentException("圆环分段数超出范围");
        if(!VisualWidgetOptions.ValidColor(AccentColor))throw new ArgumentException("强调色无效");
        if(!VisualWidgetOptions.ValidColor(SecondaryColor))throw new ArgumentException("辅助文字颜色无效");
        if(!VisualWidgetOptions.ValidColor(DecorationColor))throw new ArgumentException("装饰文字颜色无效");
        if(!VisualWidgetOptions.ValidColor(SecondHandColor))throw new ArgumentException("秒针颜色无效");
        if(!VisualWidgetOptions.ValidColor(ProgressTrackColor))throw new ArgumentException("进度条底色无效");
        if(!VisualWidgetOptions.ValidColor(RingTrackColor))throw new ArgumentException("圆环底色无效");
    }
    public TimeWidgetAppearance ResetGroup(int group) {
        var d=new TimeWidgetAppearance();return group switch {
            0=>this with {DigitFont=d.DigitFont,DigitSlant=d.DigitSlant,DigitSpacing=d.DigitSpacing,DigitWidth=d.DigitWidth,NumberGradient=d.NumberGradient,NumberShadow=d.NumberShadow,ShadowOpacity=d.ShadowOpacity,ShadowDistance=d.ShadowDistance,MainOffsetX=d.MainOffsetX,MainOffsetY=d.MainOffsetY},
            1=>this with {TextSlant=d.TextSlant,TitleScale=d.TitleScale,SecondaryScale=d.SecondaryScale,TitleOffsetY=d.TitleOffsetY,DetailsOffsetY=d.DetailsOffsetY},
            2=>this with {FollowAccent=d.FollowAccent,AccentColor=d.AccentColor,SecondaryColor=d.SecondaryColor,DecorationColor=d.DecorationColor},
            3=>this with {ShowBrand=d.ShowBrand,ShowFrame=d.ShowFrame,GrooveDepth=d.GrooveDepth,PanelRadius=d.PanelRadius,ShowTexture=d.ShowTexture,TextureOpacity=d.TextureOpacity,TextureSpacing=d.TextureSpacing,ShowAccentLine=d.ShowAccentLine,ShowDividers=d.ShowDividers,DividerOpacity=d.DividerOpacity,ShowScrews=d.ShowScrews},
            4=>this with {DialScale=d.DialScale,TickMode=d.TickMode,DialNumbers=d.DialNumbers,HandWidth=d.HandWidth,HourHandLength=d.HourHandLength,MinuteHandLength=d.MinuteHandLength,SecondHandColor=d.SecondHandColor,SecondsScale=d.SecondsScale,ShowDigitalTime=d.ShowDigitalTime},
            5=>this with {ShowCountdownTitle=d.ShowCountdownTitle,ShowDayUnit=d.ShowDayUnit,PadDays=d.PadDays,ShowRemainingTime=d.ShowRemainingTime,ShowTargetDate=d.ShowTargetDate},
            6=>this with {ShowProgress=d.ShowProgress,ProgressThickness=d.ProgressThickness,ProgressTrackColor=d.ProgressTrackColor,ShowPercent=d.ShowPercent,RingSegments=d.RingSegments,RingThickness=d.RingThickness,RingStartAngle=d.RingStartAngle,RingClockwise=d.RingClockwise,RingTrackColor=d.RingTrackColor,ShowRingEndpoint=d.ShowRingEndpoint},
            7=>this with {ShowStatus=d.ShowStatus,ShowRound=d.ShowRound,ShowButtons=d.ShowButtons,ShowResetButton=d.ShowResetButton,ShowSkipButton=d.ShowSkipButton,ButtonScale=d.ButtonScale,ButtonSpacing=d.ButtonSpacing,ButtonIconScale=d.ButtonIconScale,ButtonOffsetY=d.ButtonOffsetY},
            _=>throw new ArgumentOutOfRangeException(nameof(group))
        };
    }
}

