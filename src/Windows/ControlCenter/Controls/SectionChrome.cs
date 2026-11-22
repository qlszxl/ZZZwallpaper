using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

// Metadata is independent of Content, so editor bindings and content ownership
// remain intact when a section receives the shared frame.
public sealed class SectionChrome : DependencyObject
{
    public static readonly DependencyProperty TitleProperty=DependencyProperty.RegisterAttached("Title",typeof(string),typeof(SectionChrome),new PropertyMetadata(""));
    public static string GetTitle(DependencyObject item)=>(string)item.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject item,string value)=>item.SetValue(TitleProperty,value);
    public static readonly DependencyProperty ScopeProperty=DependencyProperty.RegisterAttached("Scope",typeof(string),typeof(SectionChrome),new PropertyMetadata(""));
    public static string GetScope(DependencyObject item)=>(string)item.GetValue(ScopeProperty);
    public static void SetScope(DependencyObject item,string value)=>item.SetValue(ScopeProperty,value);
    public static readonly DependencyProperty HasOwnHeaderProperty=DependencyProperty.RegisterAttached("HasOwnHeader",typeof(bool),typeof(SectionChrome),new PropertyMetadata(false));
    public static bool GetHasOwnHeader(DependencyObject item)=>(bool)item.GetValue(HasOwnHeaderProperty);
    public static void SetHasOwnHeader(DependencyObject item,bool value)=>item.SetValue(HasOwnHeaderProperty,value);
    public static readonly DependencyProperty HeaderProperty=DependencyProperty.RegisterAttached("Header",typeof(object),typeof(SectionChrome),new PropertyMetadata(null));
    public static object? GetHeader(DependencyObject item)=>item.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject item,object value)=>item.SetValue(HeaderProperty,value);
    public static readonly DependencyProperty HeaderVisibilityProperty=DependencyProperty.RegisterAttached("HeaderVisibility",typeof(Visibility),typeof(SectionChrome),new PropertyMetadata(Visibility.Collapsed,HeaderVisibilityChanged));
    public static Visibility GetHeaderVisibility(DependencyObject item)=>(Visibility)item.GetValue(HeaderVisibilityProperty);
    public static void SetHeaderVisibility(DependencyObject item,Visibility value)=>item.SetValue(HeaderVisibilityProperty,value);
    public static readonly DependencyProperty BodyMarginProperty=DependencyProperty.RegisterAttached("BodyMargin",typeof(Thickness),typeof(SectionChrome),new PropertyMetadata(new Thickness(24)));
    public static Thickness GetBodyMargin(DependencyObject item)=>(Thickness)item.GetValue(BodyMarginProperty);
    public static void SetBodyMargin(DependencyObject item,Thickness value)=>item.SetValue(BodyMarginProperty,value);
    private static void HeaderVisibilityChanged(DependencyObject item,DependencyPropertyChangedEventArgs args)=>
        item.SetValue(BodyMarginProperty,(Visibility)args.NewValue==Visibility.Visible?new Thickness(24,0,24,24):new Thickness(24));
    // Presentation state belongs to this editor instance, never the saved model.
    // Collapsing the presenter leaves every parameter control and binding intact.
    public static readonly DependencyProperty IsExpandedProperty=DependencyProperty.RegisterAttached("IsExpanded",typeof(bool),typeof(SectionChrome),new PropertyMetadata(true,ExpansionChanged));
    public static bool GetIsExpanded(DependencyObject item)=>(bool)item.GetValue(IsExpandedProperty);
    public static void SetIsExpanded(DependencyObject item,bool value)=>item.SetValue(IsExpandedProperty,value);
    public static readonly DependencyProperty BodyVisibilityProperty=DependencyProperty.RegisterAttached("BodyVisibility",typeof(Visibility),typeof(SectionChrome),new PropertyMetadata(Visibility.Visible));
    public static Visibility GetBodyVisibility(DependencyObject item)=>(Visibility)item.GetValue(BodyVisibilityProperty);
    public static void SetBodyVisibility(DependencyObject item,Visibility value)=>item.SetValue(BodyVisibilityProperty,value);
    public static readonly DependencyProperty HeaderMarginProperty=DependencyProperty.RegisterAttached("HeaderMargin",typeof(Thickness),typeof(SectionChrome),new PropertyMetadata(new Thickness(0,0,0,4)));
    public static Thickness GetHeaderMargin(DependencyObject item)=>(Thickness)item.GetValue(HeaderMarginProperty);
    public static void SetHeaderMargin(DependencyObject item,Thickness value)=>item.SetValue(HeaderMarginProperty,value);
    private static void ExpansionChanged(DependencyObject item,DependencyPropertyChangedEventArgs args)
    {
        bool expanded=(bool)args.NewValue;
        item.SetValue(BodyVisibilityProperty,expanded?Visibility.Visible:Visibility.Collapsed);
        item.SetValue(HeaderMarginProperty,expanded?new Thickness(0,0,0,4):new Thickness(0));
    }
}

// One noninteractive vector path per frame. It never takes focus or pointer
// input and follows the existing brand-pattern preference.
public sealed class SectionPattern : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Path texture=new() {StrokeThickness=1,Opacity=.055};
    private static readonly DependencyProperty PatternEnabledProperty=DependencyProperty.RegisterAttached("PatternEnabled",typeof(bool),typeof(SectionPattern),new PropertyMetadata(true));
    public SectionPattern()
    {
        IsHitTestVisible=false;Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        texture.Stroke=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlText"];Children.Add(texture);
        SizeChanged+=(_,_)=>Draw();Loaded+=(_,_)=>RefreshVisibility();
    }
    internal static void SetEnabled(FrameworkElement root,bool enabled)
    {
        root.SetValue(PatternEnabledProperty,enabled);
        void Walk(DependencyObject node) {if(node is SectionPattern pattern)pattern.RefreshVisibility();for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Walk(VisualTreeHelper.GetChild(node,i));}
        Walk(root);
    }
    private void RefreshVisibility()=>Visibility=XamlRoot?.Content is { } root&&(bool)root.GetValue(PatternEnabledProperty)?Visibility.Visible:Visibility.Collapsed;
    private void Draw()
    {
        double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;
        Clip=new RectangleGeometry {Rect=new Rect(0,0,w,h)};
        var geometry=new PathGeometry();
        void Line(double x1,double y1,double x2,double y2){var figure=new PathFigure {StartPoint=new Point(x1,y1),IsFilled=false};figure.Segments.Add(new LineSegment {Point=new Point(x2,y2)});geometry.Figures.Add(figure);}
        // Texture stays near the right edge; the center remains quiet for text.
        double left=Math.Max(0,w-260);
        for(double x=left;x<w+h;x+=12)Line(x,0,x-h,h);
        for(double y=9;y<h;y+=23)for(double x=Math.Max(left,w-110);x<w;x+=29)Line(x,y,x+1,y);
        texture.Data=geometry;
        texture.Opacity=.045;
    }
}
