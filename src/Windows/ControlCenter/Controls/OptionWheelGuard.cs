using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

// Options change by an explicit click, drag or keyboard action. Intercept the
// bubbling wheel on the template child before the owning control's native
// class handler can alter a value, and continue scrolling the containing page.
internal sealed class OptionWheelGuard
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,OptionWheelGuard> states=new();
    private readonly Control owner;
    private readonly PointerEventHandler handler;
    private readonly long templateToken;
    private UIElement? surface;
    private bool disposed;

    internal static void SetEnabled(Control control,bool enabled)
    {
        if(states.TryGetValue(control,out var existing)) {
            if(enabled)return;
            existing.Dispose();states.Remove(control);return;
        }
        if(enabled)states.Add(control,new OptionWheelGuard(control));
    }

    private OptionWheelGuard(Control control)
    {
        owner=control;handler=Wheel;
        owner.Loaded+=Loaded;owner.Unloaded+=Unloaded;
        templateToken=owner.RegisterPropertyChangedCallback(Control.TemplateProperty,(_,_)=> {
            if(owner.IsLoaded)owner.DispatcherQueue.TryEnqueue(Connect);
        });
        if(owner.IsLoaded)Connect();
    }
    private void Loaded(object sender,RoutedEventArgs e)=>Connect();
    private void Unloaded(object sender,RoutedEventArgs e)=>Disconnect();
    private void Connect()
    {
        if(disposed||!owner.IsLoaded)return;
        owner.ApplyTemplate();
        var next=VisualTreeHelper.GetChildrenCount(owner)>0?VisualTreeHelper.GetChild(owner,0) as UIElement:null;
        if(ReferenceEquals(next,surface))return;
        Disconnect();surface=next;
        surface?.AddHandler(UIElement.PointerWheelChangedEvent,handler,true);
    }
    private void Disconnect()
    {
        surface?.RemoveHandler(UIElement.PointerWheelChangedEvent,handler);surface=null;
    }
    private void Dispose()
    {
        disposed=true;
        Disconnect();owner.Loaded-=Loaded;owner.Unloaded-=Unloaded;
        owner.UnregisterPropertyChangedCallback(Control.TemplateProperty,templateToken);
    }
    private void Wheel(object sender,PointerRoutedEventArgs e)
    {
        var properties=e.GetCurrentPoint(owner).Properties;
        int delta=properties.MouseWheelDelta;
        if(delta==0)return;
        bool horizontal=properties.IsHorizontalMouseWheel;
        // An open dropdown owns a separate scrolling list. Its native wheel
        // scrolling must remain intact; this handler only guards the field.
        for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=owner;node=VisualTreeHelper.GetParent(node))
            if(node is ScrollViewer inner&&(horizontal?inner.ScrollableWidth:inner.ScrollableHeight)>0)return;
        e.Handled=true;
        ScrollContainer(owner,delta,horizontal);
    }
    internal static void ScrollContainer(Control owner,int delta,bool horizontal=false)
    {
        int lines=System.Windows.Forms.SystemInformation.MouseWheelScrollLines;
        if(lines==0)return;
        for(var node=VisualTreeHelper.GetParent(owner);node!=null;node=VisualTreeHelper.GetParent(node)) {
            if(node is not ScrollViewer viewer)continue;
            if(horizontal?viewer.HorizontalScrollMode==ScrollMode.Disabled:viewer.VerticalScrollMode==ScrollMode.Disabled)continue;
            double viewport=horizontal?viewer.ViewportWidth:viewer.ViewportHeight;
            double distance=delta/120d*(lines<0?viewport:lines*16d)*(horizontal?1:-1);
            double offset=horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
            double limit=horizontal?viewer.ScrollableWidth:viewer.ScrollableHeight;
            double target=Math.Clamp(offset+distance,0,limit);
            if(Math.Abs(target-offset)<.01)continue;
            viewer.ChangeView(horizontal?target:null,horizontal?null:target,null,true);
            return;
        }
    }
}
