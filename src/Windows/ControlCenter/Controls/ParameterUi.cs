using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

internal static class ParameterUi
{
    internal const double SectionPadding=24,HeaderGap=28,FieldGap=24,LabelGap=8,TileGap=16,TileRadius=20;
}

// A single wrapping rule for export choices and reorder tiles. Fewer items do
// not stretch into full-width bars; narrow panes still fit one complete tile.
public sealed class ChoiceTilePanel : Panel
{
    public double MinimumTileWidth {get;set;}=128;
    public double MaximumTileWidth {get;set;}=160;
    public double AspectRatio {get;set;}=1;
    private (int Columns,double Width,double Height) Geometry(double width)
    {
        if(!double.IsFinite(width))width=MaximumTileWidth*Math.Max(1,Children.Count)+ParameterUi.TileGap*Math.Max(0,Children.Count-1);
        int columns=Math.Max(1,(int)Math.Floor((width+ParameterUi.TileGap)/(MinimumTileWidth+ParameterUi.TileGap)));
        double cell=Math.Max(1,Math.Min(MaximumTileWidth,(width-ParameterUi.TileGap*(columns-1))/columns));
        return(columns,cell,Math.Max(64,cell/AspectRatio));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var g=Geometry(availableSize.Width);int count=0;
        foreach(var child in Children.Where(c=>c.Visibility==Visibility.Visible)){child.Measure(new(g.Width,g.Height));count++;}
        int rows=(count+g.Columns-1)/g.Columns;
        return new(double.IsFinite(availableSize.Width)?availableSize.Width:g.Columns*(g.Width+ParameterUi.TileGap)-ParameterUi.TileGap,rows==0?0:rows*(g.Height+ParameterUi.TileGap)-ParameterUi.TileGap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var g=Geometry(finalSize.Width);int index=0;
        foreach(var child in Children.Where(c=>c.Visibility==Visibility.Visible)){child.Arrange(new Rect(index%g.Columns*(g.Width+ParameterUi.TileGap),index/g.Columns*(g.Height+ParameterUi.TileGap),g.Width,g.Height));index++;}
        return finalSize;
    }
}
