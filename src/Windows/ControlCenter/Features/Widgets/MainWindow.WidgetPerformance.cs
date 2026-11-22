using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildPerformanceSettings(WidgetInstance widget)
    {
        var panel=new StackPanel {Spacing=ParameterUi.FieldGap};panel.Children.Add(UtilityText(UiLanguage.Text("指标排列"),"UtilityHeading"));
        var body=new Grid {ColumnSpacing=ParameterUi.FieldGap,RowSpacing=ParameterUi.FieldGap};
        var layout=new PerformanceLayoutPicker();var order=new OrderedTileList(preview:true,
            content:(item,source)=>ChoiceCardContent(item.Name,MetricCardIcon(Enum.Parse<PerformanceMetrics>(item.Id),source)));
        body.Children.Add(layout);body.Children.Add(order);panel.Children.Add(body);WidgetEditorPanel.Children.Add(UtilityCard(panel));
        bool? wasWide=null;
        void Arrange(double width) {
            bool wide=width>=720;if(wasWide==wide)return;wasWide=wide;
            body.ColumnDefinitions.Clear();body.RowDefinitions.Clear();
            body.ColumnDefinitions.Add(new() {Width=wide?new GridLength(280):new GridLength(1,GridUnitType.Star)});
            body.RowDefinitions.Add(new() {Height=GridLength.Auto});
            if(wide)body.ColumnDefinitions.Add(new());else body.RowDefinitions.Add(new() {Height=GridLength.Auto});
            Grid.SetColumn(order,wide?1:0);Grid.SetRow(order,wide?0:1);
        }
        body.SizeChanged+=(_,e)=>Arrange(e.NewSize.Width);Arrange(body.ActualWidth);
        layout.Changed+=(flow,columns)=> {if(!syncingWidgetEditor)LiveWidget(widget.Id,w=>w with {Performance=w.Performance with {Flow=flow,Columns=columns}});};
        layout.InteractionCompleted+=()=>widgets?.FlushPendingSave();
        order.Reordered+=(source,target)=>ScrubNumberInput.RunTransaction(order,()=>LiveWidget(widget.Id,w=> {
            var fields=w.Performance.Selected(w.Metrics).ToList();
            int from=fields.FindIndex(x=>x.ToString()==source),to=fields.FindIndex(x=>x.ToString()==target);
            if(from<0||to<0||from==to)return w;
            var metric=fields[from];fields.RemoveAt(from);fields.Insert(to,metric);
            return w with {Performance=w.Performance with {Order=fields.Concat(w.Performance.Order).Distinct().ToArray()}};
        }));
        return ()=> {
            var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==widget.Id);if(current==null)return;
            var fields=current.Performance.Selected(current.Metrics);
            layout.Update(current.Performance.Flow,current.Performance.Columns,fields.Length);
            order.UpdateArrangement(current.Performance.Flow,current.Performance.Columns);
            order.Update(fields.Select(m=>new OrderedTileList.Item(m.ToString(),PerformanceFields.Label(m))));
        };
    }
}
