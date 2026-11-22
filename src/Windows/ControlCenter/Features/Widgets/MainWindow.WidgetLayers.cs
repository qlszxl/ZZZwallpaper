using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,ScrubNumberInput> widgetLayerInputs=new();
    private ScrubNumberInput WidgetLayerInput(WidgetInstance widget)
    {
        var input=new ScrubNumberInput(UiLanguage.Text("组件层级"),true) {Width=100,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};input.Configure(-999,999,widget.Layer,1,0,"");
        input.ValueChanged+=value=>LiveWidget(widget.Id,w=>w with {Layer=(int)Math.Round(value)});
        input.InteractionCompleted+=()=>widgets?.FlushPendingSave();widgetLayerInputs[widget.Id]=input;return input;
    }
    private Grid WidgetLayerRow(WidgetInstance widget)
    {
        var row=new Grid {ColumnSpacing=16,MinHeight=40};
        row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(UiLanguage.Text("层级"),"UtilityLabel");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
        var input=WidgetLayerInput(widget);input.AttachDragHandle(label);Grid.SetColumn(input,1);row.Children.Add(input);return row;
    }
    private static Grid CreateWidgetOverviewCard(Button button,MenuFlyout menu)
    {
        // Reserve the same footer space for every card so groups and widgets
        // share both their outer dimensions and the icon/title position.
        const double height=164;
        button.Height=height;button.Padding=new Thickness(18,12,18,62);
        var card=new Grid {Height=height,VerticalAlignment=VerticalAlignment.Top};
        card.ContextFlyout=button.ContextFlyout=menu;card.Children.Add(button);return card;
    }
    private FrameworkElement WidgetLayerCard(Button button,WidgetInstance widget)
    {
        var card=CreateWidgetOverviewCard(button,WidgetCardMenu(widget.Id));
        var footer=WidgetCardFooter(widget);footer.Margin=new Thickness(20,0,20,12);footer.VerticalAlignment=VerticalAlignment.Bottom;
        card.Children.Add(footer);button.Padding=new Thickness(18,12,18,62);AddCardHelp(card,WidgetTitle(widget.Kind));return card;
    }
    private FrameworkElement WidgetGroupCard(Button button,WidgetLayoutGroup group)=>CreateWidgetOverviewCard(button,WidgetGroupCardMenu(group.Id));
    private void SyncWidgetLayers()
    {
        if(widgets==null)return;
        foreach(var pair in widgetLayerInputs) {
            var widget=widgets.State.Widgets.FirstOrDefault(w=>w.Id==pair.Key);
            if(widget!=null&&!pair.Value.IsInteracting&&pair.Value.Value!=widget.Layer)pair.Value.Configure(-999,999,widget.Layer,1,0,"");
        }
    }
}
