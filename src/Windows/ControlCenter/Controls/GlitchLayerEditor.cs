using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace LumaWall.ControlCenter;

public sealed class GlitchLayerEditor : UserControl
{
    private static readonly string[] Modes=[UiLanguage.Text("横向撕裂"),UiLanguage.Text("块状错位"),UiLanguage.Text("细条抖动"),UiLanguage.Text("RGB 重影")];
    private readonly int number;
    private readonly TextBlock heading=new();
    private readonly ToggleSwitch enabled=new() {OnContent="",OffContent="",Width=40,MinWidth=40,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly ComboBox mode=new() {HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,ItemsSource=Modes};
    private readonly Slider strength=Parameter(0,100,1),rate=Parameter(0,40,.1),coverage=Parameter(0,100,1);
    private readonly Slider size=Parameter(2,240,1),rgb=Parameter(0,80,1);
    private bool loading;
    public GlitchLayer Value {get;private set;}=new();
    public event Action? Changed;
    private static Slider Parameter(double min,double max,double step) => new() {
        Minimum=min,Maximum=max,StepFrequency=step,MinWidth=96,VerticalAlignment=VerticalAlignment.Center
    };
    public GlitchLayerEditor(int index)
    {
        number=index+1;
        heading.Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["EffectsHeading"];
        enabled.Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["EffectsToggleSwitch"];
        mode.Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["HomeComboBox"];
        mode.FontFamily=UiFonts.Body;mode.FontSize=14;mode.FontWeight=Microsoft.UI.Text.FontWeights.Medium;
        var titleRow=new Grid {ColumnSpacing=16,MinHeight=32};
        titleRow.ColumnDefinitions.Add(new());titleRow.ColumnDefinitions.Add(new() {Width=new GridLength(40)});
        titleRow.Children.Add(heading);Grid.SetColumn(enabled,1);titleRow.Children.Add(enabled);
        AutomationProperties.SetName(enabled,number==1?UiLanguage.Text("启用主故障"):UiLanguage.Format($"启用小故障 {number-1}"));
        AutomationProperties.SetName(mode,UiLanguage.Text("局部故障类型"));
        var section=new StackPanel {Spacing=12};section.Children.Add(titleRow);
        var parameters=new StackPanel {Spacing=4};section.Children.Add(parameters);
        var modeGroup=new Grid {ColumnSpacing=16,MinHeight=44};
        modeGroup.ColumnDefinitions.Add(new() {Width=new GridLength(128)});modeGroup.ColumnDefinitions.Add(new());modeGroup.ColumnDefinitions.Add(new() {Width=new GridLength(198)});
        modeGroup.Children.Add(new TextBlock {Text=UiLanguage.Text("故障类型"),Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["EffectsParameterLabel"]});
        Grid.SetColumn(mode,1);modeGroup.Children.Add(mode);parameters.Children.Add(modeGroup);
        void AddParameter(Slider slider,string label,string unit,int precision)
        {
            slider.Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["HomeSlider"];
            AutomationProperties.SetName(slider,$"{(number==1?UiLanguage.Text("主故障"):UiLanguage.Text("小故障 ")+(number-1))} · {label}");
            var row=new Grid {ColumnSpacing=16,MinHeight=44};
            row.ColumnDefinitions.Add(new() {Width=new GridLength(128)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=new GridLength(198)});
            row.Children.Add(new TextBlock {Text=label,Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["EffectsParameterLabel"]});
            Grid.SetColumn(slider,1);row.Children.Add(slider);
            var input=new EffectValueInput {Target=slider,Unit=unit,Precision=precision,Width=198,HorizontalAlignment=HorizontalAlignment.Right};
            Grid.SetColumn(input,2);row.Children.Add(input);parameters.Children.Add(row);
        }
        AddParameter(strength,UiLanguage.Text("强度"),"%",0);AddParameter(rate,UiLanguage.Text("频率"),UiLanguage.Text("次/秒"),1);AddParameter(coverage,UiLanguage.Text("局部填充"),"%",0);
        AddParameter(size,UiLanguage.Text("内部条带尺寸"),"px",1);AddParameter(rgb,UiLanguage.Text("RGB 偏移"),"px",1);
        Content=new ContentControl {Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["EffectsSection"],Content=section};
        enabled.Toggled+=(_,_)=>Read();mode.SelectionChanged+=(_,_)=>Read();
        foreach(var box in new[]{strength,rate,coverage,size,rgb})box.ValueChanged+=(_,_)=>Read();
    }
    public void Load(GlitchLayer value)
    {
        loading=true;Value=value;enabled.IsOn=value.Enabled;mode.SelectedIndex=value.Mode;strength.Value=value.Strength*100;
        rate.Value=value.Rate;coverage.Value=value.Coverage*100;size.Value=value.Size;rgb.Value=value.RgbOffset;loading=false;Header();
    }
    private void Read()
    {
        if(loading)return;
        double ReadNumber(Slider box,double fallback)=>double.IsFinite(box.Value)?Math.Clamp(box.Value,box.Minimum,box.Maximum):fallback;
        Value=new GlitchLayer {Enabled=enabled.IsOn,Mode=Math.Clamp(mode.SelectedIndex,0,3),Strength=ReadNumber(strength,Value.Strength*100)/100,
            Rate=ReadNumber(rate,Value.Rate),Coverage=ReadNumber(coverage,Value.Coverage*100)/100,Size=ReadNumber(size,Value.Size),RgbOffset=ReadNumber(rgb,Value.RgbOffset)};
        Header();Changed?.Invoke();
    }
    private void Header()
    {
        heading.Text=number==1?UiLanguage.Text("主故障"):UiLanguage.Format($"小故障 {number-1}");
        mode.IsEnabled=enabled.IsOn;
        foreach(var slider in new[]{strength,rate,coverage,size,rgb})slider.IsEnabled=enabled.IsOn;
    }
}
