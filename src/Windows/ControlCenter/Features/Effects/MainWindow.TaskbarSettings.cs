using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private Slider taskbarOpacity=null!,taskbarBlur=null!;
    private StackPanel taskbarVisualFields=null!;
    private FrameworkElement taskbarBlurField=null!;
    private ToggleSwitch taskbarBorder=null!;
    private Button taskbarColor=null!;
    private bool syncingTaskbar;
    private FrameworkElement CreateTaskbarSettings()
    {
        var panel=new StackPanel {Spacing=ParameterUi.FieldGap};
        taskbarSwitch=WidgetToggle(UiLanguage.Text("启用任务栏设置"),false);taskbarSwitch.Visibility=Visibility.Collapsed;panel.Children.Add(taskbarSwitch);
        taskbarStylePicker=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(string label in new[]{UiLanguage.Text("隐藏"),UiLanguage.Text("透明"),UiLanguage.Text("磨砂")})taskbarStylePicker.Items.Add(new ComboBoxItem {Content=label});
        var body=new StackPanel {Spacing=ParameterUi.FieldGap};body.Children.Add(WidgetField(UiLanguage.Text("任务栏样式"),taskbarStylePicker));
        taskbarVisualFields=new StackPanel {Spacing=ParameterUi.FieldGap};body.Children.Add(taskbarVisualFields);
        taskbarOpacity=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=1};
        taskbarBlur=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=1};
        taskbarVisualFields.Children.Add(NumericSliderRow(UiLanguage.Text("背景不透明度"),taskbarOpacity,"%",0));
        taskbarVisualFields.Children.Add(taskbarBlurField=NumericSliderRow(UiLanguage.Text("磨砂强度"),taskbarBlur,"",20));
        taskbarColor=ColorChoiceButton(UiLanguage.Text("任务栏背景颜色"),()=>SkinColor(effectSession.Collection.TaskbarVisual.Color),color=>ChangeTaskbarVisual(v=>v with {Color=ColorHex(color)}));
        taskbarVisualFields.Children.Add(WidgetField(UiLanguage.Text("背景颜色"),taskbarColor));
        taskbarBorder=WidgetToggle(UiLanguage.Text("显示边线"),false);taskbarVisualFields.Children.Add(WidgetToggleRow(UiLanguage.Text("显示边线"),taskbarBorder));
        panel.Children.Add(UtilityCard(body,UiLanguage.Text("任务栏外观")));
        taskbarSwitch.Toggled+=(_,_)=> {if(!loadingEffects){effectSession.Collection=effectSession.Collection with {HideTaskbarEnabled=taskbarSwitch.IsOn};DraftChanged();}};
        taskbarStylePicker.SelectionChanged+=(_,_)=> {if(!loadingEffects&&taskbarStylePicker.SelectedIndex>=0){effectSession.Collection=effectSession.Collection with {TaskbarStyle=(TaskbarStyle)taskbarStylePicker.SelectedIndex};SyncTaskbarSettings();DraftChanged();}};
        taskbarOpacity.ValueChanged+=(_,_)=>ChangeTaskbarVisual(v=>effectSession.Collection.TaskbarStyle==TaskbarStyle.Transparent?v with {TransparentOpacity=(int)taskbarOpacity.Value}:v with {FrostedOpacity=(int)taskbarOpacity.Value});
        taskbarBlur.ValueChanged+=(_,_)=>ChangeTaskbarVisual(v=>v with {Blur=(int)taskbarBlur.Value});
        taskbarBorder.Toggled+=(_,_)=>ChangeTaskbarVisual(v=>v with {ShowBorder=taskbarBorder.IsOn});
        return panel;
    }
    private void ChangeTaskbarVisual(Func<TaskbarVisualOptions,TaskbarVisualOptions> change)
    {
        if(loadingEffects||syncingTaskbar)return;
        effectSession.Collection=effectSession.Collection with {TaskbarVisual=change(effectSession.Collection.TaskbarVisual).Validate()};DraftChanged();
    }
    private void SyncTaskbarSettings()
    {
        syncingTaskbar=true;
        try {
            var collection=effectSession.Collection;var visual=collection.TaskbarVisual;
            taskbarVisualFields.Visibility=collection.TaskbarStyle==TaskbarStyle.Hidden?Visibility.Collapsed:Visibility.Visible;
            taskbarBlurField.Visibility=collection.TaskbarStyle==TaskbarStyle.Frosted?Visibility.Visible:Visibility.Collapsed;
            taskbarOpacity.Value=collection.TaskbarStyle==TaskbarStyle.Transparent?visual.TransparentOpacity:visual.FrostedOpacity;
            taskbarBlur.Value=visual.Blur;taskbarBorder.IsOn=visual.ShowBorder;(taskbarColor.Tag as Action)?.Invoke();
        } finally {syncingTaskbar=false;}
    }
}
