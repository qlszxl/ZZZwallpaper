using LumaWall.Contracts;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace LumaWall.ControlCenter;

// Hardware stays vector; a continuous hue texture avoids subpixel seams and
// moire from hundreds of SVG wedges/grooves. Marker and input remain live XAML.
internal sealed class DiscHueSelector : UserControl
{
    private readonly Canvas surface = new() { Width=460, Height=400 };
    private readonly Ellipse marker = new() { Width=22, Height=22, StrokeThickness=3, Stroke=new SolidColorBrush(Microsoft.UI.Colors.White), Fill=new SolidColorBrush(Microsoft.UI.Colors.Transparent), IsHitTestVisible=false };
    private readonly Ellipse markerEdge = new() { Width=26, Height=26, StrokeThickness=2, Stroke=new SolidColorBrush(Color.FromArgb(180,0,0,0)), IsHitTestVisible=false };
    private readonly Ellipse preview = new() { Width=40, Height=40, IsHitTestVisible=false };
    private readonly Image colorSurface = new() {
        Source=new BitmapImage(new Uri("ms-appx:///Assets/ColorPicker/DiscHue.png")),
        Width=358, Height=358, IsHitTestVisible=false
    };
    private readonly Ellipse shade = new() { Width=358, Height=358, Fill=new SolidColorBrush(Microsoft.UI.Colors.Black), IsHitTestVisible=false };
    private uint? capturedPointer;
    private double hue;
    internal event Action<double>? HueChanged;

    internal DiscHueSelector()
    {
        IsTabStop=true;
        InteractionFocus.SetEnabled(this,true);
        InteractionFocus.SetSuppressFeedback(this,true);
        UseSystemFocusVisuals=false;
        HorizontalAlignment=HorizontalAlignment.Stretch;
        ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.Hand);
        surface.Children.Add(new Image {
            Source=new SvgImageSource(new Uri("ms-appx:///Assets/ColorPicker/DiscDrive.svg")),
            Width=460, Height=400, IsHitTestVisible=false
        });
        var white=new Ellipse {Width=358,Height=358,Fill=new SolidColorBrush(Microsoft.UI.Colors.White),IsHitTestVisible=false};
        Canvas.SetLeft(white,51);Canvas.SetTop(white,21);surface.Children.Add(white);
        Canvas.SetLeft(colorSurface,51);Canvas.SetTop(colorSurface,21);surface.Children.Add(colorSurface);
        Canvas.SetLeft(shade,51);Canvas.SetTop(shade,21);surface.Children.Add(shade);
        surface.Children.Add(new Image {
            Source=new SvgImageSource(new Uri("ms-appx:///Assets/ColorPicker/DiscHub.svg")),
            Width=460, Height=400, IsHitTestVisible=false
        });
        surface.Children.Add(preview);Canvas.SetLeft(preview,210);Canvas.SetTop(preview,180);
        surface.Children.Add(markerEdge);surface.Children.Add(marker);
        surface.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Content=new Viewbox { Child=surface, Stretch=Stretch.Uniform };
        AutomationProperties.SetHelpText(this,UiLanguage.Text("拖动光盘选择色相；方向键微调，Home 回到红色。"));
        
        PointerPressed+=Pressed;
        PointerMoved+=Moved;
        PointerReleased+=(_,e)=> {
            if(capturedPointer!=e.Pointer.PointerId)return;
            Select(e.GetCurrentPoint(surface).Position);
            capturedPointer=null;ReleasePointerCapture(e.Pointer);e.Handled=true;
        };
        PointerCanceled+=(_,_)=>ReleaseDisc();
        PointerCaptureLost+=(_,_)=>capturedPointer=null;
        Unloaded+=(_,_)=>ReleaseDisc();
        KeyDown+=(_,e)=> {
            double? next=e.Key switch {
                VirtualKey.Left or VirtualKey.Down=>hue-1,
                VirtualKey.Right or VirtualKey.Up=>hue+1,
                VirtualKey.Home=>0,
                VirtualKey.End=>359,
                _=>null
            };
            if(next is not double value)return;
            ChangeHue(value);e.Handled=true;
        };
    }

    internal void Update(double degrees,double saturation,double value,Color color)
    {
        hue=(degrees%360+360)%360;
        // HSV = value * (saturation * fullHue + (1 - saturation)).
        // Keep the stored HSV values even when the RGB result is black/grey.
        colorSurface.Opacity=Math.Clamp(saturation,0,1);
        shade.Opacity=1-Math.Clamp(value,0,1);
        double radians=(hue-20)*Math.PI/180;
        double x=230+150*Math.Cos(radians),y=200+150*Math.Sin(radians);
        Canvas.SetLeft(marker,x-11);Canvas.SetTop(marker,y-11);
        Canvas.SetLeft(markerEdge,x-13);Canvas.SetTop(markerEdge,y-13);
        preview.Fill=new SolidColorBrush(color);
        AutomationProperties.SetName(this,UiLanguage.Format($"光盘色相，{hue:0} 度，当前颜色 #{color.R:X2}{color.G:X2}{color.B:X2}"));
    }

    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(surface);
        if(!point.Properties.IsLeftButtonPressed||capturedPointer!=null)return;
        double radius=Radius(point.Position);
        if(radius<82||radius>182)return;
        Focus(FocusState.Pointer);
        if(!CapturePointer(e.Pointer))return;
        capturedPointer=e.Pointer.PointerId;Select(point.Position);e.Handled=true;
    }

    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(capturedPointer!=e.Pointer.PointerId)return;
        Select(e.GetCurrentPoint(surface).Position);e.Handled=true;
    }

    private static double Radius(Point p)=>Math.Sqrt((p.X-230)*(p.X-230)+(p.Y-200)*(p.Y-200));
    private void Select(Point p)
    {
        // Ignore the spindle dead zone even while dragging across the disc.
        if(Radius(p)<82)return;
        ChangeHue(Math.Atan2(p.Y-200,p.X-230)*180/Math.PI+20);
    }
    private void ChangeHue(double value)
    {
        hue=(value%360+360)%360;
        HueChanged?.Invoke(hue);
    }
    private void ReleaseDisc() {capturedPointer=null;ReleasePointerCaptures();}
}
