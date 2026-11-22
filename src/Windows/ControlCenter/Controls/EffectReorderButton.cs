using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace LumaWall.ControlCenter;

// Own the primary pointer before ButtonBase consumes it for Click. This is an
// in-page rearrangement gesture, not an OLE/data-transfer drag operation.
internal sealed class EffectReorderButton : Button
{
    internal UIElement? CoordinateRoot {get;set;}
    internal Action? Activate {get;set;}
    internal Func<Point,bool>? BeginDrag {get;set;}
    internal Action<Point>? MoveDrag {get;set;}
    internal Action<bool>? EndDrag {get;set;}
    internal Func<Point?>? SampleMouseDrag {get;set;}
    internal bool IsDragging=>dragging;
    internal bool IsMouseDragging=>dragging&&mousePointer;
    private Pointer? pointer;
    private uint pointerId;
    private Point origin,last;
    private bool dragging,dragAttempted,mousePointer;
    private readonly DispatcherTimer hold=new() {Interval=TimeSpan.FromMilliseconds(280)};

    internal EffectReorderButton()
    {
        ManipulationMode=ManipulationModes.None;
        Click+=(_,_)=> {if(pointer==null)Activate?.Invoke();}; // Keyboard and UIA.
        hold.Tick+=(_,_)=> {hold.Stop();StartDrag();};
        Unloaded+=(_,_)=>CancelGesture();
        IsEnabledChanged+=(_,_)=> {if(!IsEnabled)CancelGesture();};
        ContextRequested+=(_,e)=> {if(pointer!=null)e.Handled=true;};
    }
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(CoordinateRoot);
        var pressed=e.Pointer;
        bool mouse=pressed.PointerDeviceType==PointerDeviceType.Mouse;
        bool primary=!mouse||point.Properties.IsLeftButtonPressed;
        if(!IsEnabled||!primary||CoordinateRoot==null) {base.OnPointerPressed(e);return;}
        if(pointer!=null) {e.Handled=true;return;}
        origin=last=point.Position;dragAttempted=false;
        if(!CapturePointer(pressed)) {base.OnPointerPressed(e);return;}
        e.Handled=true;
        pointer=pressed;pointerId=pressed.PointerId;mousePointer=mouse;Focus(FocusState.Pointer);hold.Start();
        VisualStateManager.GoToState(this,"Pressed",true);
    }
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if(pointer==null||pointerId!=e.Pointer.PointerId) {base.OnPointerMoved(e);return;}
        e.Handled=true;
        // Captured mouse drags are sampled once per render frame. Do not create
        // PointerPoint/Properties wrappers for every high-frequency mouse event.
        if(IsMouseDragging&&SampleMouseDrag!=null)return;
        var point=e.GetCurrentPoint(CoordinateRoot);last=point.Position;
        if(mousePointer&&!point.Properties.IsLeftButtonPressed) {CancelGesture();return;}
        double dx=last.X-origin.X,dy=last.Y-origin.Y;
        if(!dragging&&dx*dx+dy*dy>=36)StartDrag();
        // Touch/pen retain routed coordinates; the render tick consumes the
        // latest value. Release below always flushes its exact final point.
    }
    private void StartDrag()
    {
        if(pointer==null||dragging||dragAttempted)return;hold.Stop();dragAttempted=true;
        dragging=BeginDrag?.Invoke(origin)==true;
        if(dragging)MoveDrag?.Invoke(last);
    }
    internal Point? ReadDragPosition()=>!dragging?null:IsMouseDragging&&SampleMouseDrag!=null?SampleMouseDrag():last;
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if(pointer==null||pointerId!=e.Pointer.PointerId) {base.OnPointerReleased(e);return;}
        e.Handled=true;var local=e.GetCurrentPoint(this).Position;
        bool tap=!dragAttempted&&local.X>=0&&local.Y>=0&&local.X<=ActualWidth&&local.Y<=ActualHeight;
        if(dragging)MoveDrag?.Invoke(e.GetCurrentPoint(CoordinateRoot).Position);
        Finish(true);if(tap&&IsEnabled)Activate?.Invoke();
    }
    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        if(pointer!=null&&pointerId==e.Pointer.PointerId) {e.Handled=true;CancelGesture();return;}
        base.OnPointerCanceled(e);
    }
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        if(pointer!=null&&pointerId==e.Pointer.PointerId) {e.Handled=true;CancelGesture();return;}
        base.OnPointerCaptureLost(e);
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if(e.Key==VirtualKey.Escape&&pointer!=null) {e.Handled=true;CancelGesture();return;}
        base.OnKeyDown(e);
    }
    internal void CancelGesture()=>Finish(false);
    private void Finish(bool commit)
    {
        hold.Stop();var captured=pointer;bool wasDragging=dragging;
        pointer=null;dragging=false;dragAttempted=false;mousePointer=false; // CaptureLost and Unloaded must not re-enter.
        if(captured!=null)ReleasePointerCapture(captured);
        VisualStateManager.GoToState(this,"Normal",true);
        if(wasDragging)EndDrag?.Invoke(commit);
    }
}
