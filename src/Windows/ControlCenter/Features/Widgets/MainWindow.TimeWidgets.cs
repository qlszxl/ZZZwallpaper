using System.Globalization;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildTimeWidgetSettings(WidgetInstance widget)
    {
        string id=widget.Id;var sync=new List<Action<WidgetInstance>>();
        StackPanel Section(string title) {var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(title,"UtilityHeading"));WidgetEditorPanel.Children.Add(UtilityCard(panel));return panel;}
        void Toggle(StackPanel panel,string title,Func<WidgetInstance,bool> read,Func<WidgetInstance,bool,WidgetInstance> set) {
            var control=WidgetToggle(title,read(widget));panel.Children.Add(WidgetToggleRow(title,control));control.Toggled+=(_,_)=>LiveWidget(id,w=>set(w,control.IsOn));sync.Add(w=>control.IsOn=read(w));
        }
        FrameworkElement Choice(StackPanel panel,string title,string[] labels,Func<WidgetInstance,int> read,Func<WidgetInstance,int,WidgetInstance> set) {
            var control=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var label in labels)control.Items.Add(label);control.SelectedIndex=read(widget);var field=WidgetField(title,control);panel.Children.Add(field);
            control.SelectionChanged+=(_,_)=> {if(control.SelectedIndex>=0)LiveWidget(id,w=>set(w,control.SelectedIndex));};sync.Add(w=>control.SelectedIndex=read(w));
            return field;
        }
        void Minutes(StackPanel panel,string title,int max,Func<FocusWidgetOptions,int> read,Func<FocusWidgetOptions,int,FocusWidgetOptions> set,string? unit=null) {
            unit ??= UiLanguage.Text("分钟");
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=1,Maximum=max,StepFrequency=1,Value=read(widget.Focus)};panel.Children.Add(NumericSliderRow(title,slider,unit,0));
            slider.ValueChanged+=(_,_)=>LiveWidget(id,w=> {var f=set(w.Focus,(int)slider.Value);return w with {Focus=f.Action("reset",DateTimeOffset.UtcNow)};});sync.Add(w=>slider.Value=read(w.Focus));
        }
        var appearance=Section(UiLanguage.Text("外观样式"));
        Choice(appearance,UiLanguage.Text("组件样式"),[UiLanguage.Text("A · 凹槽卡片"),UiLanguage.Text("B · 字体浮层"),UiLanguage.Text("C · 仪表圆盘")],w=>w.Visual.TimeStyle,(w,v)=>w with {Visual=w.Visual with {TimeStyle=v,Width=v==2?300:v==1?400:360,Height=v==2?444:v==1?344:360}});
        appearance.Children.Add(WidgetButton(UiLanguage.Text("恢复样式尺寸"),()=>LiveWidget(id,w=>w with {Visual=w.Visual with {Width=w.Visual.TimeStyle==2?300:w.Visual.TimeStyle==1?400:360,Height=w.Visual.TimeStyle==2?444:w.Visual.TimeStyle==1?344:360}})));
        if(widget.Kind==WidgetKind.Clock) {
            var panel=Section(UiLanguage.Text("时钟与日期"));
            var face=Choice(panel,UiLanguage.Text("时钟样式"),[UiLanguage.Text("数字时钟"),UiLanguage.Text("指针时钟")],w=>w.Clock.Face,(w,v)=>w with {Clock=w.Clock with {Face=v}});
            face.Visibility=widget.Visual.TimeStyle==2?Visibility.Collapsed:Visibility.Visible;
            sync.Add(w=>face.Visibility=w.Visual.TimeStyle==2?Visibility.Collapsed:Visibility.Visible);
            Toggle(panel,UiLanguage.Text("24 小时制"),w=>w.Clock.Use24Hour,(w,v)=>w with {Clock=w.Clock with {Use24Hour=v}});
            Toggle(panel,UiLanguage.Text("显示秒数"),w=>w.Clock.ShowSeconds,(w,v)=>w with {Clock=w.Clock with {ShowSeconds=v}});
            Toggle(panel,UiLanguage.Text("显示日期"),w=>w.Clock.ShowDate,(w,v)=>w with {Clock=w.Clock with {ShowDate=v}});
            Toggle(panel,UiLanguage.Text("显示星期"),w=>w.Clock.ShowWeekday,(w,v)=>w with {Clock=w.Clock with {ShowWeekday=v}});
            Choice(panel,UiLanguage.Text("日期格式"),["YYYY.MM.DD","YYYY/MM/DD",UiLanguage.Text("月 / 日")],w=>w.Clock.DateFormat,(w,v)=>w with {Clock=w.Clock with {DateFormat=v}});
        } else if(widget.Kind==WidgetKind.Countdown) {
            var panel=Section(UiLanguage.Text("倒计时"));
            var title=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=widget.Countdown.Title,MaxLength=80};panel.Children.Add(WidgetField(UiLanguage.Text("名称"),title));
            DoubleClickEdit.OnAccepted(title,()=>LiveWidget(id,w=>w with {Countdown=w.Countdown with {Title=title.Text}}));sync.Add(w=> {if(title.IsReadOnly&&title.Text!=w.Countdown.Title)title.Text=w.Countdown.Title;});
            string Format(DateTimeOffset value)=>value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
            var target=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=Format(widget.Countdown.Target),PlaceholderText="yyyy-MM-dd HH:mm:ss",MaxLength=19};panel.Children.Add(WidgetField(UiLanguage.Text("目标时间"),target));
            void SaveTarget() {
                if(syncingWidgetEditor)return;
                if(DateTime.TryParseExact(target.Text.Trim(),["yyyy-MM-dd HH:mm:ss","yyyy-MM-dd HH:mm","yyyy-MM-dd"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)&&date.Year is >=1970 and <=9998&&!TimeZoneInfo.Local.IsInvalidTime(date))
                    LiveWidget(id,w=>w with {Countdown=w.Countdown with {Target=new DateTimeOffset(date,TimeZoneInfo.Local.GetUtcOffset(date))}});
                else {var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current!=null)target.Text=Format(current.Countdown.Target);ShowError(UiLanguage.Text("请输入有效的目标日期和时间。"));}
            }
            DoubleClickEdit.OnAccepted(target,SaveTarget);
            sync.Add(w=> {if(target.FocusState==FocusState.Unfocused)target.Text=Format(w.Countdown.Target);});
            Toggle(panel,UiLanguage.Text("显示秒数"),w=>w.Countdown.ShowSeconds,(w,v)=>w with {Countdown=w.Countdown with {ShowSeconds=v}});
            Toggle(panel,UiLanguage.Text("到期后正计时"),w=>w.Countdown.CountUpAfterEnd,(w,v)=>w with {Countdown=w.Countdown with {CountUpAfterEnd=v}});
        } else {
            var panel=Section(UiLanguage.Text("专注计时器"));
            var state=UtilityText("","UtilityLabel");panel.Children.Add(state);
            var actions=new Grid {ColumnSpacing=12,RowSpacing=12};
            var start=WidgetButton(UiLanguage.Text("开始"),()=>WidgetEdit(()=>widgets?.ControlFocus(id,widgets.State.Widgets.First(w=>w.Id==id).Focus.Advance(DateTimeOffset.UtcNow).Running?"pause":"start")),primary:true);
            actions.Children.Add(start);actions.Children.Add(WidgetButton(UiLanguage.Text("重置"),()=>WidgetEdit(()=>widgets?.ControlFocus(id,"reset"))));actions.Children.Add(WidgetButton(UiLanguage.Text("跳过"),()=>WidgetEdit(()=>widgets?.ControlFocus(id,"skip"))));UtilityAdaptiveGrid(actions,3,480);panel.Children.Add(actions);
            Minutes(panel,UiLanguage.Text("专注时长"),240,f=>f.FocusMinutes,(f,v)=>f with {FocusMinutes=v});
            Minutes(panel,UiLanguage.Text("短休息时长"),120,f=>f.BreakMinutes,(f,v)=>f with {BreakMinutes=v});
            Minutes(panel,UiLanguage.Text("长休息时长"),240,f=>f.LongBreakMinutes,(f,v)=>f with {LongBreakMinutes=v});
            Minutes(panel,UiLanguage.Text("长休息间隔"),12,f=>f.SessionsPerLongBreak,(f,v)=>f with {SessionsPerLongBreak=v},UiLanguage.Text("轮"));
            Toggle(panel,UiLanguage.Text("自动开始下一阶段"),w=>w.Focus.AutoStart,(w,v)=>w with {Focus=w.Focus with {AutoStart=v}});
            sync.Add(w=> {UtilityButtonContent(start,w.Focus.Running?UiLanguage.Text("暂停"):UiLanguage.Text("开始"));state.Text=UiLanguage.Format($"{w.Focus.PhaseName} · 已完成 {w.Focus.CompletedSessions} 轮");foreach(var button in actions.Children.OfType<Button>())button.IsEnabled=!w.InteractionLocked;});
        }
        var syncVisual=BuildVisualWidgetSettings(widget);
        var syncDetails=BuildTimeDetailSettings(widget);
        return ()=> {var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return;foreach(var action in sync)action(current);syncVisual();syncDetails();};
    }
}
