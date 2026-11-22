using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace LumaWall.ControlCenter;

internal sealed class SpectrumWeightEditor : UserControl
{
    private readonly Canvas canvas=new() {Height=240,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
    private SpectrumWeighting curve=new();
    private int selected;
    private uint? pointerId;
    private SpectrumWeightPoint[]? dragStart;
    internal event Action<SpectrumWeightPoint[]>? PointsChanged;
    internal event Action? SelectionChanged;
    internal event Action? InteractionCompleted;
    internal int SelectedIndex=>selected;
    internal SpectrumWeightPoint SelectedPoint=>curve.Points[selected];
    internal bool CanDelete=>selected>0&&selected<curve.Points.Length-1;
    internal bool CanAdd=>curve.Points.Length<32;
    internal void SelectPoint(int direction)
    {
        if(!IsEnabled)return;
        selected=Math.Clamp(selected+direction,0,curve.Points.Length-1);Draw();SelectionChanged?.Invoke();
    }
    private static Brush Brush(string key)=>(Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    internal SpectrumWeightEditor()
    {
        IsTabStop=true;MinWidth=0;
        InteractionFocus.SetEnabled(this,true);
        InteractionFocus.SetCustomTarget(this,true);
        var surface=new Border {Background=Brush("HomeControlRecess"),BorderBrush=Brush("HomeRimBrush"),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(16),Child=canvas};
        InteractionFocus.SetSurface(surface,true);Content=surface;
        AutomationProperties.SetName(this,UiLanguage.Text("频谱位置加权曲线，点击空白添加关键点，拖动调整，Delete 删除，方向键微调"));
        canvas.SizeChanged+=(_,_)=>Draw();
        canvas.PointerPressed+=Pressed;canvas.PointerMoved+=Moved;
        canvas.PointerReleased+=(_,e)=> {if(pointerId==e.Pointer.PointerId) {pointerId=null;dragStart=null;canvas.ReleasePointerCapture(e.Pointer);InteractionCompleted?.Invoke();e.Handled=true;}};
        canvas.PointerCaptureLost+=(_,_)=>EndDrag();canvas.PointerCanceled+=(_,_)=>EndDrag();
        IsEnabledChanged+=(_,_)=> {Opacity=IsEnabled?1:.45;if(!IsEnabled) {canvas.ReleasePointerCaptures();EndDrag();}};
        Unloaded+=(_,_)=> {canvas.ReleasePointerCaptures();EndDrag();};
        KeyDown+=(_,e)=> {
            if(!IsEnabled)return;
            if(e.Key==VirtualKey.Delete) {DeleteSelected();e.Handled=true;}
            else if(e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down) {
                double x=SelectedPoint.Position,y=SelectedPoint.Weight;
                if(e.Key==VirtualKey.Left)x-=.01;if(e.Key==VirtualKey.Right)x+=.01;if(e.Key==VirtualKey.Up)y+=.01;if(e.Key==VirtualKey.Down)y-=.01;
                MoveSelected(x,y);InteractionCompleted?.Invoke();e.Handled=true;
            } else if(e.Key==VirtualKey.Escape&&pointerId!=null) {
                var restore=dragStart;pointerId=null;dragStart=null;canvas.ReleasePointerCaptures();
                if(restore!=null)Publish(restore);InteractionCompleted?.Invoke();e.Handled=true;
            }
        };
    }
    internal void SetCurve(SpectrumWeighting value)
    {
        bool changed=curve.Smooth!=value.Smooth||!curve.Points.SequenceEqual(value.Points);
        curve=value;selected=Math.Clamp(selected,0,curve.Points.Length-1);if(changed)Draw();
    }
    private Rect Plot=>new(44,16,Math.Max(1,canvas.ActualWidth-72),190);
    private Point Pixel(SpectrumWeightPoint point) {var plot=Plot;return new(plot.X+point.Position*plot.Width,plot.Bottom-point.Weight*plot.Height);}
    private int Hit(Point point)
    {
        int found=-1;double nearest=14*14;
        for(int i=0;i<curve.Points.Length;i++) {var p=Pixel(curve.Points[i]);double d=(p.X-point.X)*(p.X-point.X)+(p.Y-point.Y)*(p.Y-point.Y);if(d<=nearest) {nearest=d;found=i;}}
        return found;
    }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(!IsEnabled||pointerId!=null)return;var point=e.GetCurrentPoint(canvas);int hit=Hit(point.Position);
        if(point.Properties.IsRightButtonPressed) {if(hit>=0) {selected=hit;DeleteSelected();Draw();SelectionChanged?.Invoke();}e.Handled=true;return;}
        if(!point.Properties.IsLeftButtonPressed)return;Focus(FocusState.Pointer);
        if(hit<0) {
            var plot=Plot;if(!plot.Contains(point.Position))return;
            hit=AddPoint((point.Position.X-plot.X)/plot.Width,(plot.Bottom-point.Position.Y)/plot.Height);if(hit<0)return;
        }
        selected=hit;dragStart=curve.Points.ToArray();pointerId=e.Pointer.PointerId;
        if(!canvas.CapturePointer(e.Pointer)) {pointerId=null;dragStart=null;}
        Draw();SelectionChanged?.Invoke();e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(pointerId!=e.Pointer.PointerId)return;var point=e.GetCurrentPoint(canvas);
        if(!point.Properties.IsLeftButtonPressed) {canvas.ReleasePointerCaptures();EndDrag();return;}
        var plot=Plot;MoveSelected((point.Position.X-plot.X)/plot.Width,(plot.Bottom-point.Position.Y)/plot.Height);e.Handled=true;
    }
    private void EndDrag() {if(pointerId==null)return;pointerId=null;dragStart=null;InteractionCompleted?.Invoke();}
    internal void MoveSelected(double position,double weight)
    {
        if(!IsEnabled)return;var points=curve.Points.ToArray();double x=points[selected].Position;
        if(CanDelete) {
            double min=points[selected-1].Position+.001,max=points[selected+1].Position-.001;
            if(max>=min)x=Math.Clamp(Math.Round(position,3),min,max);
        }
        points[selected]=new(x,Math.Clamp(Math.Round(weight,3),0,1));Publish(points);
    }
    private int AddPoint(double position,double weight)
    {
        if(!IsEnabled||!CanAdd)return -1;position=Math.Clamp(Math.Round(position,3),0,1);
        if(curve.Points.Any(p=>Math.Abs(p.Position-position)<.001-1e-9))return -1;
        var point=new SpectrumWeightPoint(position,Math.Clamp(Math.Round(weight,3),0,1));
        var points=curve.Points.Append(point).OrderBy(p=>p.Position).ToArray();selected=Array.IndexOf(points,point);Publish(points);return selected;
    }
    internal void AddPoint()
    {
        if(!IsEnabled||!CanAdd)return;
        int gap=0;for(int i=1;i<curve.Points.Length-1;i++)if(curve.Points[i+1].Position-curve.Points[i].Position>curve.Points[gap+1].Position-curve.Points[gap].Position)gap=i;
        double x=(curve.Points[gap].Position+curve.Points[gap+1].Position)/2;
        AddPoint(x,SpectrumWeightCurve.Evaluate(curve,x));InteractionCompleted?.Invoke();
    }
    internal void DeleteSelected()
    {
        if(!IsEnabled||!CanDelete)return;var points=curve.Points.Where((_,i)=>i!=selected).ToArray();selected--;Publish(points);InteractionCompleted?.Invoke();
    }
    private void Publish(SpectrumWeightPoint[] points)
    {
        curve=curve with {Points=points};Draw();PointsChanged?.Invoke(points);SelectionChanged?.Invoke();
    }
    private void Draw()
    {
        if(canvas.ActualWidth<=0)return;canvas.Children.Clear();var plot=Plot;var accent=Brush("InteractionAccentBrush");var edge=Brush("HomeControlText");
        void Line(double x1,double y1,double x2,double y2,double opacity) =>canvas.Children.Add(new Line {X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=edge,StrokeThickness=1,Opacity=opacity,IsHitTestVisible=false});
        void Label(string text,double x,double y,double width) {var label=new TextBlock {Text=text,Width=width,FontFamily=UiFonts.Body,FontSize=11,Foreground=edge,Opacity=.65,TextAlignment=TextAlignment.Center,IsHitTestVisible=false};Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);}
        for(int i=0;i<=4;i++) {
            double x=plot.X+i*plot.Width/4,y=plot.Bottom-i*plot.Height/4;Line(x,plot.Top,x,plot.Bottom,.12);Line(plot.Left,y,plot.Right,y,.16);
            Label($"{i*25}%",0,y-8,40);Label($"{i*25}%",x-22,plot.Bottom+9,44);
        }
        var line=new Polyline {Points=new PointCollection(),Stroke=accent,StrokeThickness=2.5,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};
        var fill=new Polygon {Points=new PointCollection(),Fill=accent,Opacity=.12,IsHitTestVisible=false};fill.Points.Add(new(plot.Left,plot.Bottom));
        for(int i=0;i<=160;i++) {var p=Pixel(new(i/160d,SpectrumWeightCurve.Evaluate(curve,i/160d)));line.Points.Add(p);fill.Points.Add(p);}
        fill.Points.Add(new(plot.Right,plot.Bottom));canvas.Children.Add(fill);canvas.Children.Add(line);
        for(int i=0;i<curve.Points.Length;i++) {
            var p=Pixel(curve.Points[i]);double size=i==selected?16:12;
            var dot=new Ellipse {Width=size,Height=size,Fill=i==selected?accent:Brush("HomeControlRecess"),Stroke=accent,StrokeThickness=2,IsHitTestVisible=false};
            Canvas.SetLeft(dot,p.X-size/2);Canvas.SetTop(dot,p.Y-size/2);canvas.Children.Add(dot);
        }
    }
}
