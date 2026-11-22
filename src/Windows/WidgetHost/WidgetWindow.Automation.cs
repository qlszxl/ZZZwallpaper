using System.Text.Json;
using W=System.Windows;

namespace LumaWall.WidgetHost;

internal sealed partial class WidgetWindow
{
    internal JsonElement AutomationTelemetry()
    {
        UpdateLayout();GetWindowRect(hwnd,out var window);
        object Bounds(W.FrameworkElement element) {
            var origin=element.PointToScreen(new W.Point(0,0));var end=element.PointToScreen(new W.Point(element.ActualWidth,element.ActualHeight));
            return new {x=origin.X,y=origin.Y,width=end.X-origin.X,height=end.Y-origin.Y};
        }
        return JsonSerializer.SerializeToElement(new {
            widgetId=instance.Id,window=hwnd.ToInt64(),capturedAt=DateTimeOffset.UtcNow,visible=IsVisible,coordinateSpace="physical_desktop_pixels",
            positionLocked=editingLocked,interactionLocked,dragLocked,mousePassThrough,
            windowBounds=new {x=window.Left,y=window.Top,width=window.Right-window.Left,height=window.Bottom-window.Top},
            drawingBounds=timeArtwork!=null?Bounds(timeArtwork):Content is W.FrameworkElement content?Bounds(content):null,
            buttons=timeButtons.Select((button,index)=>new {controlId="widget:"+instance.Id+":"+index,name=W.Automation.AutomationProperties.GetName(button),visible=button.IsVisible,enabled=button.IsEnabled,bounds=button.IsVisible?Bounds(button):null}).ToArray()
        });
    }
}
