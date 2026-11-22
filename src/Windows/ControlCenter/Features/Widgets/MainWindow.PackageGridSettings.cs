using LumaWall.Contracts;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildPackageGridSettings(WidgetInstance widget,PackageGrid grid)
    {
        var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(grid.Title,"UtilityHeading"));
        Slider Add(string title,int min,int max,int value,string unit,Func<GridWidgetOptions,int,GridWidgetOptions> change) {
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=max,StepFrequency=1,Value=value};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,title);
            panel.Children.Add(NumericSliderRow(title,slider,unit,0));
            slider.ValueChanged+=(_,_)=>LiveWidget(widget.Id,w=>w with {GridOptions=change(grid.Options(w),(int)Math.Round(slider.Value))});
            return slider;
        }
        var rows=Add(UiLanguage.Text("行数"),grid.MinRows,grid.MaxRows,grid.Options(widget).Rows,UiLanguage.Text("行"),(o,value)=>o with {Rows=value});
        var width=Add(UiLanguage.Text("宽度"),grid.MinWidth,grid.MaxWidth,grid.Options(widget).Width,"DIP",(o,value)=>o with {Width=value});
        WidgetEditorPanel.Children.Add(UtilityCard(panel));
        return ()=> {
            var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==widget.Id);if(current==null)return;
            rows.Value=grid.Options(current).Rows;width.Value=grid.Options(current).Width;
        };
    }
}
