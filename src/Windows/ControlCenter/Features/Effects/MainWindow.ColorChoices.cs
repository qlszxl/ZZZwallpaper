using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static string ColorHex(Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private Button ColorChoiceButton(string title,Func<Color> read,Action<Color> change,bool hueOnly=false)
    {
        var button=WidgetButton(title,()=>{});button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.MinHeight=44;
        var swatch=new Border {Width=26,Height=26,CornerRadius=new CornerRadius(13),BorderThickness=new Thickness(1),BorderBrush=HomeBrush("HomeControlText"),VerticalAlignment=VerticalAlignment.Center};
        var text=UtilityText("","UtilityLabel");text.VerticalAlignment=VerticalAlignment.Center;
        var content=new Grid {ColumnSpacing=12};content.ColumnDefinitions.Add(new() {Width=GridLength.Auto});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new() {Width=GridLength.Auto});content.Children.Add(swatch);Grid.SetColumn(text,1);content.Children.Add(text);
        var arrow=CreateHomeVectorIcon("ChevronDown",14,button);Grid.SetColumn(arrow,2);content.Children.Add(arrow);button.Content=content;
        DiscColorPicker? picker=null;
        int pickerEpoch=-1;
        bool syncing=false;
        void Refresh() {var color=read();swatch.Background=new SolidColorBrush(color);text.Text=hueOnly?UiLanguage.Text("色环"):ColorHex(color);if(picker!=null&&pickerEpoch==widgetDrawerEpoch) {syncing=true;try {picker.Color=color;}finally {syncing=false;}}}
        button.Click+=(_,_)=> {
            // Keep the color picker in the existing XamlRoot. A detached native
            // Flyout.ShowAt can reject these dynamically created editor targets.
            var color=read();OpenWidgetDrawer(title,button,520,hueOnly?560:820);
            WidgetDrawerSubtitle.Text="COLOR DRIVE";WidgetDrawerSubtitle.Visibility=ColorDriveBackdrop.Visibility=Visibility.Visible;
            WidgetDrawerLayout.RowSpacing=10;WidgetDrawerScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
            int epoch=widgetDrawerEpoch;pickerEpoch=epoch;
            var currentPicker=new DiscColorPicker(color,hueOnly);picker=currentPicker;
            // The shared drawer handles short windows with vertical scrolling;
            // the vector disc scales with available width, while fields stay native.
            WidgetDrawerContent.Children.Add(currentPicker);
            currentPicker.ColorChanged+=next=> {if(syncing||epoch!=widgetDrawerEpoch||!ReferenceEquals(currentPicker,picker))return;change(next);Refresh();};
        };button.Unloaded+=(_,_)=> {if(ReferenceEquals(widgetDrawerFocus,button))CloseWidgetDrawer(false);picker=null;};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,title+UiLanguage.Text("，打开光盘调色器"));button.Tag=(Action)Refresh;Refresh();return button;
    }
    private void ColorDriveBackdrop_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        double width=e.NewSize.Width,height=e.NewSize.Height;
        ColorDriveBackdropClip.Rect=new Windows.Foundation.Rect(0,0,width,height);
        var stripes=new GeometryGroup();
        for(double x=-height;x<width;x+=22)
            stripes.Children.Add(new LineGeometry {StartPoint=new(x,0),EndPoint=new(x+height,height)});
        ColorDriveStripes.Data=stripes;
        var dots=new GeometryGroup();
        for(double y=9;y<height;y+=18)
            for(double x=9;x<width;x+=18)dots.Children.Add(new EllipseGeometry {Center=new(x,y),RadiusX=1,RadiusY=1});
        ColorDriveDots.Data=dots;
    }
    private FrameworkElement WidgetColorField(string label,string initial,Func<string> read,Action<string> change)
    {
        var button=ColorChoiceButton(label,()=>SkinColor(read()),color=> {if(!syncingWidgetEditor)change(ColorHex(color));});
        return WidgetField(label,button);
    }
    private static Color HueColor(double degrees)
    {
        double h=(degrees%360+360)%360/60,x=1-Math.Abs(h%2-1);var (r,g,b)=h switch {<1=>(1d,x,0d),<2=>(x,1d,0d),<3=>(0d,1d,x),<4=>(0d,x,1d),<5=>(x,0d,1d),_=>(1d,0d,x)};
        return Color.FromArgb(255,(byte)Math.Round(r*255),(byte)Math.Round(g*255),(byte)Math.Round(b*255));
    }
    private void InitializeEffectColorChoices()
    {
        foreach(var slider in new[]{GlitchHueSlider,LargeGlitchHueSlider}) {
            var slot=slider==GlitchHueSlider?GlitchHueInput:LargeGlitchHueInput;
            var numeric=(EffectValueInput)slot.Content;slot.Content=null;
            var button=ColorChoiceButton(slider==GlitchHueSlider?UiLanguage.Text("小故障偏移色相"):UiLanguage.Text("故障突发偏移色相"),()=>HueColor(slider.Value),color=> {double h=System.Drawing.Color.FromArgb(color.R,color.G,color.B).GetHue();slider.Value=h>180?h-360:h;},true);
            button.Style=HomeStyle("HomeIconButton");
            // The inherited rim consumes 6 DIP per side: 44 - 12 - 8 leaves the full 24 DIP swatch.
            button.Width=button.Height=44;button.MinWidth=button.MinHeight=44;button.Padding=new Thickness(4);button.CornerRadius=new CornerRadius(22);
            button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;button.VerticalAlignment=VerticalAlignment.Center;
            button.Content=new Border {Width=24,Height=24,CornerRadius=new CornerRadius(12),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(HueColor(slider.Value))};
            button.SetBinding(Control.IsEnabledProperty,new Binding {Source=slider,Path=new PropertyPath("IsEnabled")});
            var controls=new Grid {ColumnSpacing=6};controls.ColumnDefinitions.Add(new() {Width=GridLength.Auto});controls.ColumnDefinitions.Add(new());controls.Children.Add(button);numeric.Width=148;Grid.SetColumn(numeric,1);controls.Children.Add(numeric);slot.Content=controls;
            slider.ValueChanged+=(_,_)=> {((Border)button.Content).Background=new SolidColorBrush(HueColor(slider.Value));if(button.Tag is Action refresh)refresh();};
        }
    }
}
