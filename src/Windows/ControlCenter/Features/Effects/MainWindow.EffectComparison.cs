using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Image effectOriginalImage=null!;
    private string? effectOriginalImagePath;
    private Border effectOriginalTaskbar=null!;
    private double effectComparisonPosition=.5;
    private bool effectComparisonDragging;
    private Action? endEffectComparisonDrag;
    private void InitializeImageComparison(StackPanel body)
    {
        const double handleSize=40,aspect=EffectImagePreviewClient.FrameHeight/(double)EffectImagePreviewClient.FrameWidth;
        var frameClip=new RectangleGeometry();
        var frame=new Grid {MaxWidth=960,Height=540,HorizontalAlignment=HorizontalAlignment.Stretch,Background=HomeBrush("HomeControlRecess"),Clip=frameClip};
        InteractionFocus.SetInteractiveRegion(frame,true);
        effectOriginalImage=new Image {Stretch=Stretch.Uniform};frame.Children.Add(effectOriginalImage);
        Border Taskbar() {
            var icons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Center};
            var content=new ContentControl {Content=icons,Foreground=HomeBrush("HomeControlText"),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,IsTabStop=false};
            foreach(string glyph in new[]{"Home","Search","Widgets","Settings"})icons.Children.Add(UtilityIcon(glyph,content,16));
            return new Border {Background=HomeBrush("HomeControlSurface"),Height=30,VerticalAlignment=VerticalAlignment.Bottom,Child=content,Visibility=Visibility.Collapsed};
        }
        effectOriginalTaskbar=Taskbar();frame.Children.Add(effectOriginalTaskbar);
        var clip=new RectangleGeometry();var previewLayer=new Grid {Clip=clip};effectPreviewImage=new Image {Stretch=Stretch.Uniform};previewLayer.Children.Add(effectPreviewImage);effectPreviewTaskbar=Taskbar();previewLayer.Children.Add(effectPreviewTaskbar);frame.Children.Add(previewLayer);
        Border Caption(string value,HorizontalAlignment side)=>new() {Child=UtilityText(value,"UtilityLabel"),Background=HomeBrush("HomeControlRecess"),Padding=new Thickness(12,6,12,6),CornerRadius=new CornerRadius(12),Margin=new Thickness(14),HorizontalAlignment=side,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
        frame.Children.Add(Caption(UiLanguage.Text("效果预览"),HorizontalAlignment.Left));frame.Children.Add(Caption(UiLanguage.Text("原图"),HorizontalAlignment.Right));
        var lineOffset=new TranslateTransform();var handleOffset=new TranslateTransform();
        var line=new Border {Width=2,Background=HomeBrush("InteractionAccentBrush"),HorizontalAlignment=HorizontalAlignment.Left,RenderTransform=lineOffset,IsHitTestVisible=false};frame.Children.Add(line);
        // A native-size circle and symmetric vector arrows avoid button-template
        // minimum sizes, font baselines and Viewbox scaling of the grip.
        var grip=new Grid {Width=handleSize,Height=handleSize,CornerRadius=new CornerRadius(handleSize/2)};
        InteractionFocus.SetSurface(grip,true);
        grip.Children.Add(new Ellipse {Fill=HomeBrush("HomeControlRecess"),Stroke=HomeBrush("InteractionAccentBrush"),StrokeThickness=2});
        var arrows=new Canvas {Width=20,Height=16,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        void Arrow(params Point[] points) {
            var coordinates=new PointCollection();foreach(var point in points)coordinates.Add(point);
            arrows.Children.Add(new Polyline {Points=coordinates,Stroke=HomeBrush("HomeControlText"),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
        }
        Arrow(new(6,3),new(1,8),new(6,13));Arrow(new(14,3),new(19,8),new(14,13));Arrow(new(1,8),new(19,8));grip.Children.Add(arrows);
        var handle=new UserControl {Content=grip,Width=handleSize,Height=handleSize,MinWidth=0,MinHeight=0,Padding=new Thickness(0),IsTabStop=true,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,RenderTransform=handleOffset};frame.Children.Add(handle);
        InteractionFocus.SetEnabled(handle,true);
        InteractionFocus.SetCustomTarget(handle,true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(handle,UiLanguage.Text("原图对比分隔线，左右键移动，Home 和 End 显示单侧"));
        void Announce()=>Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(handle,UiLanguage.Format($"左侧效果预览 {effectComparisonPosition:P0}，右侧原图"));
        void Position(double value) {
            effectComparisonPosition=Math.Clamp(value,0,1);double width=frame.ActualWidth,height=frame.ActualHeight,x=width*effectComparisonPosition;
            clip.Rect=new Rect(0,0,x,height);lineOffset.X=Math.Clamp(x-1,0,Math.Max(0,width-2));handleOffset.X=Math.Clamp(x-handleSize/2,0,Math.Max(0,width-handleSize));
        }
        uint? pointer=null;double? pending=null;bool rendering=false;
        void Paint(object? sender,object args) {if(pending is { } value) {pending=null;Position(value);}}
        void EndDrag() {
            Paint(null,EventArgs.Empty);pointer=null;
            if(rendering) {CompositionTarget.Rendering-=Paint;rendering=false;}
            if(effectComparisonDragging) {effectComparisonDragging=false;if(effectExplanationOpen&&effectPreviewTimer?.IsEnabled==true)effectPreviewClock.Start();}
            Announce();
        }
        endEffectComparisonDrag=()=> {EndDrag();frame.ReleasePointerCaptures();};
        double PointerPosition(PointerRoutedEventArgs e)=>e.GetCurrentPoint(frame).Position.X/Math.Max(1,frame.ActualWidth);
        frame.PointerPressed+=(_,e)=> {
            if(pointer!=null||!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed)return;
            if(!frame.CapturePointer(e.Pointer))return;
            pointer=e.Pointer.PointerId;effectComparisonDragging=true;effectPreviewClock.Stop();handle.Focus(FocusState.Pointer);Position(PointerPosition(e));
            CompositionTarget.Rendering+=Paint;rendering=true;e.Handled=true;
        };
        frame.PointerMoved+=(_,e)=> {if(pointer==e.Pointer.PointerId) {if(!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) {EndDrag();frame.ReleasePointerCapture(e.Pointer);}else pending=PointerPosition(e);e.Handled=true;}};
        frame.PointerReleased+=(_,e)=> {if(pointer==e.Pointer.PointerId) {pending=PointerPosition(e);EndDrag();frame.ReleasePointerCapture(e.Pointer);e.Handled=true;}};
        frame.PointerCanceled+=(_,_)=>EndDrag();frame.PointerCaptureLost+=(_,_)=>EndDrag();
        frame.Unloaded+=(_,_)=> {EndDrag();frame.ReleasePointerCaptures();};Closed+=(_,_)=>EndDrag();
        frame.SizeChanged+=(_,_)=> {frame.Height=frame.ActualWidth*aspect;frameClip.Rect=new Rect(0,0,frame.ActualWidth,frame.ActualHeight);Position(effectComparisonPosition);};
        handle.KeyDown+=(_,e)=> {double? value=e.Key switch {VirtualKey.Left=>effectComparisonPosition-.02,VirtualKey.Right=>effectComparisonPosition+.02,VirtualKey.Home=>0,VirtualKey.End=>1,_=>null};if(value is { } v) {Position(v);Announce();e.Handled=true;}};
        InitializeLightFlareLocator(frame,()=>Position(1));
        Position(effectComparisonPosition);Announce();body.Children.Add(frame);
    }
}
