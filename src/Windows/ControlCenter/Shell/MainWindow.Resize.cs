using System.Runtime.InteropServices;
using LumaWall.Contracts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly Canvas shellResizeLayer=new();
    private readonly Dictionary<int,ShellResizeGrip> shellResizeGrips=new();
    private RectInt32[] shellResizeInputRects=[];
    private (double Width,double Height,double Top,double Scale,bool Enabled)? shellResizeLayout;
    private sealed record ShellResizeDrag(ShellResizeGrip Grip,Pointer Pointer,ShellResizePoint Start,RectInt32 Bounds,int MouseButton);
    private ShellResizeDrag? shellResizeDrag;
    private DispatcherQueueTimer? shellResizeReleaseGuard;
    [StructLayout(LayoutKind.Sequential)] private struct ShellResizePoint {public int X,Y;}
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ShellResizePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index,uint dpi);

    // XAML content has its own input HWND. These transparent, local controls
    // receive its pointer events instead of relying only on the parent's hit test.
    private sealed class ShellResizeGrip : Grid,IDisposable
    {
        private readonly InputSystemCursor cursor;
        internal int Hit {get;}
        internal ShellResizeGrip(int hit,Action<ShellResizeGrip,PointerRoutedEventArgs> pressed)
        {
            Hit=hit;
            cursor=InputSystemCursor.Create(hit switch {
                10 or 11=>InputSystemCursorShape.SizeWestEast,
                12 or 15=>InputSystemCursorShape.SizeNorthSouth,
                13 or 17=>InputSystemCursorShape.SizeNorthwestSoutheast,
                _=>InputSystemCursorShape.SizeNortheastSouthwest});
            ProtectedCursor=cursor;
            var transparent=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            if(hit is 13 or 14 or 16 or 17)
            {
                // Chamfer the inner corner to avoid stealing caption-button or
                // content clicks. The native window region clips the outer arc.
                var points=new[]{new Point(0,0),new Point(24,0),new Point(24,8),new Point(8,24),new Point(0,24)};
                for(int i=0;i<points.Length;i++)points[i]=new Point(hit is 14 or 17?24-points[i].X:points[i].X,hit is 16 or 17?24-points[i].Y:points[i].Y);
                var figure=new PathFigure {StartPoint=points[0],IsClosed=true,IsFilled=true};
                foreach(var point in points.Skip(1))figure.Segments.Add(new LineSegment {Point=point});
                var geometry=new PathGeometry();geometry.Figures.Add(figure);
                Children.Add(new Microsoft.UI.Xaml.Shapes.Path {Data=geometry,Fill=transparent});
            }
            else Background=transparent;
            AutomationProperties.SetAccessibilityView(this,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            PointerPressed+=(_,args)=>pressed(this,args);
        }
        public void Dispose() {ProtectedCursor=null;cursor.Dispose();}
    }

    private void InitializeShellResizeGrips()
    {
        if(AppWindow.Presenter is OverlappedPresenter presenter)presenter.IsResizable=true;
        Grid.SetRowSpan(shellResizeLayer,2);Canvas.SetZIndex(shellResizeLayer,10000);
        foreach(int hit in new[]{10,11,12,15,13,14,16,17})
        {
            var grip=new ShellResizeGrip(hit,ShellResize_Pressed);
            grip.PointerMoved+=ShellResize_Moved;
            grip.PointerReleased+=ShellResize_Released;
            grip.PointerCanceled+=ShellResize_Canceled;
            grip.PointerCaptureLost+=ShellResize_CaptureLost;
            shellResizeGrips.Add(hit,grip);shellResizeLayer.Children.Add(grip);
        }
        Root.Children.Add(shellResizeLayer);
        Root.LayoutUpdated+=ShellResize_LayoutUpdated;
        Root.Unloaded+=(_,_)=>EndShellResize("Unloaded");
        Root.AddHandler(UIElement.PreviewKeyDownEvent,new KeyEventHandler((_,args)=> {
            if(shellResizeDrag==null||args.Key!=Windows.System.VirtualKey.Escape)return;
            args.Handled=true;EndShellResize("Escape",restore:true);
        }),true);
        Activated+=(_,args)=> {if(args.WindowActivationState==WindowActivationState.Deactivated)EndShellResize("Deactivated");};
        shellResizeReleaseGuard=DispatcherQueue.CreateTimer();
        shellResizeReleaseGuard.Interval=TimeSpan.FromMilliseconds(32);
        shellResizeReleaseGuard.IsRepeating=true;
        shellResizeReleaseGuard.Tick+=(_,_)=> {
            if(shellResizeDrag is not { } drag)return;
            if((GetAsyncKeyState(0x1B)&0x8000)!=0)EndShellResize("Escape",restore:true);
            else if(!CanContinueShellResize())EndShellResize("WindowState");
            else if((GetAsyncKeyState(drag.MouseButton)&0x8000)==0)EndShellResize("ButtonUp");
        };
        Closed+=(_,_)=> {
            EndShellResize("Closed");
            shellResizeReleaseGuard?.Stop();shellResizeReleaseGuard=null;
            Root.LayoutUpdated-=ShellResize_LayoutUpdated;
            shellResizeInputRects=[];
            foreach(var grip in shellResizeGrips.Values)grip.Dispose();
        };
    }

    private void ShellResize_LayoutUpdated(object? sender,object args)
    {
        if(shellChromeClosed||!Root.IsLoaded||Root.XamlRoot is not { } root||Root.ActualWidth<=0||Root.ActualHeight<=0)return;
        double width=Root.ActualWidth,height=Root.ActualHeight;
        // The header is in the root's first row; its inset is the only offset.
        double top=Math.Clamp(ShellHeaderBorder.Margin.Top,0,Math.Max(0,height-1));
        bool enabled=AppWindow.Presenter is OverlappedPresenter {IsResizable:true,State:OverlappedPresenterState.Restored};
        var layout=(width,height,top,root.RasterizationScale*interfaceScale,enabled);
        if(shellResizeLayout==layout)return;
        shellResizeLayout=layout;shellResizeLayer.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
        var rectangles=new List<RectInt32>();
        if(enabled)
        {
            void Place(int hit,double x,double y,double w,double h)
            {
                double right=Math.Clamp(x+Math.Max(0,w),0,width),bottom=Math.Clamp(y+Math.Max(0,h),0,height);
                x=Math.Clamp(x,0,right);y=Math.Clamp(y,0,bottom);w=right-x;h=bottom-y;
                var grip=shellResizeGrips[hit];Canvas.SetLeft(grip,x);Canvas.SetTop(grip,y);
                grip.Width=w;grip.Height=h;grip.Visibility=w>0&&h>0?Visibility.Visible:Visibility.Collapsed;
                if(w<=0||h<=0)return;
                double scale=root.RasterizationScale*interfaceScale;int left=(int)Math.Floor(x*scale),upper=(int)Math.Floor(y*scale);
                rectangles.Add(new RectInt32(left,upper,(int)Math.Ceiling((x+w)*scale)-left,(int)Math.Ceiling((y+h)*scale)-upper));
            }
            Place(10,0,top+24,8,height-top-48);Place(11,width-8,top+24,8,height-top-48);
            Place(12,24,top,width-48,8);Place(15,24,height-8,width-48,8);
            Place(13,0,top,24,24);Place(14,width-24,top,24,24);
            Place(16,0,height-24,24,24);Place(17,width-24,height-24,24,24);
        }
        shellResizeInputRects=rectangles.ToArray();
        UpdateShellInputRegions();
    }

    private void ShellResize_Pressed(ShellResizeGrip grip,PointerRoutedEventArgs args)
    {
        if(shellResizeDrag!=null||!CanContinueShellResize()||args.Pointer.PointerDeviceType!=PointerDeviceType.Mouse||
            !args.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)return;
        args.Handled=true;
        // Check the current physical state too: a queued press must never start
        // resizing after the user has already released the button.
        int mouseButton=GetSystemMetrics(23)!=0?2:1; // SM_SWAPBUTTON
        if((GetAsyncKeyState(mouseButton)&0x8000)==0||!GetCursorPos(out var point))return;
        var position=AppWindow.Position;var size=AppWindow.Size;
        if(!grip.CapturePointer(args.Pointer))return;
        shellResizeDrag=new(grip,args.Pointer,point,new RectInt32(position.X,position.Y,size.Width,size.Height),mouseButton);
        shellResizeReleaseGuard?.Start();
        AppLog.Info("WindowResize",$"Started; hit={grip.Hit}; size={size.Width}x{size.Height}");
    }

    private bool CanContinueShellResize()=>!shellChromeClosed&&Root.IsLoaded&&AppWindow.IsVisible&&
        AppWindow.Presenter is OverlappedPresenter {IsResizable:true,State:OverlappedPresenterState.Restored};

    private void ShellResize_Moved(object sender,PointerRoutedEventArgs args)
    {
        if(shellResizeDrag is not { } drag||args.Pointer.PointerId!=drag.Pointer.PointerId)return;
        args.Handled=true;
        if(!CanContinueShellResize()) {EndShellResize("WindowState");return;}
        if(!args.GetCurrentPoint(drag.Grip).Properties.IsLeftButtonPressed||(GetAsyncKeyState(drag.MouseButton)&0x8000)==0)
        {EndShellResize("ButtonUp");return;}
        if((GetAsyncKeyState(0x1B)&0x8000)!=0) {EndShellResize("Escape",restore:true);return;}
        if(!GetCursorPos(out var point)) {EndShellResize("PointerUnavailable");return;}

        // Anchor every move to the initial outer rectangle in physical screen
        // pixels. Local grip coordinates move with the window and would drift.
        int dx=point.X-drag.Start.X,dy=point.Y-drag.Start.Y,hit=drag.Grip.Hit;
        var bounds=drag.Bounds;
        uint dpi=(uint)Math.Max(96,Math.Round((Root.XamlRoot?.RasterizationScale??1)*96));
        int minWidth=Math.Max(1,GetSystemMetricsForDpi(34,dpi)),minHeight=Math.Max(1,GetSystemMetricsForDpi(35,dpi));
        int left=bounds.X,top=bounds.Y,right=left+bounds.Width,bottom=top+bounds.Height;
        if(hit is 10 or 13 or 16)left=Math.Min(left+dx,right-minWidth);
        if(hit is 11 or 14 or 17)right=Math.Max(right+dx,left+minWidth);
        if(hit is 12 or 13 or 14)top=Math.Min(top+dy,bottom-minHeight);
        if(hit is 15 or 16 or 17)bottom=Math.Max(bottom+dy,top+minHeight);
        var position=AppWindow.Position;var size=AppWindow.Size;
        if(position.X!=left||position.Y!=top||size.Width!=right-left||size.Height!=bottom-top)
            AppWindow.MoveAndResize(new RectInt32(left,top,right-left,bottom-top));
    }

    private void ShellResize_Released(object sender,PointerRoutedEventArgs args)
    {
        if(shellResizeDrag is not { } drag||args.Pointer.PointerId!=drag.Pointer.PointerId)return;
        args.Handled=true;
        if(!args.GetCurrentPoint(drag.Grip).Properties.IsLeftButtonPressed)EndShellResize("Released");
    }
    private void ShellResize_Canceled(object sender,PointerRoutedEventArgs args)
    {
        if(shellResizeDrag?.Pointer.PointerId!=args.Pointer.PointerId)return;
        args.Handled=true;EndShellResize("Canceled");
    }
    private void ShellResize_CaptureLost(object sender,PointerRoutedEventArgs args)
    {
        if(shellResizeDrag?.Pointer.PointerId!=args.Pointer.PointerId)return;
        args.Handled=true;EndShellResize("CaptureLost");
    }
    private void EndShellResize(string reason,bool restore=false)
    {
        if(shellResizeDrag is not { } drag)return;
        // Clear state before releasing capture, which can synchronously raise
        // PointerCaptureLost. Nothing can resume this drag without a new press.
        shellResizeDrag=null;shellResizeReleaseGuard?.Stop();
        drag.Grip.ReleasePointerCapture(drag.Pointer);
        if(restore&&CanContinueShellResize())AppWindow.MoveAndResize(drag.Bounds);
        AppLog.Info("WindowResize",$"Stopped; hit={drag.Grip.Hit}; reason={reason}");
    }
}
