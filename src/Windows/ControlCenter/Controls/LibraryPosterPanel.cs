using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

public sealed class LibraryPosterPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width=double.IsFinite(availableSize.Width)?Math.Max(0,availableSize.Width):320;
        var size=new Size(width,width*9/16);
        foreach(var child in Children)child.Measure(size);
        return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds=new Rect(0,0,finalSize.Width,finalSize.Width*9/16);
        foreach(var child in Children)child.Arrange(bounds);
        return new Size(bounds.Width,bounds.Height);
    }
}
