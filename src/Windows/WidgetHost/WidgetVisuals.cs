using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;
using Shapes=System.Windows.Shapes;

namespace LumaWall.WidgetHost;

internal static class WidgetVisuals
{
    private sealed class Palette : W.DependencyObject
    {
        internal static readonly W.DependencyProperty ColorProperty=W.DependencyProperty.Register(nameof(Color),typeof(M.Color),typeof(Palette),new W.PropertyMetadata(M.Color.FromRgb(244,230,0)));
        internal static readonly W.DependencyProperty SurfaceProperty=W.DependencyProperty.Register(nameof(Surface),typeof(M.Color),typeof(Palette),new W.PropertyMetadata(M.Color.FromRgb(25,25,25)));
        public M.Color Surface {get=>(M.Color)GetValue(SurfaceProperty);set=>SetValue(SurfaceProperty,value);}
        public M.Color Color {get=>(M.Color)GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
    }
    private static readonly Palette palette=new();
    // Template sealing freezes ordinary Freezables. A bound brush cannot freeze,
    // so menu templates and live controls can safely share the same palette.
    private static M.SolidColorBrush LiveAccent(string property=nameof(Palette.Color))
    {
        var brush=new M.SolidColorBrush();System.Windows.Data.BindingOperations.SetBinding(brush,M.SolidColorBrush.ColorProperty,new System.Windows.Data.Binding(property) {Source=palette});return brush;
    }
    internal static readonly M.SolidColorBrush Surface=LiveAccent(nameof(Palette.Surface)),Accent=LiveAccent(),Ink=Brush(242,242,242),Muted=Brush(156,156,156),Edge=Brush(44,44,44);
    private static readonly Uri fontBase=new(Path.Combine(AppContext.BaseDirectory,"Assets","Fonts")+Path.DirectorySeparatorChar);
    internal static readonly M.FontFamily Body=new(fontBase,"./#Source Han Sans SC Medium"),Heavy=new(fontBase,"./#Source Han Sans SC Heavy");
    private static AppearanceOptions appearance=new();
    private static M.Color accentStart=M.Color.FromRgb(244,230,0),accentEnd=M.Color.FromRgb(134,235,22);
    internal static void ApplyAppearance(AppearanceOptions value) {
        appearance=value.Normalize();var skin=SkinPalette.Find(appearance.Skin);
        accentStart=(M.Color)M.ColorConverter.ConvertFromString(skin.Accent);accentEnd=(M.Color)M.ColorConverter.ConvertFromString(skin.AccentEnd);
        palette.Surface=(M.Color)M.ColorConverter.ConvertFromString(skin.Surface);Advance();
    }
    internal static bool Animate=>appearance.Motion&&W.SystemParameters.ClientAreaAnimation;
    internal static void Advance()
    {
        double phase=Animate?(Environment.TickCount64%2000)/1000.0:0,t=phase<=1?phase:2-phase;
        palette.Color=M.Color.FromRgb((byte)Math.Round(accentStart.R+(accentEnd.R-accentStart.R)*t),(byte)Math.Round(accentStart.G+(accentEnd.G-accentStart.G)*t),(byte)Math.Round(accentStart.B+(accentEnd.B-accentStart.B)*t));
    }
    private static M.SolidColorBrush Brush(byte r,byte g,byte b)=>new(M.Color.FromRgb(r,g,b));
    internal static C.Border Frame(W.UIElement child,out C.Border edge)
    {
        var gradient=new M.LinearGradientBrush {StartPoint=new W.Point(0,0),EndPoint=new W.Point(0,1)};
        gradient.GradientStops.Add(new M.GradientStop(M.Color.FromRgb(8,8,8),0));var end=new M.GradientStop {Offset=1};
        System.Windows.Data.BindingOperations.SetBinding(end,M.GradientStop.ColorProperty,new System.Windows.Data.Binding(nameof(Palette.Surface)) {Source=palette});gradient.GradientStops.Add(end);
        var inner=new C.Border {Background=gradient,CornerRadius=new W.CornerRadius(16),Child=child};
        var innerEdge=new C.Border {Background=Edge,CornerRadius=new W.CornerRadius(17),Padding=new W.Thickness(1),Child=inner};
        edge=new C.Border {Background=Edge,CornerRadius=new W.CornerRadius(22),Padding=new W.Thickness(3),Child=new C.Border {Background=Brush(2,2,2),CornerRadius=new W.CornerRadius(19),Padding=new W.Thickness(2),Child=innerEdge}};
        return edge;
    }
    internal static void SetFrameRadius(C.Border frame,double radius)
    {
        // Preserve concentric rims at every radius, including square corners.
        C.Border? current=frame;double inset=0;
        while(current!=null) {
            current.CornerRadius=new W.CornerRadius(Math.Max(0,radius-inset));
            inset+=current.Padding.Left;current=current.Child as C.Border;
        }
    }
    internal static W.FrameworkElement Icon(string name,M.Brush ink)
    {
        string data=name switch {
            "search"=>"M10,3 A7,7 0 1 1 9.99,3 M15,15 L22,22",
            "folder"=>"M3,6 L9,6 L12,9 L21,9 L21,20 L3,20 Z M3,6 L3,4 L9,4 L12,7 L20,7",
            "command"=>"M3,3 L21,3 L21,21 L3,21 Z M7,8 L11,12 L7,16 M13,16 L17,16",
            "performance"=>"M3,19 L3,13 M9,19 L9,5 M15,19 L15,9 M21,19 L21,3",
            "copy"=>"M8,8 L21,8 L21,21 L8,21 Z M4,16 L3,16 L3,3 L16,3 L16,4",
            "edit"=>"M4,15 L16,3 L21,8 L9,20 L3,21 Z M13,6 L18,11",
            "close"=>"M6,6 L18,18 M18,6 L6,18",
            "visible"=>"M2,12 C7,3 17,3 22,12 C17,21 7,21 2,12 Z M9,12 A3,3 0 1 0 15,12 A3,3 0 1 0 9,12",
            "lock"=>"M5,10 L19,10 L19,21 L5,21 Z M8,10 L8,7 A4,4 0 0 1 16,7 L16,10 M12,14 L12,17",
            "unlock"=>"M5,10 L19,10 L19,21 L5,21 Z M8,10 L8,7 A4,4 0 0 1 16,7 M12,14 L12,17",
            "settings"=>"M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M8,15 L8,21",
            _=>"M3,3 L9,3 L9,9 L3,9 Z M15,3 L21,3 L21,9 L15,9 Z M3,15 L9,15 L9,21 L3,21 Z M15,15 L21,15 L21,21 L15,21 Z"
        };
        return new C.Viewbox {Width=20,Height=20,HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center,Child=new Shapes.Path {Data=M.Geometry.Parse(data),Stroke=ink,StrokeThickness=2.6,StrokeStartLineCap=M.PenLineCap.Round,StrokeEndLineCap=M.PenLineCap.Round,StrokeLineJoin=M.PenLineJoin.Round,Width=24,Height=24}};
    }
    internal static W.Style PlainButton()
    {
        var presenter=new W.FrameworkElementFactory(typeof(C.ContentPresenter));presenter.SetValue(C.ContentPresenter.HorizontalAlignmentProperty,W.HorizontalAlignment.Stretch);presenter.SetValue(C.ContentPresenter.VerticalAlignmentProperty,W.VerticalAlignment.Stretch);
        var style=new W.Style(typeof(C.Button));style.Setters.Add(new W.Setter(C.Control.TemplateProperty,new C.ControlTemplate(typeof(C.Button)) {VisualTree=presenter}));
        style.Setters.Add(new W.Setter(C.Control.BackgroundProperty,M.Brushes.Transparent));style.Setters.Add(new W.Setter(C.Control.ForegroundProperty,Ink));style.Setters.Add(new W.Setter(C.Control.BorderThicknessProperty,new W.Thickness(0)));style.Setters.Add(new W.Setter(W.FrameworkElement.FocusVisualStyleProperty,null));
        style.Setters.Add(new W.Setter(W.FrameworkElement.CursorProperty,I.Cursors.Hand));return style;
    }
    internal static C.ContextMenu Menu()
    {
        var menu=new C.ContextMenu {Background=Surface,Foreground=Ink,FontFamily=Body,FontSize=14,Padding=new W.Thickness(7),HasDropShadow=true};
        var edge=new W.FrameworkElementFactory(typeof(C.Border));edge.SetValue(C.Border.BackgroundProperty,Surface);edge.SetValue(C.Border.BorderBrushProperty,Edge);edge.SetValue(C.Border.BorderThicknessProperty,new W.Thickness(2));edge.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(14));edge.SetValue(C.Border.PaddingProperty,new W.Thickness(7));edge.AppendChild(new W.FrameworkElementFactory(typeof(C.ItemsPresenter)));
        menu.Template=new C.ControlTemplate(typeof(C.ContextMenu)) {VisualTree=edge};return menu;
    }
    internal static C.MenuItem Action(C.ContextMenu menu,string label,string glyph,Action action,Func<bool>? canExecute=null)=>Action(menu,()=>label,glyph,action,canExecute);
    internal static C.MenuItem Action(C.ContextMenu menu,Func<string> label,string glyph,Action action,Func<bool>? canExecute=null)
    {
        var item=new C.MenuItem {MinWidth=196,Height=40,Margin=new W.Thickness(0,2,0,2),Padding=new W.Thickness(12,0,12,0)};
        var header=new C.StackPanel {Orientation=C.Orientation.Horizontal,VerticalAlignment=W.VerticalAlignment.Center};
        var icon=(C.Viewbox)Icon(glyph,Ink);((Shapes.Path)icon.Child).SetBinding(Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") {RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(C.MenuItem),1)});
        var caption=new C.TextBlock {Margin=new W.Thickness(10,0,0,0),VerticalAlignment=W.VerticalAlignment.Center};
        header.Children.Add(icon);header.Children.Add(caption);item.Header=header;
        var border=new W.FrameworkElementFactory(typeof(C.Border));border.Name="surface";border.SetValue(C.Border.BackgroundProperty,Surface);border.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(10));border.SetValue(C.Border.PaddingProperty,new W.Thickness(12,0,12,0));
        var text=new W.FrameworkElementFactory(typeof(C.ContentPresenter));text.SetValue(C.ContentPresenter.ContentSourceProperty,"Header");text.SetValue(C.ContentPresenter.VerticalAlignmentProperty,W.VerticalAlignment.Center);border.AppendChild(text);
        var template=new C.ControlTemplate(typeof(C.MenuItem)) {VisualTree=border};
        var hover=new W.Trigger {Property=C.MenuItem.IsHighlightedProperty,Value=true};hover.Setters.Add(new W.Setter(C.Border.BackgroundProperty,Accent,"surface"));hover.Setters.Add(new W.Setter(C.Control.ForegroundProperty,Brush(12,14,0)));template.Triggers.Add(hover);
        var disabled=new W.Trigger {Property=W.UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new W.Setter(W.UIElement.OpacityProperty,.45));template.Triggers.Add(disabled);item.Template=template;
        void Refresh() {caption.Text=label();W.Automation.AutomationProperties.SetName(item,caption.Text);item.IsEnabled=canExecute?.Invoke()??true;}
        item.Tag=(Action)Refresh;menu.Opened+=(_,_)=>Refresh();Refresh();
        item.Click+=(_,e)=> {e.Handled=true;if(canExecute?.Invoke()==false)return;menu.IsOpen=false;action();};menu.Items.Add(item);return item;
    }
    internal static void RefreshMenu(C.ContextMenu menu) {foreach(var item in menu.Items.OfType<C.MenuItem>())if(item.Tag is Action refresh)refresh();}
    internal static C.ToolTip Tip(string text)
    {
        var tip=new C.ToolTip {Content=text,Background=Surface,Foreground=Ink,FontFamily=Body,FontSize=12,MaxWidth=380};
        var border=new W.FrameworkElementFactory(typeof(C.Border));border.SetValue(C.Border.BackgroundProperty,Surface);border.SetValue(C.Border.BorderBrushProperty,Edge);border.SetValue(C.Border.BorderThicknessProperty,new W.Thickness(1));border.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(10));border.SetValue(C.Border.PaddingProperty,new W.Thickness(12,8,12,8));
        var textBlock=new W.FrameworkElementFactory(typeof(C.TextBlock));textBlock.SetBinding(C.TextBlock.TextProperty,new System.Windows.Data.Binding("Content") {RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});textBlock.SetValue(C.TextBlock.TextWrappingProperty,W.TextWrapping.Wrap);border.AppendChild(textBlock);
        tip.Template=new C.ControlTemplate(typeof(C.ToolTip)) {VisualTree=border};return tip;
    }
}
