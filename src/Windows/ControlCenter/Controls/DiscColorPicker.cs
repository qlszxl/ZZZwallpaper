using LumaWall.Contracts;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace LumaWall.ControlCenter;

internal sealed class DiscColorPicker : UserControl
{
    // Recent choices are shared by all color fields for this application session.
    private static readonly List<Color> recent = [Rgb(255,255,255),Rgb(0,0,0),Rgb(96,132,255),Rgb(0,224,181),Rgb(229,245,0),Rgb(255,74,85)];
    private readonly DiscHueSelector disc=new();
    private readonly Slider saturation;
    private readonly Slider brightness;
    private readonly ScrubNumberInput saturationInput=new(UiLanguage.Text("饱和度"),true);
    private readonly ScrubNumberInput brightnessInput=new(UiLanguage.Text("明度"),true);
    private readonly ScrubNumberInput[] channels=[new(UiLanguage.Text("红色"),true),new(UiLanguage.Text("绿色"),true),new(UiLanguage.Text("蓝色"),true)];
    private readonly TextBox hex=new() { Style=StyleOf("UtilityTextBox"), MinWidth=0, PlaceholderText="#RRGGBB" };
    private readonly TextBlock hexHint=Label(UiLanguage.Text("颜色格式：#RRGGBB"),"UtilityHint");
    private readonly Border swatch=new() { Width=52, Height=32, CornerRadius=new(8), BorderThickness=new(1), BorderBrush=BrushOf("HomeControlEdge") };
    private readonly List<(Border Ring,Color Color)> swatchRings=[];
    private readonly bool hueOnly;
    private readonly StackPanel controls=new() {Spacing=10};
    private ScrollViewer? viewport;
    private bool updating,changed,fittingDisc;
    private double hue,s=1,v=1;
    private Color color;
    internal event Action<Color>? ColorChanged;

    internal DiscColorPicker(Color initial,bool hueOnly=false)
    {
        this.hueOnly=hueOnly;IsTabStop=false;
        HorizontalAlignment=HorizontalAlignment.Stretch;
        saturation=MakeSlider(UiLanguage.Text("饱和度"));brightness=MakeSlider(UiLanguage.Text("明度"));
        var body=new StackPanel { Spacing=10, HorizontalAlignment=HorizontalAlignment.Stretch };
        disc.HorizontalAlignment=HorizontalAlignment.Center;
        body.Children.Add(disc);body.Children.Add(controls);
        controls.SizeChanged+=(_,_)=>FitDisc();
        disc.HueChanged+=value=> {hue=value;ApplyHsv();};

        if(!hueOnly) {
            var sliders=new StackPanel { Spacing=8 };
            sliders.Children.Add(SliderRow(UiLanguage.Text("饱和度"),saturation,saturationInput));
            sliders.Children.Add(SliderRow(UiLanguage.Text("明度"),brightness,brightnessInput));
            controls.Children.Add(Section(sliders));
            var fields=new StackPanel { Spacing=10 };
            var hexRow=new Grid { ColumnSpacing=10 };
            hexRow.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            hexRow.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            hexRow.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            hexRow.ColumnDefinitions.Add(new());
            Add(hexRow,Label(UiLanguage.Text("当前颜色")),0);Add(hexRow,swatch,1);Add(hexRow,Label("HEX"),2);Add(hexRow,hex,3);
            fields.Children.Add(hexRow);
            var rgbRow=new Grid { ColumnSpacing=12 };
            for(int i=0;i<3;i++) {
                rgbRow.ColumnDefinitions.Add(new());
                var field=new Grid { ColumnSpacing=8 };
                field.ColumnDefinitions.Add(new() {Width=GridLength.Auto});field.ColumnDefinitions.Add(new());
                Add(field,Label(new[]{"R","G","B"}[i]),0);Add(field,channels[i],1);Add(rgbRow,field,i);
                int channel=i;channels[i].ValueChanged+=value=> {
                    if(updating)return;
                    SetColor(channel switch {0=>Rgb((byte)value,color.G,color.B),1=>Rgb(color.R,(byte)value,color.B),_=>Rgb(color.R,color.G,(byte)value)},true);
                };
            }
            fields.Children.Add(rgbRow);hexHint.Visibility=Visibility.Collapsed;fields.Children.Add(hexHint);
            controls.Children.Add(Section(fields));
            controls.Children.Add(RecentRow());
        }
        else controls.Visibility=Visibility.Collapsed;
        Content=body;
        saturation.ValueChanged+=(_,e)=> {if(updating)return;s=e.NewValue/100;ApplyHsv();};
        brightness.ValueChanged+=(_,e)=> {if(updating)return;v=e.NewValue/100;ApplyHsv();};
        saturationInput.ValueChanged+=value=> {if(!updating)saturation.Value=value;};
        brightnessInput.ValueChanged+=value=> {if(!updating)brightness.Value=value;};
        AutomationProperties.SetName(hex,UiLanguage.Text("HEX 颜色值"));
        AutomationProperties.SetHelpText(hex,UiLanguage.Text("输入 6 位十六进制颜色，可省略井号。"));
        hex.BeforeTextChanging+=(_,e)=>{if(!updating&&e.NewText.Length>7){e.Cancel=true;hexHint.Visibility=Visibility.Visible;}};
        hex.TextChanged+=(_,_)=> {
            if(updating)return;
            hexHint.Visibility=Visibility.Collapsed;
        };
        DoubleClickEdit.OnAccepted(hex,()=> {CommitHex(false);return hexHint.Visibility!=Visibility.Visible;});
        Loaded+=(_,_)=> {
            DependencyObject? ancestor=VisualTreeHelper.GetParent(this);
            while(ancestor!=null&&ancestor is not ScrollViewer)ancestor=VisualTreeHelper.GetParent(ancestor);
            viewport=ancestor as ScrollViewer;
            if(viewport!=null) {viewport.SizeChanged+=ViewportChanged;FitDisc();}
        };
        Unloaded+=(_,_)=> {if(viewport!=null)viewport.SizeChanged-=ViewportChanged;viewport=null;Remember();};
        SetColor(initial,false);
    }

    internal Color Color {
        get=>color;
        set {if(!Same(color,value))SetColor(value,false);}
    }

    // Commit draft numeric text before the modal invalidates its epoch/removes
    // controls: otherwise a typed value can be lost when closing with Escape.
    internal void Commit()
    {
        if(!hueOnly) {
            saturationInput.CommitEdit();brightnessInput.CommitEdit();
            foreach(var channel in channels)channel.CommitEdit();
            CommitHex();
        }
        Remember();
    }

    private static Style StyleOf(string key)=>(Style)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static Brush BrushOf(string key)=>(Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static TextBlock Label(string text,string style="UtilityLabel")=>new() {Text=text,Style=StyleOf(style),VerticalAlignment=VerticalAlignment.Center};
    private static Color Rgb(byte r,byte g,byte b)=>Windows.UI.Color.FromArgb(255,r,g,b);
    private static string Hex(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static bool Same(Color a,Color b)=>a.R==b.R&&a.G==b.G&&a.B==b.B&&a.A==b.A;
    private static void Add(Grid grid,FrameworkElement child,int column) {Grid.SetColumn(child,column);grid.Children.Add(child);}
    private static Border Section(UIElement content)=>new() {Background=BrushOf("HomeWellBrush"),BorderBrush=BrushOf("HomeControlEdge"),BorderThickness=new(1),CornerRadius=new(12),Padding=new(12),Child=content};
    private void ViewportChanged(object sender,SizeChangedEventArgs e)=>FitDisc();
    private void FitDisc()
    {
        if(fittingDisc||viewport==null||viewport.ActualHeight<=0||viewport.ActualWidth<=0)return;
        fittingDisc=true;
        try {
            double width=viewport.ViewportWidth>0?viewport.ViewportWidth:viewport.ActualWidth;
            double height=viewport.ViewportHeight>0?viewport.ViewportHeight:viewport.ActualHeight;
            // Measure real controls instead of reserving an estimated height.
            // Keep their type and hit targets at the app's normal size.
            controls.Measure(new Windows.Foundation.Size(width,double.PositiveInfinity));
            double remaining=height-(hueOnly?0:controls.DesiredSize.Height+10)-4;
            double trayHeight=Math.Min(Math.Clamp(remaining,160,400),width*400/460);
            if(Math.Abs(disc.Height-trayHeight)>.5||double.IsNaN(disc.Height)) {
                disc.Height=trayHeight;disc.Width=trayHeight*460/400;
            }
        } finally {fittingDisc=false;}
    }
    private static Slider MakeSlider(string name)
    {
        var slider=new Slider {Style=StyleOf("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=1,SmallChange=1,LargeChange=10,MinWidth=0,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(slider,name);return slider;
    }
    private static Grid SliderRow(string name,Slider slider,ScrubNumberInput number)
    {
        var row=new Grid {ColumnSpacing=8};
        row.ColumnDefinitions.Add(new() {Width=new(48)});row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() {Width=new(62)});row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        Add(row,Label(name),0);Add(row,slider,1);Add(row,number,2);Add(row,Label("%","UtilityHint"),3);return row;
    }
    private Grid RecentRow()
    {
        var row=new Grid {ColumnSpacing=12,Margin=new(4,0,4,0)};
        row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.ColumnDefinitions.Add(new());Add(row,Label(UiLanguage.Text("最近使用")),0);
        var colors=new Grid {ColumnSpacing=6};
        foreach(var item in recent.ToArray()) {
            int column=colors.ColumnDefinitions.Count;colors.ColumnDefinitions.Add(new());
            var ring=new Border {Width=26,Height=26,CornerRadius=new(13),Background=new SolidColorBrush(item),BorderBrush=BrushOf("HomeControlEdge"),BorderThickness=new(1)};
            var button=new Button {Style=StyleOf("HomeIconButton"),Width=38,Height=38,CornerRadius=new(19),Content=ring};
            AutomationProperties.SetName(button,UiLanguage.Text("使用颜色 ")+Hex(item));
            button.Click+=(_,_)=>SetColor(item,true);Add(colors,button,column);swatchRings.Add((ring,item));
        }
        Add(row,colors,1);return row;
    }

    private void SetColor(Color next,bool notify)
    {
        double r=next.R/255d,g=next.G/255d,b=next.B/255d;
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        if(delta>0)hue=((max==r?(g-b)/delta:max==g?2+(b-r)/delta:4+(r-g)/delta)*60+360)%360;
        // Retain hue at greys and saturation at black so dragging either HSV
        // slider back up restores the user's chosen color instead of red.
        if(max>0)s=delta/max;
        v=max;
        if(hueOnly) {s=v=1;next=FromHsv(hue,s,v);}
        Publish(Rgb(next.R,next.G,next.B),notify);
    }
    private void ApplyHsv()=>Publish(FromHsv(hue,s,v),true);
    private void Publish(Color next,bool notify)
    {
        bool different=!Same(color,next);color=next;Refresh();
        if(notify&&different) {changed=true;ColorChanged?.Invoke(color);}
    }
    private void Refresh()
    {
        updating=true;
        try {
            disc.Update(hue,s,v,color);swatch.Background=new SolidColorBrush(color);
            saturation.Value=Math.Round(s*100);brightness.Value=Math.Round(v*100);
            saturationInput.Configure(0,100,s*100,1,0,"");brightnessInput.Configure(0,100,v*100,1,0,"");
            channels[0].Configure(0,255,color.R,1,0,"");channels[1].Configure(0,255,color.G,1,0,"");channels[2].Configure(0,255,color.B,1,0,"");
            if(hex.FocusState==FocusState.Unfocused)hex.Text=Hex(color);
            foreach(var (ring,item) in swatchRings) {ring.BorderBrush=BrushOf(Same(item,color)?"HomeControlText":"HomeControlEdge");ring.BorderThickness=new(Same(item,color)?2:1);}
        } finally {updating=false;}
    }
    private void ResetHex() {updating=true;try {hex.Text=Hex(color);}finally {updating=false;}}
    private void CommitHex(bool restoreInvalid=true)
    {
        if(TryHex(hex.Text,out var next)) {if(!Same(color,next))ScrubNumberInput.RunTransaction(this,()=>SetColor(next,true));hexHint.Visibility=Visibility.Collapsed;}
        else {hexHint.Visibility=Visibility.Visible;if(!restoreInvalid)return;}
        ResetHex();
    }
    private void Remember()
    {
        if(!changed||hueOnly)return;
        recent.RemoveAll(item=>Same(item,color));recent.Insert(0,color);
        if(recent.Count>6)recent.RemoveRange(6,recent.Count-6);
        changed=false;
    }
    private static bool TryHex(string text,out Color result)
    {
        string value=text.Trim();if(value.StartsWith('#'))value=value[1..];
        if(value.Length==6&&uint.TryParse(value,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out uint rgb)) {result=Rgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);return true;}
        result=default;return false;
    }
    private static Color FromHsv(double degrees,double saturation,double value)
    {
        double h=(degrees%360+360)%360/60,c=value*saturation,x=c*(1-Math.Abs(h%2-1)),m=value-c;
        var (r,g,b)=h switch {<1=>(c,x,0d),<2=>(x,c,0d),<3=>(0d,c,x),<4=>(0d,x,c),<5=>(x,0d,c),_=>(c,0d,x)};
        return Rgb((byte)Math.Clamp(Math.Round((r+m)*255),0,255),(byte)Math.Clamp(Math.Round((g+m)*255),0,255),(byte)Math.Clamp(Math.Round((b+m)*255),0,255));
    }
}
