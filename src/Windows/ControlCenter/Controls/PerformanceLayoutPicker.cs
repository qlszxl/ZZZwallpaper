using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

// The performance editor has a compact column control. Group editors retain
// their existing matrix picker and member navigation.
internal sealed class PerformanceLayoutPicker : StackPanel
{
    private readonly ScrubNumberInput columnInput=new(UiLanguage.Text("指标网格列数"));
    private readonly Grid columnField=new() {ColumnSpacing=8};
    private readonly Button fewer,more;
    private readonly TextBlock summary=new() {Style=StyleOf("UtilityLabel")};
    private int columns=2;
    private bool synchronizing;
    internal event Action<WidgetGroupFlow,int>? Changed;
    internal event Action? InteractionCompleted;
    private static Style StyleOf(string key)=>(Style)Microsoft.UI.Xaml.Application.Current.Resources[key];
    internal PerformanceLayoutPicker()
    {
        Spacing=ParameterUi.FieldGap;
        columnField.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        columnField.ColumnDefinitions.Add(new());
        columnField.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        columnField.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=new TextBlock {Text=UiLanguage.Text("列数"),Style=StyleOf("UtilityLabel"),VerticalAlignment=VerticalAlignment.Center};
        columnInput.AttachDragHandle(label);columnField.Children.Add(label);
        Grid.SetColumn(columnInput,1);columnField.Children.Add(columnInput);
        Button Step(string text,string name,int delta,int column) {
            var button=new Button {Style=StyleOf("UtilityButton"),Content=text,MinWidth=44,Width=44,Height=44,MinHeight=44,Padding=new Thickness(0)};
            AutomationProperties.SetName(button,name);Grid.SetColumn(button,column);columnField.Children.Add(button);
            button.Click+=(_,_)=>ScrubNumberInput.RunTransaction(this,()=>Changed?.Invoke(WidgetGroupFlow.Grid,Math.Clamp(columns+delta,1,8)));
            return button;
        }
        fewer=Step("−",UiLanguage.Text("减少指标列数"),-1,2);more=Step("+",UiLanguage.Text("增加指标列数"),1,3);
        columnInput.ValueChanged+=value=> {if(!synchronizing)Changed?.Invoke(WidgetGroupFlow.Grid,(int)value);};
        columnInput.InteractionCompleted+=()=>InteractionCompleted?.Invoke();
        Children.Add(columnField);Children.Add(summary);
    }
    internal void Update(WidgetGroupFlow value,int columnCount,int itemCount)
    {
        synchronizing=true;
        try {
            int count=Math.Max(0,itemCount);columns=Math.Clamp(value==WidgetGroupFlow.Horizontal?Math.Max(1,count):value==WidgetGroupFlow.Vertical?1:columnCount,1,8);
            columnInput.Configure(1,8,columns,1,0,"");fewer.IsEnabled=columns>1;more.IsEnabled=columns<8;
            int actual=Math.Max(1,new PerformanceWidgetOptions {Flow=WidgetGroupFlow.Grid,Columns=columns}.ColumnCount(count));
            summary.Text=count==0?UiLanguage.Text("暂无指标"):UiLanguage.Format($"{actual} 列 × {(count+actual-1)/actual} 行");
        } finally {synchronizing=false;}
    }
}
