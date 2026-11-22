using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildTimeDetailSettings(WidgetInstance widget)
    {
        string id=widget.Id;var initial=widget.Visual.TimeAppearance;var sync=new List<Action<WidgetInstance>>();
        TimeWidgetAppearance Current()=>widgets!.State.Widgets.First(w=>w.Id==id).Visual.TimeAppearance;
        void Change(Func<TimeWidgetAppearance,TimeWidgetAppearance> update)=>LiveWidget(id,w=>w with {Visual=w.Visual with {TimeAppearance=update(w.Visual.TimeAppearance)}});
        void Visible(FrameworkElement element,Func<WidgetInstance,bool> show) {void Update(WidgetInstance w)=>element.Visibility=show(w)?Visibility.Visible:Visibility.Collapsed;Update(widget);sync.Add(Update);}
        var details=new StackPanel {Spacing=ParameterUi.FieldGap};
        details.Children.Add(UtilityText(UiLanguage.Text("外观细节"),"UtilityHeading"));
        WidgetEditorPanel.Children.Add(UtilityCard(details));
        StackPanel Section(int group,string title,Func<WidgetInstance,bool> show) {
            var body=new StackPanel {Spacing=16};var expander=new Expander {Style=HomeStyle("EffectsExpander"),IsExpanded=false,Header=UtilityText(title,"UtilityLabel"),Content=body,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            details.Children.Add(expander);Visible(expander,show);
            body.Children.Add(WidgetButton(UiLanguage.Text("恢复本组默认"),()=>Change(d=>d.ResetGroup(group))));return body;
        }
        void Number(StackPanel panel,string title,string unit,double min,double max,double factor,bool integer,Func<TimeWidgetAppearance,double> read,Func<TimeWidgetAppearance,double,TimeWidgetAppearance> set,Func<WidgetInstance,bool> show) {
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min*factor,Maximum=max*factor,StepFrequency=integer?1:factor==100?1:.1,Value=read(initial)*factor};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,title);
            var row=NumericSliderRow(title,slider,unit,integer||factor==100?0:1);panel.Children.Add(row);Visible(row,show);
            slider.ValueChanged+=(_,_)=>Change(d=>set(d,slider.Value/factor));sync.Add(w=>slider.Value=read(w.Visual.TimeAppearance)*factor);
        }
        void Toggle(StackPanel panel,string title,Func<TimeWidgetAppearance,bool> read,Func<TimeWidgetAppearance,bool,TimeWidgetAppearance> set,Func<WidgetInstance,bool> show) {
            var toggle=WidgetToggle(title,read(initial));var row=WidgetToggleRow(title,toggle);panel.Children.Add(row);Visible(row,show);
            toggle.Toggled+=(_,_)=>Change(d=>set(d,toggle.IsOn));sync.Add(w=>toggle.IsOn=read(w.Visual.TimeAppearance));
        }
        void Choice(StackPanel panel,string title,string[] values,Func<TimeWidgetAppearance,int> read,Func<TimeWidgetAppearance,int,TimeWidgetAppearance> set,Func<WidgetInstance,bool> show) {
            var choice=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var value in values)choice.Items.Add(value);choice.SelectedIndex=read(initial);
            var row=WidgetField(title,choice);panel.Children.Add(row);Visible(row,show);
            choice.SelectionChanged+=(_,_)=> {if(choice.SelectedIndex>=0)Change(d=>set(d,choice.SelectedIndex));};sync.Add(w=>choice.SelectedIndex=read(w.Visual.TimeAppearance));
        }
        void Color(StackPanel panel,string title,Func<TimeWidgetAppearance,string> read,Func<TimeWidgetAppearance,string,TimeWidgetAppearance> set,Func<WidgetInstance,bool> show) {
            var button=ColorChoiceButton(title,()=>SkinColor(read(Current())),color=> {if(!syncingWidgetEditor)Change(d=>set(d,ColorHex(color)));});
            var row=WidgetField(title,button);panel.Children.Add(row);Visible(row,show);sync.Add(_=> {if(button.Tag is Action refresh)refresh();});
        }
        var group0=Section(0,UiLanguage.Text("数字字形"),w=>true);
        Choice(group0,UiLanguage.Text("数字字体"),["Saira Condensed",UiLanguage.Text("思源黑体")],d=>d.DigitFont,(d,v)=>d with {DigitFont=v},w=>true);
        Number(group0,UiLanguage.Text("数字倾斜"),"°",-15,25,1,false,d=>d.DigitSlant,(d,v)=>d with {DigitSlant=v},w=>true);
        Number(group0,UiLanguage.Text("数字字距"),"px",0,12,1,false,d=>d.DigitSpacing,(d,v)=>d with {DigitSpacing=v},w=>true);
        Number(group0,UiLanguage.Text("数字横向比例"),"%",0.6,1.25,100,false,d=>d.DigitWidth,(d,v)=>d with {DigitWidth=v},w=>true);
        Toggle(group0,UiLanguage.Text("数字明暗渐变"),d=>d.NumberGradient,(d,v)=>d with {NumberGradient=v},w=>true);
        Toggle(group0,UiLanguage.Text("数字阴影"),d=>d.NumberShadow,(d,v)=>d with {NumberShadow=v},w=>true);
        Number(group0,UiLanguage.Text("阴影不透明度"),"%",0,1,100,false,d=>d.ShadowOpacity,(d,v)=>d with {ShadowOpacity=v},w=>w.Visual.TimeAppearance.NumberShadow);
        Number(group0,UiLanguage.Text("阴影距离"),"px",0,12,1,false,d=>d.ShadowDistance,(d,v)=>d with {ShadowDistance=v},w=>w.Visual.TimeAppearance.NumberShadow);
        Number(group0,UiLanguage.Text("主数字水平偏移"),"px",-60,60,1,false,d=>d.MainOffsetX,(d,v)=>d with {MainOffsetX=v},w=>true);
        Number(group0,UiLanguage.Text("主数字垂直偏移"),"px",-60,60,1,false,d=>d.MainOffsetY,(d,v)=>d with {MainOffsetY=v},w=>true);
        var group1=Section(1,UiLanguage.Text("标题与辅助文字"),w=>true);
        Number(group1,UiLanguage.Text("文字倾斜"),"°",-15,25,1,false,d=>d.TextSlant,(d,v)=>d with {TextSlant=v},w=>true);
        Number(group1,UiLanguage.Text("标题大小"),"%",0.5,1.5,100,false,d=>d.TitleScale,(d,v)=>d with {TitleScale=v},w=>w.Kind!=WidgetKind.Clock);
        Number(group1,UiLanguage.Text("辅助文字大小"),"%",0.5,1.5,100,false,d=>d.SecondaryScale,(d,v)=>d with {SecondaryScale=v},w=>true);
        Number(group1,UiLanguage.Text("标题垂直偏移"),"px",-40,40,1,false,d=>d.TitleOffsetY,(d,v)=>d with {TitleOffsetY=v},w=>w.Kind!=WidgetKind.Clock);
        Number(group1,UiLanguage.Text("辅助文字垂直偏移"),"px",-40,40,1,false,d=>d.DetailsOffsetY,(d,v)=>d with {DetailsOffsetY=v},w=>true);
        var group2=Section(2,UiLanguage.Text("独立配色"),w=>true);
        Toggle(group2,UiLanguage.Text("强调色跟随主题"),d=>d.FollowAccent,(d,v)=>d with {FollowAccent=v},w=>true);
        Color(group2,UiLanguage.Text("强调色"),d=>d.AccentColor,(d,v)=>d with {AccentColor=v},w=>!w.Visual.TimeAppearance.FollowAccent);
        Color(group2,UiLanguage.Text("辅助文字颜色"),d=>d.SecondaryColor,(d,v)=>d with {SecondaryColor=v},w=>true);
        Color(group2,UiLanguage.Text("装饰文字颜色"),d=>d.DecorationColor,(d,v)=>d with {DecorationColor=v},w=>true);
        var group3=Section(3,UiLanguage.Text("装饰与凹槽"),w=>true);
        Toggle(group3,UiLanguage.Text("显示品牌栏"),d=>d.ShowBrand,(d,v)=>d with {ShowBrand=v},w=>w.Visual.TimeStyle!=1);
        Toggle(group3,UiLanguage.Text("显示外框"),d=>d.ShowFrame,(d,v)=>d with {ShowFrame=v},w=>w.Visual.TimeStyle!=1);
        Number(group3,UiLanguage.Text("凹槽深度"),"%",0,2,100,false,d=>d.GrooveDepth,(d,v)=>d with {GrooveDepth=v},w=>true);
        Number(group3,UiLanguage.Text("内框圆角比例"),"%",0,2,100,false,d=>d.PanelRadius,(d,v)=>d with {PanelRadius=v},w=>true);
        Toggle(group3,UiLanguage.Text("显示斜纹"),d=>d.ShowTexture,(d,v)=>d with {ShowTexture=v},w=>w.Visual.TimeStyle!=1);
        Number(group3,UiLanguage.Text("斜纹不透明度"),"%",0,1,100,false,d=>d.TextureOpacity,(d,v)=>d with {TextureOpacity=v},w=>w.Visual.TimeStyle!=1&&w.Visual.TimeAppearance.ShowTexture);
        Number(group3,UiLanguage.Text("斜纹间距"),"px",4,24,1,false,d=>d.TextureSpacing,(d,v)=>d with {TextureSpacing=v},w=>w.Visual.TimeStyle!=1&&w.Visual.TimeAppearance.ShowTexture);
        Toggle(group3,UiLanguage.Text("显示强调短线"),d=>d.ShowAccentLine,(d,v)=>d with {ShowAccentLine=v},w=>w.Visual.TimeStyle!=2);
        Toggle(group3,UiLanguage.Text("显示分隔线"),d=>d.ShowDividers,(d,v)=>d with {ShowDividers=v},w=>true);
        Number(group3,UiLanguage.Text("分隔线不透明度"),"%",0,1,100,false,d=>d.DividerOpacity,(d,v)=>d with {DividerOpacity=v},w=>w.Visual.TimeAppearance.ShowDividers);
        Toggle(group3,UiLanguage.Text("显示螺钉"),d=>d.ShowScrews,(d,v)=>d with {ShowScrews=v},w=>w.Visual.TimeStyle==2);
        var group4=Section(4,UiLanguage.Text("时钟与圆盘"),w=>w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&(w.Clock.Face==1||w.Clock.ShowSeconds)));
        Number(group4,UiLanguage.Text("圆盘大小"),"%",0.65,1.05,100,false,d=>d.DialScale,(d,v)=>d with {DialScale=v},w=>w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1));
        Choice(group4,UiLanguage.Text("刻度"),[UiLanguage.Text("全部刻度"),UiLanguage.Text("仅整点"),UiLanguage.Text("隐藏刻度")],d=>d.TickMode,(d,v)=>d with {TickMode=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1)));
        Choice(group4,UiLanguage.Text("表盘数字"),[UiLanguage.Text("四个方向"),UiLanguage.Text("十二个数字"),UiLanguage.Text("隐藏数字")],d=>d.DialNumbers,(d,v)=>d with {DialNumbers=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1)));
        Number(group4,UiLanguage.Text("指针粗细"),"%",0.5,2,100,false,d=>d.HandWidth,(d,v)=>d with {HandWidth=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1)));
        Number(group4,UiLanguage.Text("时针长度"),"%",0.5,1.2,100,false,d=>d.HourHandLength,(d,v)=>d with {HourHandLength=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1)));
        Number(group4,UiLanguage.Text("分针长度"),"%",0.5,1.1,100,false,d=>d.MinuteHandLength,(d,v)=>d with {MinuteHandLength=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1)));
        Color(group4,UiLanguage.Text("秒针颜色"),d=>d.SecondHandColor,(d,v)=>d with {SecondHandColor=v},w=>w.Kind==WidgetKind.Clock&&(w.Visual.TimeStyle==2||(w.Kind==WidgetKind.Clock&&w.Clock.Face==1))&&w.Clock.ShowSeconds);
        Number(group4,UiLanguage.Text("小号秒数大小"),"%",0.5,1.5,100,false,d=>d.SecondsScale,(d,v)=>d with {SecondsScale=v},w=>w.Kind==WidgetKind.Clock&&w.Clock.ShowSeconds&&(w.Visual.TimeStyle==2?w.Visual.TimeAppearance.ShowDigitalTime:w.Clock.Face==0));
        Toggle(group4,UiLanguage.Text("显示数字时间"),d=>d.ShowDigitalTime,(d,v)=>d with {ShowDigitalTime=v},w=>w.Kind==WidgetKind.Clock&&w.Visual.TimeStyle==2);
        var group5=Section(5,UiLanguage.Text("倒计时内容"),w=>w.Kind==WidgetKind.Countdown);
        Toggle(group5,UiLanguage.Text("显示倒计时标题"),d=>d.ShowCountdownTitle,(d,v)=>d with {ShowCountdownTitle=v},w=>true);
        Toggle(group5,UiLanguage.Text("显示天数单位"),d=>d.ShowDayUnit,(d,v)=>d with {ShowDayUnit=v},w=>true);
        Toggle(group5,UiLanguage.Text("天数补零"),d=>d.PadDays,(d,v)=>d with {PadDays=v},w=>true);
        Toggle(group5,UiLanguage.Text("显示剩余时分秒"),d=>d.ShowRemainingTime,(d,v)=>d with {ShowRemainingTime=v},w=>true);
        Toggle(group5,UiLanguage.Text("显示目标日期"),d=>d.ShowTargetDate,(d,v)=>d with {ShowTargetDate=v},w=>w.Visual.TimeStyle==2);
        var group6=Section(6,UiLanguage.Text("进度条与圆环"),w=>w.Kind==WidgetKind.FocusTimer||(w.Kind==WidgetKind.Countdown&&w.Visual.TimeStyle==2));
        Toggle(group6,UiLanguage.Text("显示进度"),d=>d.ShowProgress,(d,v)=>d with {ShowProgress=v},w=>true);
        Number(group6,UiLanguage.Text("进度条粗细"),"%",0.5,2,100,false,d=>d.ProgressThickness,(d,v)=>d with {ProgressThickness=v},w=>w.Visual.TimeStyle!=2&&w.Visual.TimeAppearance.ShowProgress);
        Color(group6,UiLanguage.Text("进度条底色"),d=>d.ProgressTrackColor,(d,v)=>d with {ProgressTrackColor=v},w=>w.Visual.TimeStyle!=2&&w.Visual.TimeAppearance.ShowProgress);
        Toggle(group6,UiLanguage.Text("显示进度百分比"),d=>d.ShowPercent,(d,v)=>d with {ShowPercent=v},w=>w.Kind==WidgetKind.FocusTimer&&w.Visual.TimeStyle==0&&w.Visual.TimeAppearance.ShowProgress);
        Number(group6,UiLanguage.Text("圆环分段数"),UiLanguage.Text("段"),12,120,1,true,d=>d.RingSegments,(d,v)=>d with {RingSegments=(int)v},w=>w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        Number(group6,UiLanguage.Text("圆环粗细"),"%",0.5,1.5,100,false,d=>d.RingThickness,(d,v)=>d with {RingThickness=v},w=>w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        Number(group6,UiLanguage.Text("圆环起始角度"),"°",0,360,1,false,d=>d.RingStartAngle,(d,v)=>d with {RingStartAngle=v},w=>w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        Toggle(group6,UiLanguage.Text("顺时针进度"),d=>d.RingClockwise,(d,v)=>d with {RingClockwise=v},w=>w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        Color(group6,UiLanguage.Text("圆环底色"),d=>d.RingTrackColor,(d,v)=>d with {RingTrackColor=v},w=>w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        Toggle(group6,UiLanguage.Text("显示进度端点"),d=>d.ShowRingEndpoint,(d,v)=>d with {ShowRingEndpoint=v},w=>w.Kind==WidgetKind.FocusTimer&&w.Visual.TimeStyle==2&&w.Visual.TimeAppearance.ShowProgress);
        var group7=Section(7,UiLanguage.Text("专注状态与按钮"),w=>w.Kind==WidgetKind.FocusTimer);
        Toggle(group7,UiLanguage.Text("显示专注状态"),d=>d.ShowStatus,(d,v)=>d with {ShowStatus=v},w=>true);
        Toggle(group7,UiLanguage.Text("显示轮次"),d=>d.ShowRound,(d,v)=>d with {ShowRound=v},w=>true);
        Toggle(group7,UiLanguage.Text("显示操作按钮"),d=>d.ShowButtons,(d,v)=>d with {ShowButtons=v},w=>true);
        Toggle(group7,UiLanguage.Text("显示重置按钮"),d=>d.ShowResetButton,(d,v)=>d with {ShowResetButton=v},w=>w.Visual.TimeAppearance.ShowButtons);
        Toggle(group7,UiLanguage.Text("显示跳过按钮"),d=>d.ShowSkipButton,(d,v)=>d with {ShowSkipButton=v},w=>w.Visual.TimeAppearance.ShowButtons);
        Number(group7,UiLanguage.Text("按钮大小"),"%",0.6,1.2,100,false,d=>d.ButtonScale,(d,v)=>d with {ButtonScale=v},w=>w.Visual.TimeAppearance.ShowButtons);
        Number(group7,UiLanguage.Text("按钮间距"),"%",0.65,1.1,100,false,d=>d.ButtonSpacing,(d,v)=>d with {ButtonSpacing=v},w=>w.Visual.TimeAppearance.ShowButtons);
        Number(group7,UiLanguage.Text("按钮图标大小"),"%",0.6,1.5,100,false,d=>d.ButtonIconScale,(d,v)=>d with {ButtonIconScale=v},w=>w.Visual.TimeAppearance.ShowButtons);
        Number(group7,UiLanguage.Text("按钮垂直偏移"),"px",-20,20,1,false,d=>d.ButtonOffsetY,(d,v)=>d with {ButtonOffsetY=v},w=>w.Visual.TimeAppearance.ShowButtons);
        details.Children.Add(WidgetButton(UiLanguage.Text("恢复全部细节"),()=>Change(_=>new())));
        return ()=> {var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current!=null)foreach(var action in sync)action(current);};
    }
}

