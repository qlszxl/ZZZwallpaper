using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    // Sharing and metric selection use the same card, content and grid rules.
    // Only their callers own selection/persistence events.
    private static Grid ChoiceCardContent(string text,FrameworkElement glyph)
    {
        var content=new Grid {ColumnSpacing=ParameterUi.TileGap};content.ColumnDefinitions.Add(new() {Width=new GridLength(24)});content.ColumnDefinitions.Add(new());
        glyph.Opacity=.8;content.Children.Add(glyph);
        var label=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,TextLineBounds=TextLineBounds.Full,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(label,1);content.Children.Add(label);return content;
    }
    private static CheckBox SelectionCard(string text,bool selected,Func<Control,FrameworkElement> icon)
    {
        var check=new CheckBox {Style=HomeStyle("SharingSelectionCheckBox"),IsChecked=selected,HorizontalAlignment=HorizontalAlignment.Stretch};
        check.Content=ChoiceCardContent(text,icon(check));AutomationProperties.SetName(check,text);
        return check;
    }
    private static void LayoutChoiceCards(Grid tiles,int maximumColumns=5,double minimumTileWidth=216,bool bounded=false)
    {
        int columns=tiles.Children.Any(child=>ParameterInputGate.Unwrap((FrameworkElement)child) is CheckBox||child is Grid group&&group.Children.OfType<CheckBox>().Any())
            ?Math.Clamp((int)Math.Floor((tiles.ActualWidth+ParameterUi.TileGap)/(minimumTileWidth+ParameterUi.TileGap)),1,maximumColumns):1;
        FlowCards(tiles,columns);
        if(bounded)foreach(var column in tiles.ColumnDefinitions)column.MaxWidth=280;
    }
    private static Grid ChoiceCardGrid(int maximumColumns=5,double minimumTileWidth=216,bool bounded=false)
    {
        var tiles=new Grid {ColumnSpacing=ParameterUi.TileGap,RowSpacing=ParameterUi.TileGap};
        tiles.SizeChanged+=(_,_)=>LayoutChoiceCards(tiles,maximumColumns,minimumTileWidth,bounded);
        tiles.Loaded+=(_,_)=>LayoutChoiceCards(tiles,maximumColumns,minimumTileWidth,bounded);
        return tiles;
    }
    private static FrameworkElement MetricCardIcon(PerformanceMetrics metric,DependencyObject source)=>UtilityIcon(PerformanceFields.Category(metric) switch {
        "CPU"=>"Processor",var category when category==UiLanguage.Text("内存")=>"Memory","GPU"=>metric is PerformanceMetrics.GpuMemory or PerformanceMetrics.GpuMemoryTotal or PerformanceMetrics.GpuMemoryClock?"Memory":"Graphics",
        var category when category==UiLanguage.Text("磁盘")=>"Storage",var category when category==UiLanguage.Text("网络")=>metric==PerformanceMetrics.NetworkSend?"Up":"Down",_=>"Processor"
    },source,24);
}
