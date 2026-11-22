using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class VisualWidgetContent : WidgetContent
{
    private static M.SolidColorBrush VisualBrush(string color,double opacity=1) {var brush=new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(color)) {Opacity=opacity};brush.Freeze();return brush;}
    internal VisualWidgetContent(WidgetContentContext context):base(context)
    {
        var s=instance.Visual;W.FrameworkElement visual;
        var fill=VisualBrush(s.FillColor,s.FillOpacity);var edge=VisualBrush(s.BorderColor);
        if(instance.Kind==WidgetKind.Mask)visual=new MaskView(s);
        else {
            W.FrameworkElement child;
            if(instance.Kind==WidgetKind.Text)child=new C.TextBlock {Text=s.Text,FontFamily=WidgetVisuals.Body,FontSize=s.FontSize,FontWeight=s.Bold?W.FontWeights.Bold:W.FontWeights.Normal,Foreground=VisualBrush(s.TextColor),TextWrapping=W.TextWrapping.Wrap,TextAlignment=s.Alignment==0?W.TextAlignment.Left:s.Alignment==1?W.TextAlignment.Center:W.TextAlignment.Right,VerticalAlignment=W.VerticalAlignment.Center};
            else {
                child=StickerVisual(s);if(s.FlipHorizontal) {child.RenderTransformOrigin=new W.Point(.5,.5);child.RenderTransform=new M.ScaleTransform(-1,1);}
            }
            var border=new C.Border {Child=child,Padding=new W.Thickness(s.Padding),Background=s.Background?fill:M.Brushes.Transparent,BorderBrush=edge,BorderThickness=new W.Thickness(s.BorderWidth),CornerRadius=new W.CornerRadius(s.Radius),ClipToBounds=true};
            border.SizeChanged+=(_,_)=>border.Clip=new M.RectangleGeometry(new W.Rect(0,0,border.ActualWidth,border.ActualHeight),Math.Min(s.Radius,border.ActualWidth/2),Math.Min(s.Radius,border.ActualHeight/2));visual=border;
        }
        var button=new C.Button {Content=visual,Style=WidgetVisuals.PlainButton(),HorizontalContentAlignment=W.HorizontalAlignment.Stretch,VerticalContentAlignment=W.VerticalAlignment.Stretch,Padding=new W.Thickness(0)};
        DragHandle(button,()=>Send(new() {Type="edit-widget",Id=instance.Id}),opensSettings:true);Element=button;
        WidgetVisuals.Action(menu!,UiLanguage.Text("组件设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string,M.Imaging.BitmapImage> stickerImages=new();
    private static W.FrameworkElement StickerVisual(VisualWidgetOptions s)
    {
        if(s.Image!=null)try {
            var bitmap=stickerImages.GetValue(s.Image,image=> {using var stream=new MemoryStream(Convert.FromBase64String(image));var result=new M.Imaging.BitmapImage();result.BeginInit();result.CacheOption=M.Imaging.BitmapCacheOption.OnLoad;result.StreamSource=stream;result.EndInit();result.Freeze();return result;});
            return new C.Image {Source=bitmap,Stretch=s.ImageFit==0?M.Stretch.Uniform:s.ImageFit==1?M.Stretch.UniformToFill:M.Stretch.Fill};
        }catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException or System.FormatException) { }
        string path=s.StickerShape switch {
            1=>"M 50,0 A 50,50 0 1 1 49.99,0 Z",
            2=>"M 50,90 C 15,65 0,48 5,25 C 10,0 40,0 50,22 C 60,0 90,0 95,25 C 100,48 85,65 50,90 Z",
            3=>"M 56,0 L 15,58 L 44,58 L 35,100 L 88,36 L 57,36 L 70,0 Z",
            _=>"M 50,2 L 62,35 L 98,36 L 70,58 L 80,95 L 50,74 L 20,95 L 30,58 L 2,36 L 38,35 Z"
        };
        return new System.Windows.Shapes.Path {Data=M.Geometry.Parse(path),Fill=VisualBrush(s.BorderColor),Stretch=M.Stretch.Uniform};
    }
    private sealed class MaskView(VisualWidgetOptions options) : W.FrameworkElement
    {
        protected override void OnRender(M.DrawingContext dc)
        {
            base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;
            var color=(M.Color)M.ColorConverter.ConvertFromString(options.FillColor);color.A=(byte)Math.Round(options.FillOpacity*255);M.Brush fill=new M.SolidColorBrush(color);
            if(options.Gradient!=0) {
                var clear=color;clear.A=0;var start=options.Gradient switch {1=>new W.Point(0,.5),2=>new W.Point(1,.5),3=>new W.Point(.5,0),_=>new W.Point(.5,1)};
                fill=new M.LinearGradientBrush(color,clear,start,new W.Point(1-start.X,1-start.Y));
            }
            double line=Math.Min(options.BorderWidth,Math.Min(w,h));var rect=new W.Rect(line/2,line/2,Math.Max(0,w-line),Math.Max(0,h-line));M.Pen? pen=line>0?new M.Pen(VisualBrush(options.BorderColor),line):null;
            // Transparent hit surface keeps a fully faded mask editable until mouse pass-through is enabled.
            dc.DrawRectangle(M.Brushes.Transparent,null,new W.Rect(0,0,w,h));
            if(options.MaskShape==1)dc.DrawEllipse(fill,pen,new W.Point(w/2,h/2),rect.Width/2,rect.Height/2);
            else dc.DrawRoundedRectangle(fill,pen,rect,Math.Min(options.Radius,w/2),Math.Min(options.Radius,h/2));
        }
    }
}
