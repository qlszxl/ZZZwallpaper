using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<object> refinementMeasurements=[];
    private Border? refinementOverlay;
    private bool refinementNativeWheelVerified;
    private static IEnumerable<FrameworkElement> RefinementVisuals(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in RefinementVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal async void CheckSettingsRefinements(string output)
    {
        try {
            await ReorderSettle();ReorderCheckIsolation();
            await CheckDiscColorRendering(output);
            await CheckOptionWheelRouting(output);
            await CheckCollapseSectionsUi(output);
            ReorderCheckIsolation();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks,
                scope="Off-screen rendered HSV disc, option template hook and actual scroll-container behavior, actual collapsible effects and disabled widget editors. Native wheel message delivery is probed separately; when unavailable, scroll behavior is driven through the production forwarding method.",
                nativeWheelChainVerified=refinementNativeWheelVerified,
                foregroundActivated=false,productionDataAccessed=false,desktopHostsStarted=false},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;
        } finally {
            File.WriteAllText(Path.Combine(output,"measurements.json"),JsonSerializer.Serialize(refinementMeasurements,new JsonSerializerOptions {WriteIndented=true}));
            if(refinementOverlay!=null)Root.Children.Remove(refinementOverlay);
            widgets?.Dispose();widgets=null;effectSession.StopSchedule();
            ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.Observe("before settings fixture close");App.ReorderIsolation?.WriteReport();
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
    private void ShowRefinementOverlay(UIElement content,double width=680,double height=860)
    {
        if(refinementOverlay!=null)Root.Children.Remove(refinementOverlay);
        refinementOverlay=new Border {Width=width,Height=height,Padding=new(24),HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Color.FromArgb(255,35,35,35)),Child=content};
        Grid.SetRowSpan(refinementOverlay,Root.RowDefinitions.Count);Root.Children.Add(refinementOverlay);
    }
    private async Task<(byte R,byte G,byte B)> DiscSample(DiscHueSelector disc,string output,string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(disc);
        byte[] pixels=(await bitmap.GetPixelsAsync()).ToArray();
        // Logical (230,350) is well inside the hue ring, outside the hub and
        // far from the marker at hue zero. Scale independently of monitor DPI.
        int x=(int)Math.Round(bitmap.PixelWidth*230d/460),y=(int)Math.Round(bitmap.PixelHeight*350d/400);
        int index=(y*bitmap.PixelWidth+x)*4;
        var sample=(R:pixels[index+2],G:pixels[index+1],B:pixels[index]);
        refinementMeasurements.Add(new {name,red=sample.R,green=sample.G,blue=sample.B,width=bitmap.PixelWidth,height=bitmap.PixelHeight});
        using var file=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,file.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
        return sample;
    }
    private async Task CheckDiscColorRendering(string output)
    {
        foreach(var (name,value) in new[]{("disc-initial-white",(byte)255),("disc-initial-grey",(byte)128),("disc-initial-black",(byte)0)}) {
            var picker=new DiscColorPicker(Color.FromArgb(255,value,value,value));
            ShowRefinementOverlay(new ScrollViewer {Content=picker,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            await ReorderSettle(320);
            var disc=RefinementVisuals(picker).OfType<DiscHueSelector>().Single();
            var sample=await DiscSample(disc,output,name);
            ReorderCheck(Math.Abs(sample.R-value)<=3&&Math.Abs(sample.G-value)<=3&&Math.Abs(sample.B-value)<=3,
                "The rendered hue ring matches its initial achromatic color: "+name);
            ReorderCheck(disc.Focus(FocusState.Programmatic)&&InteractionFocus.Target(disc)==null&&!disc.UseSystemFocusVisuals,
                "Focusing the disc retains keyboard input without the outer yellow feedback frame: "+name);
            ReorderCheck(RefinementVisuals(disc).OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().Count(ellipse=>ellipse.StrokeThickness>=2)>=2,
                "The actual hue marker and its contrasting edge remain present: "+name);
        }
        var colorPicker=new DiscColorPicker(Microsoft.UI.Colors.Red);
        ShowRefinementOverlay(new ScrollViewer {Content=colorPicker,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        await ReorderSettle(300);
        var colorDisc=RefinementVisuals(colorPicker).OfType<DiscHueSelector>().Single();
        var controls=RefinementVisuals(colorPicker).OfType<Slider>().ToArray();
        var saturation=controls.Single(c=>AutomationProperties.GetName(c)=="饱和度");
        var brightness=controls.Single(c=>AutomationProperties.GetName(c)=="明度");
        var full=await DiscSample(colorDisc,output,"disc-full-color");
        saturation.Value=50;await ReorderSettle(90);
        var halfS=await DiscSample(colorDisc,output,"disc-half-saturation");
        ReorderCheck(Math.Abs(halfS.R-(full.R+255)/2d)<4&&Math.Abs(halfS.G-(full.G+255)/2d)<4&&Math.Abs(halfS.B-(full.B+255)/2d)<4,
            "Changing saturation immediately desaturates the rendered ring, including unselected hues");
        brightness.Value=40;await ReorderSettle(90);
        var halfSV=await DiscSample(colorDisc,output,"disc-half-saturation-low-value");
        ReorderCheck(Math.Abs(halfSV.R-halfS.R*.4)<4&&Math.Abs(halfSV.G-halfS.G*.4)<4&&Math.Abs(halfSV.B-halfS.B*.4)<4,
            "Changing brightness immediately darkens the ring while retaining saturation");
        saturation.Value=0;brightness.Value=100;await ReorderSettle(90);
        var white=await DiscSample(colorDisc,output,"disc-slider-white");
        ReorderCheck(white.R>=252&&white.G>=252&&white.B>=252,"Zero saturation and full brightness render white");
        brightness.Value=0;saturation.Value=73;await ReorderSettle(90);
        var black=await DiscSample(colorDisc,output,"disc-slider-black");
        ReorderCheck(black.R<=3&&black.G<=3&&black.B<=3&&saturation.Value==73,"Changing saturation at zero brightness retains black and the HSV saturation setting");
        brightness.Value=100;await ReorderSettle(90);
        ReorderCheck(colorPicker.Color.R==255&&colorPicker.Color.G is >=68 and <=70&&colorPicker.Color.B is >=68 and <=70,
            "Restoring brightness from black uses the retained saturation and hue");
        await ReorderCapture(output,"color-picker-adjusted");Root.Children.Remove(refinementOverlay!);refinementOverlay=null;
    }

    private delegate bool RefinementEnumWindow(nint window,nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent,RefinementEnumWindow callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint window,StringBuilder text,int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint processId);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref RefinementPoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window,ref RefinementPoint point);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessage(nint window,uint message,nuint wParam,nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RefinementPoint {public int X,Y;}
    private nint RefinementInputWindow()
    {
        nint root=WinRT.Interop.WindowNative.GetWindowHandle(this),input=0;
        EnumChildWindows(root,(window,_)=> {
            var text=new StringBuilder(256);GetClassName(window,text,text.Capacity);GetWindowThreadProcessId(window,out uint pid);
            refinementMeasurements.Add(new {nativeWindow=window.ToInt64(),className=text.ToString(),pid});
            if(pid==(uint)Environment.ProcessId&&text.ToString().Contains("InputSite",StringComparison.OrdinalIgnoreCase))input=window;
            return true;
        },0);
        ReorderCheck(input!=0,"The wheel fixture identifies its own WinUI input-site child HWND");
        return input;
    }
    private async Task WheelOver(nint input,FrameworkElement element,int delta=-120)
    {
        nint window=WinRT.Interop.WindowNative.GetWindowHandle(this);
        GetWindowThreadProcessId(input,out uint pid);
        ReorderCheck(pid==(uint)Environment.ProcessId,"A wheel message is restricted to this fixture process");
        var at=element.TransformToVisual(Root).TransformPoint(new Point(element.ActualWidth/2,element.ActualHeight/2));
        double scale=Root.XamlRoot.RasterizationScale;
        var point=new RefinementPoint {X=(int)Math.Round(at.X*scale),Y=(int)Math.Round(at.Y*scale)};
        ReorderCheck(ClientToScreen(window,ref point)&&point.X<-10000&&point.Y<-10000,"The targeted option remains outside the visible desktop");
        var local=point;ScreenToClient(input,ref local);
        static nint Pack(int x,int y)=>(nint)(uint)((ushort)x|((uint)(ushort)y<<16));
        PostMessage(input,0x0200,0,Pack(local.X,local.Y));
        ReorderCheck(PostMessage(input,0x020A,(nuint)(uint)((ushort)delta<<16),Pack(point.X,point.Y)),"A native wheel message is queued only to the isolated input HWND");
        await ReorderSettle(160);
    }
    private async Task CheckOptionWheelRouting(string output)
    {
        var combo=new ComboBox {Style=HomeStyle("HomeComboBox"),ItemsSource=Enumerable.Range(0,30).Select(i=>"选项 "+i).ToArray(),SelectedIndex=8,Width=330};
        var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,Value=50,Width=330};
        var number=new NumberBox {Style=HomeStyle("HomeNumberBox"),Minimum=0,Maximum=100,Value=50,Width=330};
        var scrub=new ScrubNumberInput("滚轮数值") {Width=330};scrub.Configure(0,100,50,1,0,"");
        var body=new StackPanel {Spacing=28};
        body.Children.Add(combo);body.Children.Add(slider);body.Children.Add(number);body.Children.Add(scrub);
        body.Children.Add(new Border {Height=1500});
        var scroll=new ScrollViewer {Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        ShowRefinementOverlay(scroll,600,620);await ReorderSettle(300);
        nint input=RefinementInputWindow();
        int received=0;PointerEventHandler observe=(_,_)=>received++;Root.AddHandler(UIElement.PointerWheelChangedEvent,observe,true);
        try {
            combo.Focus(FocusState.Programmatic);await WheelOver(input,combo);
            if(received==0) {
                input=WinRT.Interop.WindowNative.GetWindowHandle(this);await WheelOver(input,combo);
            }
            refinementNativeWheelVerified=received>0;
            refinementMeasurements.Add(new {nativeWheelChainVerified=refinementNativeWheelVerified,
                limitation=refinementNativeWheelVerified?"":"Targeted WM_MOUSEWHEEL messages to the fixture input child and root HWND did not produce WinUI pointer events. No global input or activation was used."});
            foreach(var control in new Control[]{combo,slider,number,scrub}) {
                scroll.ChangeView(null,0,null,true);await ReorderSettle(80);
                if(control is ScrubNumberInput custom)custom.DragSurface.Focus(FocusState.Programmatic);else control.Focus(FocusState.Programmatic);
                var table=(System.Runtime.CompilerServices.ConditionalWeakTable<Control,OptionWheelGuard>)typeof(OptionWheelGuard).GetField("states",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                ReorderCheck(table.TryGetValue(control,out var guard)&&ReferenceEquals(typeof(OptionWheelGuard).GetField("surface",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(guard),VisualTreeHelper.GetChild(control,0)),
                    "The loaded option has its wheel handler attached before its owning native control: "+control.GetType().Name);
                int oldEvents=received;int selected=combo.SelectedIndex;double oldSlider=slider.Value,oldNumber=number.Value,oldScrub=scrub.Value;
                if(refinementNativeWheelVerified) {
                    await WheelOver(input,control);
                    ReorderCheck(received>oldEvents,"Native wheel input reaches the real WinUI routed event: "+control.GetType().Name);
                } else {OptionWheelGuard.ScrollContainer(control,-120);await ReorderSettle(100);}
                refinementMeasurements.Add(new {control=control.GetType().Name,received,offset=scroll.VerticalOffset,selected=combo.SelectedIndex,slider=slider.Value,number=number.Value,scrub=scrub.Value});
                ReorderCheck(combo.SelectedIndex==selected&&slider.Value==oldSlider&&number.Value==oldNumber&&scrub.Value==oldScrub,
                    "Forwarding wheel scrolling leaves focused option values unchanged: "+control.GetType().Name);
                ReorderCheck(scroll.VerticalOffset>0,"The option wheel forwarding method scrolls its actual surrounding page: "+control.GetType().Name);
                ReorderCheckIsolation();
            }
            // Negative control proves the same targeted event chain can alter
            // NumberBox when native pointer delivery is available on the host.
            if(refinementNativeWheelVerified) {
                scroll.ChangeView(null,0,null,true);await ReorderSettle(80);
                RefinementVisuals(number).OfType<TextBox>().Single().Focus(FocusState.Programmatic);
                OptionWheelGuard.SetEnabled(number,false);double before=number.Value;await WheelOver(input,number);
                ReorderCheck(number.Value!=before,"Without the guard the same native wheel message changes the focused NumberBox");
                OptionWheelGuard.SetEnabled(number,true);
            }
            slider.Value=42;combo.SelectedIndex=12;number.Value=63;scrub.Value=28;
            ReorderCheck(slider.Value==42&&combo.SelectedIndex==12&&number.Value==63&&scrub.Value==28,"Option guards permit normal explicit value and selection updates");
            body.Children.Remove(slider);await ReorderSettle(70);body.Children.Insert(1,slider);await ReorderSettle(70);
            scroll.ChangeView(null,0,null,true);await ReorderSettle(70);OptionWheelGuard.ScrollContainer(slider,-120);await ReorderSettle(80);
            ReorderCheck(scroll.VerticalOffset>0&&slider.Value==42,"A reloaded option retains its value and scroll-container behavior");
            await ReorderCapture(output,"option-wheel-fixture");
        } finally {Root.RemoveHandler(UIElement.PointerWheelChangedEvent,observe);Root.Children.Remove(refinementOverlay!);refinementOverlay=null;}
    }
}
