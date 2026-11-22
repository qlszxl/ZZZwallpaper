using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class HomeRestoreRegressionProgram
{
    internal static string Output="";
    [STAThread] public static void Main(string[] args)
    {
        Output=Path.GetFullPath(args[0]);Directory.CreateDirectory(Output);
        _=Task.Run(async()=>{await Task.Delay(90000);Fail(new TimeoutException("Home restoration UI timed out"));});
        try {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters=>{
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        }catch(Exception ex){Fail(ex);}
    }
    internal static void Fail(Exception ex){File.WriteAllText(Path.Combine(Output,"failure.txt"),ex.ToString());Environment.Exit(1);}
}
public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    public App(){UnhandledException+=(_,e)=>HomeRestoreRegressionProgram.Fail(e.Exception);InitializeComponent();}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try {
            string output=HomeRestoreRegressionProgram.Output;AppLog.Initialize("HomeRestoreRegression",output);
            var store=new LibraryStore(Path.Combine(output,"data"));
            store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
            var window=new MainWindow(store);window.LoadedReady+=()=>window.CheckHomeRestoreUi(output);
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-20000,-20000,1440,1000));window.Activate();
        }catch(Exception ex){HomeRestoreRegressionProgram.Fail(ex);}
    }
}
public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> HomeRestoreElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){
            var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement element)yield return element;
            foreach(var descendant in HomeRestoreElements(child))yield return descendant;
        }
    }
    private async Task HomeRestoreCapture(UIElement element,string output,string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(element);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();
    }
    internal async void CheckHomeRestoreUi(string output)
    {
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
        async Task Settle(){Root.UpdateLayout();await Task.Delay(150);Root.UpdateLayout();}
        try {
            posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            await Settle();Navigate("wallpaper");
            Check(EmptyPreview.Visibility==Visibility.Visible,"Empty home shows the import entry");
            Check(GetType().Assembly.GetTypes().All(t=>!t.Name.StartsWith("Showcase",StringComparison.Ordinal)),"Retired showcase types are absent");
            Check(HomePreviewCard.Style==HomeStyle("HomeFrame"),"Home uses the original shared frame");
            Check(PosterImage.Stretch==Stretch.UniformToFill,"Home cover uses the original fill behavior");
            Check(HomeRestoreElements(HomePreviewCard).OfType<CassetteScrew>().Count()==0,"Home contains no television screws");
            Check(EffectsGlobalNavigation.Children.OfType<CassetteNavButton>().Select(button=>(string)button.Tag).SequenceEqual(new[]{"wallpaper","library","display","widgets","presets","settings"}),"Six cassette navigation entries place presets immediately before settings");
            await HomeRestoreCapture(Root,output,"empty-home");
            PosterImage.Source=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","EffectPreviews","landscape.png")));
            EmptyPreview.Visibility=Visibility.Collapsed;HomeAssetTitle.Visibility=PreviewMeta.Visibility=Visibility.Visible;
            HeroLabel.Text="当前壁纸 · 视频封面";PlaybackStatus.Text="播放中 · 预览";PreviewTitle.Text="预览";PreviewMeta.Text="2560 × 1440  ·  H264  ·  00:12";
            ApplyButton.Click-=Apply_Click;HomeApplyAllButton.Click-=ApplyAll_Click;PauseButton.Click-=Pause_Click;
            HomePreviousButton.Click-=PlaybackPrevious_Click;HomeNextButton.Click-=PlaybackNext_Click;
            int invoked=0;
            var controls=new[]{HomePreviousButton,PauseButton,HomeNextButton,ApplyButton,HomeApplyAllButton};
            foreach(var button in controls){button.IsEnabled=true;button.Click+=(_,_)=>invoked++;}
            foreach(int width in new[]{1440,1100,960}){
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width,1000));await Settle();
                Check(HomePosterViewport.ActualWidth>0&&HomePosterViewport.ActualHeight>=190,"Cover has visible space at "+width);
                Check(HomePreviewCard.ActualWidth<=WallpaperPanel.ActualWidth+1,"Preview fits page at "+width);
                foreach(var button in controls){
                    var point=button.TransformToVisual(HomePreviewCard).TransformPoint(new Windows.Foundation.Point(0,0));
                    Check(point.X>=0&&point.Y>=0&&point.X+button.ActualWidth<=HomePreviewCard.ActualWidth+1&&point.Y+button.ActualHeight<=HomePreviewCard.ActualHeight+1,"Control fits preview: "+button.Name+" / "+width);
                    Check(button.Focus(FocusState.Keyboard),"Control accepts focus: "+button.Name+" / "+width);
                    var peer=FrameworkElementAutomationPeer.CreatePeerForElement(button)!;
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();await Settle();
                }
                await HomeRestoreCapture(Root,output,"home-"+width);
                File.AppendAllText(Path.Combine(output,"dimensions.txt"),$"{width}: root {Root.ActualWidth},{Root.ActualHeight}; cover {HomePosterViewport.ActualWidth},{HomePosterViewport.ActualHeight}; rows {HomeTransport.RowDefinitions.Count}\n");
            }
            Check(invoked==15,"All five home buttons invoke at all three window widths");
            foreach(var page in new[]{"library","display","widgets","settings","wallpaper"}){
                Navigate(page);await Settle();
                Check(EffectsBodyHost.IsEnabled,"Navigation is not blocked by a retired overlay: "+page);
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,1000));await Settle();
            // Inspect every instantiated switch's actual Style, then render each distinct
            // template through the same coordinator used by real pointer hover and focus.
            var switches=GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Where(f=>f.FieldType==typeof(ToggleSwitch)).Select(f=>(ToggleSwitch?)f.GetValue(this)).Where(t=>t!=null).Cast<ToggleSwitch>().ToArray();
            var styles=switches.Select(t=>(Style?)t.Style).Distinct().ToList();styles.Add(null);
            var probePanel=new StackPanel {Spacing=18,Padding=new Thickness(24),Background=HomeBrush("HomeControlSurface")};
            var probeHost=new ContentControl {Content=probePanel,Width=550,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetRowSpan(probeHost,2);Root.Children.Add(probeHost);
            var coordinatorProperty=(DependencyProperty)typeof(InteractionFocus).GetField("CoordinatorProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var coordinator=(InteractionFocus.Coordinator)Root.GetValue(coordinatorProperty);
            int index=0;
            foreach(var style in styles.Distinct()){
                var probe=new ToggleSwitch {Header="开关轮廓检查 "+index,OnContent="开启",OffContent="原方向"};
                if(style!=null)probe.Style=style;
                probePanel.Children.Add(probe);await Settle();
                foreach(bool on in new[]{false,true}){
                    probe.IsOn=on;await Settle();coordinator.SetActive(true);Check(probe.Focus(FocusState.Keyboard),"Toggle focus "+index+" / "+on);coordinator.Refresh();await Settle();
                    var surface=InteractionFocus.SurfaceFor(probe);
                    Check(surface is Grid grid&&grid.Name=="SwitchFeedbackSurface"&&grid.CornerRadius.TopLeft==10&&surface.ActualWidth==40&&surface.ActualHeight==20,"Capsule feedback excludes label and has radius 10: "+index+" / "+on);
                    var overlay=(ContainerVisual)ElementCompositionPreview.GetElementChildVisual(surface);
                    Check(overlay.Size.X==40&&overlay.Size.Y==20&&overlay.Opacity==1,"Native Composition outline follows actual track: "+index+" / "+on);
                    Check(surface.Opacity==1,"On state never hides feedback surface "+index+" / "+on);
                }
                probe.IsEnabled=false;coordinator.Refresh();await Settle();
                Check(InteractionFocus.Target(probe)==null,"Disabled toggle cannot receive feedback "+index);probe.IsEnabled=true;index++;
            }
            await HomeRestoreCapture(probeHost,output,"toggle-layout");probePanel.Children.Clear();
            // Exercise both the shared production style and a native slider. Target
            // resolution is the same path used by hover, pointer press and focus.
            foreach(var style in new Style?[]{HomeStyle("HomeSlider"),null}) {
                var slider=new Slider {Minimum=0,Maximum=100,Value=25,Width=450};
                if(style!=null)slider.Style=style;
                probePanel.Children.Add(slider);await Settle();
                foreach(var orientation in new[]{Orientation.Horizontal,Orientation.Vertical}) {
                    slider.Orientation=orientation;slider.Height=orientation==Orientation.Vertical?140:40;await Settle();
                    Check(slider.Focus(FocusState.Keyboard),"Slider retains keyboard focus: "+orientation+" / "+(style!=null));coordinator.Refresh();
                    Check(InteractionFocus.IsInteractive(slider),"Slider still classified as interactive: "+orientation+" / "+(style!=null));
                    Check(InteractionFocus.Target(slider)==null&&HomeRestoreElements(slider).All(e=>InteractionFocus.Target(e)==null),"Slider and thumb never acquire shared outline: "+orientation+" / "+(style!=null));
                    var peer=FrameworkElementAutomationPeer.CreatePeerForElement(slider)!;
                    ((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)).SetValue(65);
                    Check(slider.Value==65,"Slider range interaction remains functional: "+orientation+" / "+(style!=null));
                    Check(ElementCompositionPreview.GetElementChildVisual(InteractionFocus.SurfaceFor(slider))==null,"Focused slider has no Composition feedback layer: "+orientation+" / "+(style!=null));
                    slider.Value=25;
                }
                slider.Orientation=Orientation.Horizontal;slider.Height=40;
            }
            await Settle();await HomeRestoreCapture(probeHost,output,"slider-layout");Root.Children.Remove(probeHost);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks,switchCount=switches.Length,styleCount=index,productionDataAccessed=false,wallpaperStarted=false},new JsonSerializerOptions {WriteIndented=true}));
            Close();store.Dispose();App.ChildProcesses.Dispose();Environment.Exit(0);
        }catch(Exception ex){HomeRestoreRegressionProgram.Fail(ex);}
    }
}
