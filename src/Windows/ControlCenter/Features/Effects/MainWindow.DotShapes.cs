using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private ComboBox dotShapePicker=null!,rippleShapePicker=null!;
    private Slider dotRotationSlider=null!,rippleRotationSlider=null!;
    private void InitializeDotShapes()
    {
        (ComboBox,Slider) Build(StackPanel host) {
            var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(UiLanguage.Text("点阵形状"),"UtilityHeading"));
            var picker=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=new[]{UiLanguage.Text("圆形"),UiLanguage.Text("方形"),UiLanguage.Text("菱形"),UiLanguage.Text("三角形"),UiLanguage.Text("六边形"),UiLanguage.Text("五角星"),UiLanguage.Text("十字"),UiLanguage.Text("圆环")},SelectedIndex=0};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker,UiLanguage.Text("点阵形状"));panel.Children.Add(picker);
            var rotation=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=360,StepFrequency=1};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rotation,UiLanguage.Text("点阵旋转角度"));panel.Children.Add(NumericSliderRow(UiLanguage.Text("旋转角度"),rotation,"°",0));
            
            var card=UtilityCard(panel);
            host.Children.Insert(0,card);
            picker.SelectionChanged+=(_,_)=> {if(picker.SelectedIndex>=0)DraftChanged();};rotation.ValueChanged+=(_,_)=>DraftChanged();return(picker,rotation);
        }
        (dotShapePicker,dotRotationSlider)=Build(EffectsDotsEditor);
        (rippleShapePicker,rippleRotationSlider)=Build(EffectsRippleEditor);
    }
}
