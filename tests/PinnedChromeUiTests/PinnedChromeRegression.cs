using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class PinnedChromeRegressionProgram
{
    internal static string Output="";
    [STAThread] public static void Main(string[] args)
    {
        Output=Path.GetFullPath(args[0]);Directory.CreateDirectory(Output);
        _=Task.Run(async()=>{await Task.Delay(180000);Fail(new TimeoutException("Pinned UI timed out"));});
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters=>{
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _=new App();
        });
    }
    internal static void Fail(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Environment.Exit(1);}
}
public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    public App(){UnhandledException+=(_,e)=>PinnedChromeRegressionProgram.Fail(e.Exception);InitializeComponent();}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string output=PinnedChromeRegressionProgram.Output;AppLog.Initialize("PinnedChromeRegression",output);
        var store=new LibraryStore(Path.Combine(output,"data"));
        store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
        var window=new MainWindow(store);window.LoadedReady+=()=>window.CheckPinnedChrome(output);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-20000,-20000,1440,1000));window.Activate();
    }
}
public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> PinnedElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){
            var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement element)yield return element;
            foreach(var nested in PinnedElements(child))yield return nested;
        }
    }
    private async Task PinnedCapture(string output,string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();
    }
    internal async void CheckPinnedChrome(string output)
    {
        var checks=new List<string>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);File.AppendAllText(Path.Combine(output,"checks.txt"),message+"\n");}
        async Task Settle(){Root.UpdateLayout();await Task.Delay(160);Root.UpdateLayout();}
        double Y(FrameworkElement element)=>element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y;
        ScrollViewer? ScrollOwner(DependencyObject node){
            for(var parent=VisualTreeHelper.GetParent(node);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll)return scroll;
            return null;
        }
        ScrollBar Bar(ScrollViewer viewer)=>PinnedElements(viewer).OfType<ScrollBar>().First(b=>b.Orientation==Orientation.Vertical&&b.Maximum>0&&ReferenceEquals(ScrollOwner(b),viewer));
        async Task CheckIdle(ScrollViewer viewer,string label){
            await Settle();Check(viewer.ScrollableHeight>0,label+": overflowing content");
            var bar=Bar(viewer);Check(ScrollBarAutoHide.GetEnabled(bar),label+": unified idle behavior attached");
            double width=viewer.ViewportWidth;
            viewer.ChangeView(null,Math.Min(70,viewer.ScrollableHeight),null,true);await Settle();
            Check(ScrollBarAutoHide.IsRevealed(bar)&&bar.Opacity>.99,label+": native scroll reveals bar");
            await Task.Delay(1850);
            Check(!ScrollBarAutoHide.IsRevealed(bar)&&bar.Opacity<.01,label+": whole bar hides after idle");
            Check(Math.Abs(viewer.ViewportWidth-width)<.1&&viewer.VerticalOffset>0,label+": hiding preserves viewport and offset");
            viewer.ChangeView(null,0,null,true);await Settle();
            Check(bar.Opacity>.99,label+": new scroll wakes bar");
        }
        try {
            posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            string screen=SelectedScreen!.DeviceName;
            for(int i=0;i<24;i++){
                string path=Path.Combine(output,"sample-"+i+".png");File.WriteAllText(path,"UI fixture only");
                store.Upsert(new("asset-"+i,path,"很长的素材名称用于验证紧凑作用对象不会撑坏标题布局-"+i,12,0,2560,1440,"png","SDR",""));
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets/EffectPreviews/landscape.png"),store.Poster("asset-"+i),true);
            }
            store.SaveTargetEffects(screen,null,new(FillMode.Contain,new VideoEffects {Collection=new() {Added=effectDefinitions.Aggregate((EffectKind)0,(all,d)=>all|d.Kind)},Glow=true,GlowStrength=.3}),VideoEffects.Original);
            widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));
            widgets.Save(new WidgetConfiguration {Widgets=Enumerable.Range(0,24).Select(i=>new WidgetInstance {Id="widget-"+i,Kind=WidgetKind.Text,Screen=screen,Enabled=false}).ToArray()});
            RefreshLibrary();Navigate("display");LoadEffectDraft(true);await Settle();
            Check(EffectsTargetButton.Parent==EffectsHeaderActions,"Target control lives in page header");
            Check(EffectsTargetButton.ActualWidth<=320&&EffectsTargetButton.ActualHeight>=44,"Compact target retains usable minimum size");
            await PinnedCapture(output,"effects-top");
            foreach(int width in new[]{1440,1100,960}){
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width,1000));await Settle();
                foreach(string page in new[]{"display","widgets"}){
                    Navigate(page);await Settle();
                    var viewer=page=="display"?EffectsParameterScroll:WidgetsScroll;
                    var button=page=="display"?EffectsAddButton:WidgetsAddButton;
                    double top=Y(button),contentTop=Y(viewer);
                    Check(button.Visibility==Visibility.Visible&&contentTop>=top+button.ActualHeight,page+": scroll viewport starts below fixed add at "+width);
                    viewer.ChangeView(null,viewer.ScrollableHeight,null,true);await Settle();
                    Check(viewer.VerticalOffset>0&&Math.Abs(Y(button)-top)<.1,page+": add button stays fixed while content scrolls at "+width);
                    await PinnedCapture(output,page+"-"+width);
                }
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,1000));Navigate("display");await Settle();
            OpenEffect(EffectKind.Color);await Settle();
            Check(EffectsAddButton.Visibility==Visibility.Visible&&EffectsTargetButton.IsLoaded&&EffectsBackActions.Children.Count==2,"Effect detail retains fixed add and target beside return and remove actions");
            Check(EffectsDetailTitle.Visibility==Visibility.Visible&&EffectsCountText.Visibility==Visibility.Collapsed,"Detail header and target actions remain available");
            EffectsTargetOpen_Click(EffectsTargetButton,new RoutedEventArgs());await Settle();
            Check(!EffectsBodyHost.IsEnabled&&!EffectsTargetButton.IsEnabled&&!EffectsAddButton.IsEnabled,"Target modal blocks all fixed actions");
            CheckIdleBarsIn(EffectsTargetDrawer,"Target modal");
            CloseEffectsTarget(true);await Settle();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),EffectsTargetButton),"Closing target modal restores focus to compact header control");
            SelectEffectTarget("asset-0");await Settle();
            Check(effectTargetAssetId=="asset-0"&&EffectsCopyButton.Visibility==Visibility.Visible&&AutomationProperties.GetName(EffectsTargetButton).Contains("图片"),"Asset target exposes existing migration action and correct identity");
            Check(AutomationProperties.GetName(EffectsTargetButton).Contains("很长的素材名称"),"Automation retains complete target name");
            EffectsCopy_Click(EffectsCopyButton,new RoutedEventArgs());Check(effectTargetAssetId=="asset-0","Copy keeps explicit target selection");
            EffectsClear_Click(EffectsClearButton,new RoutedEventArgs());Check(store.ScreenProfile(screen)!.Effects.Glow,"Clearing asset keeps display effects intact");
            SelectEffectTarget(null);EffectsBack();await Settle();await CheckIdle(EffectsParameterScroll,"Effects");
            EffectsAdd_Click(EffectsAddButton,new RoutedEventArgs());await Settle();CheckIdleBarsIn(EffectsAddDrawer,"Effect picker");CloseEffectsAdd(true);await Settle();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),EffectsAddButton),"Effect picker returns focus to pinned add");
            Navigate("widgets");OpenWidget("widget-0");await Settle();
            Check(WidgetsAddButton.Visibility==Visibility.Visible&&WidgetEditorPanel.Visibility==Visibility.Visible,"Widget detail retains pinned add");
            WidgetsAdd_Click(WidgetsAddButton,new RoutedEventArgs());await Settle();CheckIdleBarsIn(WidgetDrawer,"Widget picker");CloseWidgetDrawer(true);await Settle();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),WidgetsAddButton),"Widget picker returns focus to pinned add");
            WidgetBack();await Settle();await CheckIdle(WidgetsScroll,"Widgets");
            Navigate("library");await Settle();
            var libraryScroll=PinnedElements(LibraryList).OfType<ScrollViewer>().First(v=>v.ScrollableHeight>0);await CheckIdle(libraryScroll,"Virtualized library");
            foreach(var page in new[]{"wallpaper","settings","appearance","sharing","history","performance"}){
                Navigate(page);await Settle();Check(PagePinnedActions.Visibility==Visibility.Collapsed,"No pinned add on "+page);CheckIdleBarsIn(Root,page);
            }
            Navigate("wallpaper");FillPicker.IsDropDownOpen=true;await Settle();
            foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))CheckIdleBarsIn(popup.Child,"Dropdown");FillPicker.IsDropDownOpen=false;
            var menu=new MenuFlyout();for(int i=0;i<50;i++)menu.Items.Add(new MenuFlyoutItem {Text="测试选项 "+i});menu.ShowAt(HomeMoreButton);await Settle();
            foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))CheckIdleBarsIn(popup.Child,"Menu flyout");menu.Hide();
            // Native (non-HomeScrollViewer) and nested viewports must participate too.
            var inner=new ScrollViewer {Height=130,Content=new Border {Height=800},VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var stack=new StackPanel();stack.Children.Add(inner);stack.Children.Add(new Border {Height=900});
            var outer=new ScrollViewer {Width=320,Height=240,Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            Grid.SetRowSpan(outer,2);Root.Children.Add(outer);await Settle();
            await Task.Delay(1800);inner.ChangeView(null,100,null,true);await Settle();
            var innerBar=Bar(inner);var outerBar=Bar(outer);
            Check(!ReferenceEquals(innerBar,outerBar),"Nested viewports resolve separate owned scrollbars");
            Check(innerBar.Opacity>.99&&outerBar.Opacity<.01,$"Nested scrolling only reveals its own bar (inner={innerBar.Opacity}, outer={outerBar.Opacity})");
            await CheckIdle(inner,"Native nested viewport");
            ScrollBarAutoHide.SetMotion(Root,()=>true);await CheckIdle(inner,"Animated idle fade");ScrollBarAutoHide.SetMotion(Root,()=>false);
            inner.ChangeView(null,80,null,true);await Settle();Root.Children.Remove(outer);await Task.Delay(1800);
            Check(!innerBar.IsLoaded&&!ScrollBarAutoHide.IsRevealed(innerBar)&&innerBar.Opacity<.01,"Unloaded scrollbar cancels its idle state");
#if SECTION_REGRESSION
            await CheckSectionDesign(output,Check);
#endif
            Check(widgets.ProcessId==null&&wallpapers==null,"Checks did not start wallpaper or widget hosts");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks,productionDataAccessed=false,actualInstallationRun=false},new JsonSerializerOptions {WriteIndented=true}));
            widgets.Dispose();ShutdownImports();store.Dispose();App.ChildProcesses.Dispose();Close();Environment.Exit(0);
        }catch(Exception e){PinnedChromeRegressionProgram.Fail(e);}
        void CheckIdleBarsIn(DependencyObject root,string label){
            foreach(var bar in PinnedElements(root).OfType<ScrollBar>())Check(ScrollBarAutoHide.GetEnabled(bar),label+": all realized scrollbars use shared idle behavior");
        }
    }
}
