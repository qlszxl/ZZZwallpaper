using System.Globalization;
using System.IO;
using LumaWall.Contracts;
using W=System.Windows;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;

namespace LumaWall.WidgetHost;

// Shared production drawing used by the desktop and the offline design sheets.
// Each approved design has its own proportions; changing text never reflows the card.
internal sealed class TimeWidgetArtwork : W.FrameworkElement
{
    private readonly TimeWidgetTypography typography;
    private readonly I.BitmapImage? wordmark;
    private WidgetInstance widget=new();private DateTimeOffset now;
    private M.Brush accent=M.Brushes.Yellow,ink=M.Brushes.White;
    private M.Brush themeAccent=M.Brushes.Yellow;
    private TimeWidgetAppearance Detail=>widget.Visual.TimeAppearance;
    private int hot=-1,pressed=-1,focused=-1;
    private M.Brush Muted=>Brush(Detail.SecondaryColor);
    private M.Brush Dim=>Brush(Detail.DecorationColor);
    private static readonly Dictionary<string,M.SolidColorBrush> brushes=new();
    private static readonly M.Brush Edge=Brush("#343434");
    internal double DesignHeight=>widget.Visual.TimeStyle==2?740:widget.Visual.TimeStyle==1?430:500;
    internal bool Running=>widget.Focus.Advance(now).Running;
    internal string AccessibleText {get;private set;}="";
    internal TimeWidgetArtwork(string? assets=null) {
        assets??=Path.Combine(AppContext.BaseDirectory,"Assets");typography=new(assets);
        I.BitmapImage? Image(string name) {string path=Path.Combine(assets,"Branding",name);if(!File.Exists(path))return null;var image=new I.BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.CacheOption=I.BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return image;}
        wordmark=Image("wordmark.png");Width=500;SnapsToDevicePixels=true;UseLayoutRounding=true;
    }
    internal void Update(WidgetInstance value,DateTimeOffset time,M.Brush accentBrush) {
        bool changed=widget!=value||now.ToUnixTimeSeconds()!=time.ToUnixTimeSeconds()||!ReferenceEquals(themeAccent,accentBrush);
        widget=value;now=time;themeAccent=accentBrush;accent=Detail.FollowAccent?accentBrush:Brush(Detail.AccentColor);Height=DesignHeight;
        AccessibleText=value.Kind==WidgetKind.Clock?value.Clock.Time(time.LocalDateTime)+" "+value.Clock.Date(time.LocalDateTime):value.Kind==WidgetKind.Countdown?value.Countdown.Title+" "+value.Countdown.Value(time):TimerState.PhaseName+" "+FocusTime+" "+RoundText;
        if(!changed)return;ink=Brush(value.Visual.TextColor);InvalidateVisual();
    }
    internal void Interaction(int hover,int down,int focus) {if(hot==hover&&pressed==down&&focused==focus)return;hot=hover;pressed=down;focused=focus;InvalidateVisual();}
    private W.Rect OriginalActionBounds(int index)=>widget.Visual.TimeStyle switch {
        2=>new(47+index*158,546,90,90),
        1=>new(51+index*162,333,68,68),
        _=>new(40+index*146,414,128,60)
    };
    internal bool ActionVisible(int index)=>Detail.ShowButtons&&(index==0||index==1&&Detail.ShowResetButton||index==2&&Detail.ShowSkipButton);
    internal W.Rect ActionBounds(int index) {
        var visible=Enumerable.Range(0,3).Where(ActionVisible).ToArray();var original=OriginalActionBounds(index);var middle=OriginalActionBounds(1);
        double center=middle.X+middle.Width/2,step=(OriginalActionBounds(1).X-OriginalActionBounds(0).X)*Detail.ButtonSpacing;
        int slot=Array.IndexOf(visible,index);double x=center+(slot-(visible.Length-1)/2d)*step,y=original.Y+original.Height/2+Detail.ButtonOffsetY;
        var r=new W.Rect(x-original.Width*Detail.ButtonScale/2,y-original.Height*Detail.ButtonScale/2,original.Width*Detail.ButtonScale,original.Height*Detail.ButtonScale);
        return new(Math.Clamp(r.X,20,480-r.Width),Math.Clamp(r.Y,0,DesignHeight-r.Height),r.Width,r.Height);
    }
    private static M.SolidColorBrush Brush(string color) {if(brushes.TryGetValue(color,out var found))return found;var b=new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(color));b.Freeze();if(brushes.Count>256)brushes.Clear();brushes[color]=b;return b;}
    private static M.LinearGradientBrush Gradient(string top,string bottom) {var b=new M.LinearGradientBrush((M.Color)M.ColorConverter.ConvertFromString(top),(M.Color)M.ColorConverter.ConvertFromString(bottom),90);b.Freeze();return b;}
    private static void Round(M.DrawingContext dc,W.Rect r,double radius,M.Brush fill,M.Brush? border=null,double line=1)=>dc.DrawRoundedRectangle(fill,border==null?null:new M.Pen(border,line),r,radius,radius);
    private static W.Rect Inset(W.Rect r,double amount)=>new(r.X+amount,r.Y+amount,Math.Max(0,r.Width-amount*2),Math.Max(0,r.Height-amount*2));
    private static W.Rect ScaleRect(W.Rect r,double x,double y)=>new(r.X+r.Width*(1-x)/2,r.Y+r.Height*(1-y)/2,r.Width*x,r.Height*y);
    private void DecorLabel(M.DrawingContext dc,string text,W.Rect r,double height,M.Brush? brush=null,int align=1,bool bold=true)=>typography.Draw(dc,text,r,height,brush??ink,false,bold,align,details:Detail);
    private void Label(M.DrawingContext dc,string text,W.Rect r,double height,M.Brush? brush=null,int align=1,bool bold=true) {
        r=ScaleRect(r,Detail.SecondaryScale,Detail.SecondaryScale);r.Offset(0,Detail.DetailsOffsetY);DecorLabel(dc,text,r,height*Detail.SecondaryScale,brush,align,bold);
    }
    private void Title(M.DrawingContext dc,string text,W.Rect r,double height,M.Brush? brush=null,int align=1) {
        r=ScaleRect(r,Detail.TitleScale,Detail.TitleScale);r.Offset(0,Detail.TitleOffsetY);DecorLabel(dc,text,r,height*Detail.TitleScale,brush,align);
    }
    private void Number(M.DrawingContext dc,string text,W.Rect r,double height,bool stretch=false,M.Brush? brush=null,int align=-1,bool main=false) {
        double size=main?Math.Clamp(widget.Visual.FontSize/40,.2,6):Detail.SecondaryScale;int alignment=align<0?widget.Visual.Alignment:align;
        double width=r.Width*Detail.DigitWidth*size;
        r=new(r.X+(alignment==0?0:alignment==2?r.Width-width:(r.Width-width)/2),r.Y+r.Height*(1-size)/2,width,r.Height*size);
        r.Offset(main?Detail.MainOffsetX:0,main?Detail.MainOffsetY:Detail.DetailsOffsetY);
        typography.Draw(dc,text,r,height*size,brush??ink,true,widget.Visual.Bold,alignment,stretch,true,details:Detail);
    }
    private void Rule(M.DrawingContext dc,double x1,double y,double x2,M.Brush? brush=null) {if(!Detail.ShowDividers)return;dc.PushOpacity(Detail.DividerOpacity);dc.DrawLine(new M.Pen(brush??Edge,1.5),new(x1,y),new(x2,y));dc.Pop();}
    private void Well(M.DrawingContext dc,W.Rect rect,double radius=22) {
        if(!widget.Visual.Background)return;
        dc.PushOpacity(widget.Visual.FillOpacity);radius*=Detail.PanelRadius;double g=Detail.GrooveDepth;
        Round(dc,rect,radius,Gradient("#020202","#353535"));Round(dc,Inset(rect,2*g),Math.Max(0,radius-2*g),M.Brushes.Black);
        var fill=Brush(widget.Visual.FillColor);Round(dc,Inset(rect,5*g),Math.Max(0,radius-5*g),Gradient("#171717","#070707"));
        dc.PushOpacity(.3);Round(dc,Inset(rect,5*g),Math.Max(0,radius-5*g),fill);dc.Pop();dc.Pop();
    }
    private void Frame(M.DrawingContext dc) {
        dc.PushOpacity(widget.Visual.FillOpacity);
        double radius=Math.Clamp(widget.Visual.Radius*1.8,8,80),g=Detail.GrooveDepth;var rect=new W.Rect(4,4,492,DesignHeight-8);
        Round(dc,new(5,10,490,DesignHeight-13),radius,Brush("#030303"));
        Round(dc,rect,radius,Gradient("#676767","#242424"));Round(dc,Inset(rect,2*g),Math.Max(0,radius-2*g),M.Brushes.Black);
        Round(dc,Inset(rect,7*g),Math.Max(0,radius-7*g),Gradient("#333333","#181818"));Round(dc,Inset(rect,10*g),Math.Max(0,radius-10*g),M.Brushes.Black);
        Round(dc,Inset(rect,15*g),Math.Max(0,radius-15*g),Gradient("#161616","#090909"),Brush("#292929"));
        if(widget.Visual.BorderWidth>0)Round(dc,Inset(rect,10),Math.Max(0,radius-10),M.Brushes.Transparent,Brush(widget.Visual.BorderColor),widget.Visual.BorderWidth);
        dc.Pop();
    }
    private void Stripes(M.DrawingContext dc,W.Rect rect,double radius=12) {
        if(!Detail.ShowTexture)return;dc.PushOpacity(Detail.TextureOpacity*widget.Visual.FillOpacity);
        dc.PushClip(new M.RectangleGeometry(rect,radius,radius));
        for(double x=rect.Left-rect.Height;x<rect.Right;x+=Detail.TextureSpacing)dc.DrawLine(new M.Pen(Brush("#232323"),1),new(x,rect.Bottom),new(x+rect.Height,rect.Top));
        dc.Pop();dc.Pop();
    }
    private void Brand(M.DrawingContext dc,W.Rect rect,bool small=false) {
        if(!Detail.ShowBrand)return;
        double size=small?40:54;
        Symbol(dc,"brand",new(rect.X,rect.Y,size,size),accent);
        var logo=new W.Rect(rect.X+size+12,rect.Y+4,small?190:222,small?28:36);
        if(wordmark!=null)dc.DrawImage(wordmark,logo);else DecorLabel(dc,"ZZZwallpaper",logo,logo.Height);
        if(!small)DecorLabel(dc,"DESKTOP WIDGET",new(logo.X,rect.Y+42,220,14),13,Dim,0);
    }
    protected override void OnRender(M.DrawingContext dc) {
        base.OnRender(dc);int style=widget.Visual.TimeStyle;
        dc.PushClip(new M.RectangleGeometry(new(0,0,500,DesignHeight)));
        if(style!=1&&widget.Visual.Background&&Detail.ShowFrame)Frame(dc);
        if(style==0)Card(dc);else if(style==1)Floating(dc);else Instrument(dc);
        dc.Pop();
    }
    private string ClockTime=>now.ToString(widget.Clock.Use24Hour?"HH:mm":"hh:mm",CultureInfo.InvariantCulture);
    private string ClockDate=>!widget.Clock.ShowDate?"":now.ToString(widget.Clock.DateFormat switch {1=>"yyyy/MM/dd",2=>UiLanguage.Text("MM月dd日"),_=>"yyyy.MM.dd"},CultureInfo.GetCultureInfo(UiLanguage.Current));
    private string Weekday=>widget.Clock.ShowWeekday?now.ToString("dddd",CultureInfo.GetCultureInfo(UiLanguage.Current)):"";
    private long CountdownSeconds=>(long)Math.Ceiling((widget.Countdown.Target-now).Duration().TotalSeconds);
    private bool CountdownFinished=>now>=widget.Countdown.Target&&!widget.Countdown.CountUpAfterEnd;
    private string CountdownTime=>CountdownFinished?(widget.Countdown.ShowSeconds?"00:00:00":"00:00"):$"{CountdownSeconds/3600%24:00}:{CountdownSeconds/60%60:00}"+(widget.Countdown.ShowSeconds?$":{CountdownSeconds%60:00}":"");
    private FocusWidgetOptions TimerState=>widget.Focus.Advance(now);
    private string FocusTime {get {int seconds=(int)Math.Ceiling(TimerState.Remaining(now));return $"{seconds/60:00}:{seconds%60:00}";}}
    private double Progress=>Math.Clamp(1-TimerState.Remaining(now)/TimerState.DurationSeconds,0,1);
    private string RoundText {get {var f=TimerState;int round=f.Phase==0?f.CompletedSessions%f.SessionsPerLongBreak+1:(int)(((long)f.CompletedSessions-1+f.SessionsPerLongBreak)%f.SessionsPerLongBreak)+1;return UiLanguage.Format($"第 {round} / {f.SessionsPerLongBreak} 轮");}}
    private void ClockNumber(M.DrawingContext dc,W.Rect rect,double height) {
        if(widget.Clock.ShowSeconds) {
            var main=new W.Rect(rect.X,rect.Y,rect.Width*.81,rect.Height);Number(dc,ClockTime,main,height,true,main:true);
            var seconds=ScaleRect(new(rect.Right-rect.Width*.16,rect.Bottom-height*.42,rect.Width*.16,height*.4),Detail.SecondsScale,Detail.SecondsScale);
            Number(dc,now.ToString("ss"),seconds,height*.38*Detail.SecondsScale,true,Muted);
        } else Number(dc,ClockTime,rect,height,true,main:true);
        if(!widget.Clock.Use24Hour)Label(dc,now.Hour<12?UiLanguage.Text("上午"):UiLanguage.Text("下午"),new(rect.Right-62,rect.Top-23,62,18),16,Muted);
    }
    private void Days(M.DrawingContext dc,W.Rect rect,double height) {
        if(CountdownFinished) {Title(dc,UiLanguage.Text("已到时间"),rect,40);return;}
        string text=(CountdownSeconds/86400).ToString(Detail.PadDays?"00":"0");
        var number=new W.Rect(rect.X+(Detail.ShowDayUnit?0:rect.Width*.115),rect.Y,rect.Width*.77,rect.Height);
        // Keep one-digit days in one digit cell instead of stretching across both cells.
        if(text.Length==1)number=ScaleRect(number,.5,1);
        Number(dc,text,number,height,true,main:true);
        if(Detail.ShowDayUnit)Label(dc,UiLanguage.Text("天"),new(rect.X+rect.Width*.8,rect.Bottom-height*.43,rect.Width*.2,height*.4),height*.34,Muted);
    }
    private void Card(M.DrawingContext dc) {
        if(Detail.ShowBrand) {
            Well(dc,new(23,25,454,82),20);if(widget.Visual.Background)Stripes(dc,new(25,27,450,78),18);Brand(dc,new(35,40,410,58));
            DecorLabel(dc,"BEAUTIFUL",new(361,40,95,12),10,Dim,0);DecorLabel(dc,"WALLPAPERS",new(361,54,95,12),10,Dim,0);DecorLabel(dc,"FOR A BRIGHTER",new(361,68,100,12),10,Dim,0);DecorLabel(dc,"DESKTOP",new(361,82,95,12),10,Dim,0);
        }
        if(Detail.ShowAccentLine)Round(dc,new(34,18,216,7),3,accent);
        Well(dc,new(23,111,454,367),22);
        if(widget.Kind==WidgetKind.Clock) {
            if(widget.Clock.Face==1)Dial(dc,new(250,244),119*Detail.DialScale,true,0,false);else ClockNumber(dc,new(43,179,414,160),149);
            if(ClockDate.Length>0||Weekday.Length>0) {
                Rule(dc,43,380,457);Symbol(dc,"calendar",new(45,414,32,32),ink);
                if(ClockDate.Length>0)Label(dc,ClockDate,new(95,410,217,39),28,ink,0);
                if(Weekday.Length>0) {RuleVertical(dc,324,408,450);Label(dc,Weekday,new(343,410,102,39),29);}
            }
        } else if(widget.Kind==WidgetKind.Countdown) {
            if(Detail.ShowCountdownTitle)Title(dc,widget.Countdown.Title,new(51,139,397,42),34);
            Days(dc,new(81,204,345,154),151);
            if(Detail.ShowRemainingTime) {Rule(dc,43,380,457);Symbol(dc,"clock",new(45,413,35,35),ink);Number(dc,CountdownTime,new(144,408,280,45),43,true);}
        } else {
            if(Detail.ShowStatus) {Symbol(dc,"focus",new(132,141+Detail.TitleOffsetY,33,33),accent);Title(dc,TimerState.PhaseName+(Running?UiLanguage.Text("中"):""),new(175,135,208,46),37,ink,0);}
            Number(dc,FocusTime,new(88,210,324,88),86,true,main:true);
            if(Detail.ShowProgress) {Track(dc,new(43,326,Detail.ShowPercent?343:414,17),Progress);if(Detail.ShowPercent)Number(dc,$"{Progress*100:0}%",new(397,322,57,23),21,false,Muted);}
            if(Detail.ShowRound) {Rule(dc,45,383,129);Rule(dc,371,383,455);Label(dc,RoundText,new(138,367,222,29),21,Muted);}
            Actions(dc);
        }
    }
    private void Floating(M.DrawingContext dc) {
        if(Detail.ShowAccentLine)Round(dc,new(25,10,40,5),2,accent);
        if(widget.Kind==WidgetKind.Clock) {
            if(widget.Clock.Face==1)Dial(dc,new(250,155),116*Detail.DialScale,true,0,false);else ClockNumber(dc,new(16,103,468,148),140);
            if(ClockDate.Length>0||Weekday.Length>0) {
                Well(dc,new(24,285,452,102),49);Symbol(dc,"calendar",new(48,309,55,55),accent);RuleVertical(dc,124,299,372);
                Label(dc,ClockDate,new(146,304,298,32),30,ink,0);Label(dc,Weekday,new(146,348,280,25),23,Muted,0);
            }
        } else if(widget.Kind==WidgetKind.Countdown) {
            if(Detail.ShowCountdownTitle) {Well(dc,new(28,35,442,76),38);Symbol(dc,"hourglass",new(51,53,35,40),accent);RuleVertical(dc,111,48,99);Title(dc,widget.Countdown.Title,new(139,48,304,49),33);}
            Days(dc,new(58,145,390,151),149);
            if(Detail.ShowRemainingTime) {Well(dc,new(28,325,442,80),40);Symbol(dc,"clock",new(52,345,40,40),accent);RuleVertical(dc,114,337,395);Number(dc,CountdownTime,new(164,344,253,40),38,true);}
        } else {
            if(Detail.ShowStatus) {Well(dc,new(28,35,442,76),38);Symbol(dc,"focus",new(55,53,39,39),accent);RuleVertical(dc,116,48,99);Title(dc,TimerState.PhaseName+(Running?UiLanguage.Text("中"):""),new(140,48,297,49),34);}
            Number(dc,FocusTime,new(31,158,438,105),103,true,main:true);
            if(Detail.ShowProgress)Track(dc,new(31,291,Detail.ShowRound?289:438,15),Progress);
            if(Detail.ShowRound)Label(dc,RoundText,new(338,282,132,33),21,Muted);
            if(Detail.ShowButtons) {Well(dc,new(28,320,442,94),47);if(Detail.ShowResetButton&&Detail.ShowSkipButton) {RuleVertical(dc,187,334,400);RuleVertical(dc,349,334,400);}Actions(dc);}
        }
    }
    private void RuleVertical(M.DrawingContext dc,double x,double y1,double y2) {
        if(!Detail.ShowDividers)return;dc.PushOpacity(Detail.DividerOpacity);dc.DrawLine(new M.Pen(Edge,1.5),new(x,y1),new(x,y2));dc.Pop();
    }
    private void Instrument(M.DrawingContext dc) {
        if(widget.Visual.Background)Stripes(dc,new(20,20,460,700),22);
        if(Detail.ShowBrand) {DecorLabel(dc,"ZZZ",new(37,36,104,32),29,Dim,0);DecorLabel(dc,"DESKTOP WIDGET",new(37,72,170,13),12,Dim,0);DecorLabel(dc,"0.1.11.73",new(37,89,100,12),11,Dim,0);}
        for(int i=0;i<3;i++) {dc.PushTransform(new M.MatrixTransform(1,0,-.5,1,418+i*18,45));dc.DrawRectangle(Dim,null,new(0,0,11,21));dc.Pop();}
        bool clock=widget.Kind==WidgetKind.Clock;double amount=widget.Kind==WidgetKind.FocusTimer?Progress:CountdownFinished?0:CountdownSeconds%86400/86400d;
        dc.PushTransform(new M.ScaleTransform(Detail.DialScale,Detail.DialScale,250,291));
        Dial(dc,new(250,291),213,clock,amount,widget.Kind==WidgetKind.FocusTimer);
        if(widget.Kind==WidgetKind.Countdown) {
            if(Detail.ShowCountdownTitle)Title(dc,widget.Countdown.Title,new(121,193,258,44),35);
            Days(dc,new(139,256,240,114),112);
            if(Detail.ShowRemainingTime) {Rule(dc,169,383,331);Number(dc,CountdownTime,new(152,403,196,30),28,true,Muted);}
        } else if(!clock) {
            if(Detail.ShowStatus)Title(dc,TimerState.PhaseName+(Running?UiLanguage.Text("中"):""),new(133,204,234,39),33);
            Number(dc,FocusTime,new(111,275,278,79),76,true,main:true);
            if(Detail.ShowRound) {Rule(dc,160,387,340);Label(dc,RoundText,new(135,411,230,31),24,Muted);}
        }
        dc.Pop();
        if(clock) {
            if(Detail.ShowDigitalTime||ClockDate.Length>0||Weekday.Length>0)Well(dc,new(26,527,448,151),23);
            if(Detail.ShowDigitalTime)ClockNumber(dc,new(97,543,306,81),78);
            Label(dc,ClockDate,new(52,638,260,23),21,Muted,0);Label(dc,Weekday,new(324,636,116,26),21,Muted);
            if(ClockDate.Length>0&&Weekday.Length>0)RuleVertical(dc,298,635,664);
        } else if(widget.Kind==WidgetKind.Countdown) {
            if(Detail.ShowTargetDate) {
                Well(dc,new(26,544,448,133),23);Symbol(dc,"calendar",new(52,583,43,43),Muted);RuleVertical(dc,120,568,651);
                Label(dc,widget.Countdown.Target.ToLocalTime().ToString("yyyy.MM.dd"),new(148,576,290,32),29,Muted,0);
                Label(dc,now>widget.Countdown.Target&&widget.Countdown.CountUpAfterEnd?UiLanguage.Text("已经过去"):widget.Countdown.Target.ToLocalTime().ToString("dddd HH:mm",CultureInfo.GetCultureInfo(UiLanguage.Current)),new(148,622,288,27),23,Muted,0);
            }
        } else if(Detail.ShowButtons) {Well(dc,new(26,527,448,132),23);Actions(dc);}
        if(Detail.ShowBrand) {Brand(dc,new(34,687,395,41),true);DecorLabel(dc,"BEAUTIFUL WALLPAPERS",new(348,695,106,10),8,Dim,0);DecorLabel(dc,"FOR A BRIGHTER DESKTOP",new(348,708,111,10),8,Dim,0);}
        if(Detail.ShowScrews) {Screw(dc,new(28,28));Screw(dc,new(472,712));}
    }

    private static void Screw(M.DrawingContext dc,W.Point point) {dc.DrawEllipse(Gradient("#444444","#030303"),new M.Pen(M.Brushes.Black,2),point,7,7);dc.DrawLine(new M.Pen(Brush("#090909"),2),new(point.X-3,point.Y-3),new(point.X+3,point.Y+3));}
    private void Track(M.DrawingContext dc,W.Rect r,double progress) {
        r=ScaleRect(r,1,Detail.ProgressThickness);
        Round(dc,r,r.Height/2,Detail.ProgressTrackColor=="#181818"?Gradient("#080808","#292929"):Brush(Detail.ProgressTrackColor),M.Brushes.Black);
        if(progress>0)Round(dc,new(r.X+1,r.Y+1,Math.Max(1,(r.Width-2)*progress),r.Height-2),(r.Height-2)/2,accent);
    }
    private void Actions(M.DrawingContext dc) {
        for(int i=0;i<3;i++) {
            if(!ActionVisible(i))continue;
            var r=ActionBounds(i);bool circle=widget.Visual.TimeStyle!=0;double radius=circle?r.Height/2:14*Detail.PanelRadius,g=Detail.GrooveDepth;
            Round(dc,r,radius,Gradient("#5C5C5C","#080808"));Round(dc,Inset(r,2*g),Math.Max(0,radius-2*g),M.Brushes.Black);
            Round(dc,Inset(r,5*g),Math.Max(0,radius-5*g),i==0?accent:Gradient(hot==i?"#3A3A3A":"#242424","#101010"));
            if(pressed==i)Round(dc,Inset(r,5*g),Math.Max(0,radius-5*g),new M.SolidColorBrush(M.Color.FromArgb(58,0,0,0)));
            if(focused==i)Round(dc,Inset(r,1),Math.Max(0,radius-1),M.Brushes.Transparent,accent,2);
            double size=(circle?r.Height*.4:28*Detail.ButtonScale)*Detail.ButtonIconScale;Symbol(dc,i==0?(Running?"pause":"play"):i==1?"reset":"skip",new(r.X+(r.Width-size)/2,r.Y+(r.Height-size)/2,size,size),i==0?M.Brushes.Black:ink);
        }
    }
    private void Dial(M.DrawingContext dc,W.Point center,double radius,bool analog,double progress,bool smooth) {
        W.Point Point(double angle,double r)=>new(center.X+Math.Sin(angle)*r,center.Y-Math.Cos(angle)*r);
        dc.DrawEllipse(M.Brushes.Black,new M.Pen(Brush("#555555"),2),center,radius,radius);
        dc.DrawEllipse(Gradient("#363636","#161616"),new M.Pen(M.Brushes.Black,4),center,radius-5,radius-5);
        dc.DrawEllipse(M.Brushes.Black,new M.Pen(Brush("#090909"),2),center,radius-17,radius-17);
        var face=new M.RadialGradientBrush {Center=new(.32,.22),GradientOrigin=new(.32,.22),RadiusX=.85,RadiusY=.85};face.GradientStops.Add(new( M.Color.FromRgb(28,28,28),0));face.GradientStops.Add(new(M.Color.FromRgb(6,6,6),1));
        dc.DrawEllipse(face,new M.Pen(Brush("#292929"),2),center,radius-31,radius-31);
        if(!analog) {
            if(!Detail.ShowProgress)return;
            double ring=radius-29,origin=Detail.RingStartAngle*Math.PI/180,direction=Detail.RingClockwise?1:-1;int segments=Detail.RingSegments;
            for(int i=0;i<segments;i++) {
                double start=origin+direction*(i+.12)*Math.PI*2/segments,end=origin+direction*(i+.88)*Math.PI*2/segments;
                Arc(dc,center,ring,start,end,!smooth&&(i+.5)/segments<=progress?accent:Brush(Detail.RingTrackColor),16*Detail.RingThickness,false);
            }
            if(smooth&&progress>0) {
                Arc(dc,center,ring,origin,origin+direction*Math.Min(progress,.99999)*Math.PI*2,accent,17*Detail.RingThickness,true);
                if(Detail.ShowRingEndpoint) {var dot=Point(origin+direction*progress*Math.PI*2,ring);double size=12*Detail.RingThickness;dc.DrawEllipse(M.Brushes.White,new M.Pen(Brush("#777777"),3),dot,size,size);dc.DrawEllipse(null,new M.Pen(accent,3),dot,size+3,size+3);}
            }
            return;
        }
        if(Detail.TickMode!=2)for(int i=0;i<60;i++) {if(Detail.TickMode==1&&i%5!=0)continue;double a=i*Math.PI/30;dc.DrawLine(new M.Pen(i%5==0?ink:Dim,i%5==0?3:1.5),Point(a,radius*(i%5==0?.76:.81)),Point(a,radius*.86));}
        if(Detail.DialNumbers!=2)for(int i=0;i<12;i++) {if(Detail.DialNumbers==0&&i%3!=0)continue;var p=Point(i*Math.PI/6,radius*.64);typography.Draw(dc,i==0?"12":i.ToString(),new(p.X-30,p.Y-17,60,34),radius*.125,Muted,true,false,1,italic:false,details:Detail);}
        void Hand(double angle,double length,double width,M.Brush color)=>dc.DrawLine(new M.Pen(color,width) {StartLineCap=M.PenLineCap.Round,EndLineCap=M.PenLineCap.Round},center,Point(angle,length));
        Hand((now.Hour%12+now.Minute/60d)*Math.PI/6,radius*.45*Detail.HourHandLength,Math.Max(4,radius*.033)*Detail.HandWidth,ink);Hand((now.Minute+now.Second/60d)*Math.PI/30,radius*.7*Detail.MinuteHandLength,Math.Max(3,radius*.028)*Detail.HandWidth,accent);
        if(widget.Clock.ShowSeconds)Hand(now.Second*Math.PI/30,radius*.77,Detail.HandWidth,Brush(Detail.SecondHandColor));
        dc.DrawEllipse(accent,null,center,radius*.065,radius*.065);dc.DrawEllipse(M.Brushes.Black,null,center,radius*.033,radius*.033);
    }
    private static void Arc(M.DrawingContext dc,W.Point center,double radius,double start,double end,M.Brush color,double width,bool round) {
        W.Point Point(double angle)=>new(center.X+Math.Sin(angle)*radius,center.Y-Math.Cos(angle)*radius);
        var geometry=new M.StreamGeometry();using(var ctx=geometry.Open()) {ctx.BeginFigure(Point(start),false,false);ctx.ArcTo(Point(end),new(radius,radius),0,Math.Abs(end-start)>Math.PI,end>=start?M.SweepDirection.Clockwise:M.SweepDirection.Counterclockwise,true,false);}
        dc.DrawGeometry(null,new M.Pen(color,width) {StartLineCap=round?M.PenLineCap.Round:M.PenLineCap.Flat,EndLineCap=round?M.PenLineCap.Round:M.PenLineCap.Flat},geometry);
    }
    private static void Symbol(M.DrawingContext dc,string symbol,W.Rect r,M.Brush color) {
        dc.PushTransform(new M.TranslateTransform(r.X,r.Y));dc.PushTransform(new M.ScaleTransform(r.Width/24,r.Height/24));
        var pen=new M.Pen(color,2.7) {StartLineCap=M.PenLineCap.Round,EndLineCap=M.PenLineCap.Round,LineJoin=M.PenLineJoin.Round};
        switch(symbol) {
            case "brand": dc.DrawEllipse(color,null,new(12,12),11,11);dc.DrawEllipse(M.Brushes.Black,null,new(12,12),7.5,7.5);dc.DrawGeometry(color,null,M.Geometry.Parse("M 10,6 L 18,12 10,18 Z"));dc.DrawLine(new M.Pen(M.Brushes.Black,1.5),new(5,3),new(8,7));dc.DrawLine(new M.Pen(M.Brushes.Black,1.5),new(16,17),new(19,21));break;
            case "clock": dc.DrawEllipse(null,pen,new(12,12),9,9);dc.DrawLine(pen,new(12,6),new(12,12));dc.DrawLine(pen,new(12,12),new(16,14));break;
            case "focus": dc.DrawEllipse(null,new M.Pen(color,4),new(12,12),9,9);dc.DrawEllipse(color,null,new(12,12),3,3);dc.DrawLine(new M.Pen(M.Brushes.Black,2),new(12,0),new(12,5));break;
            case "calendar": Round(dc,new(3,5,18,17),2,M.Brushes.Transparent,color,2.5);dc.DrawLine(pen,new(3,10),new(21,10));dc.DrawLine(pen,new(8,2),new(8,7));dc.DrawLine(pen,new(16,2),new(16,7));for(int i=0;i<3;i++)for(int j=0;j<2;j++)dc.DrawEllipse(color,null,new(7+i*5,14+j*4),1.2,1.2);break;
            case "hourglass": dc.DrawLine(pen,new(4,2),new(20,2));dc.DrawLine(pen,new(4,22),new(20,22));dc.DrawGeometry(null,pen,M.Geometry.Parse("M 6,3 L 6,7 12,12 6,17 6,21 M 18,3 L 18,7 12,12 18,17 18,21"));break;
            case "pause": Round(dc,new(5,3,5,18),1.5,color);Round(dc,new(14,3,5,18),1.5,color);break;
            case "play": dc.DrawGeometry(color,null,M.Geometry.Parse("M 5,2 L 22,12 5,22 Z"));break;
            case "skip": dc.DrawGeometry(color,null,M.Geometry.Parse("M 3,3 L 18,12 3,21 Z M 19,3 L 23,3 23,21 19,21 Z"));break;
            case "reset": Arc(dc,new(12,12),8,.75,Math.PI*1.9,color,2.8,true);dc.DrawGeometry(color,null,M.Geometry.Parse("M 12,0 L 12,9 5,5 Z"));break;
        }
        dc.Pop();dc.Pop();
    }
}
