using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class InteractionRegressionProgram
{
    [STAThread] public static void Main()
    {
        Directory.CreateDirectory(App.Output);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters=> {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _=new App();
        });
    }
}

public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    internal static string Output=Path.GetFullPath(Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(arg=>!arg.StartsWith("--",StringComparison.Ordinal))
        ??Path.Combine(AppContext.BaseDirectory,"interaction-results",DateTime.Now.ToString("yyyyMMdd-HHmmss")));
    internal static bool Interactive=Environment.GetCommandLineArgs().Length==1||Environment.GetCommandLineArgs().Contains("--interactive");
    public App()
    {
        InitializeComponent();
        UnhandledException+=(_,e)=> {
            File.WriteAllText(Path.Combine(Output,"unhandled.txt"),e.Exception.ToString());
            Environment.Exit(1);
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Initialize("InteractionRegression",Output);
        DebugSettings.IsXamlResourceReferenceTracingEnabled=true;
        DebugSettings.XamlResourceReferenceFailed+=(_,e)=>File.AppendAllText(Path.Combine(Output,"resources.txt"),e.Message+"\n");
        var store=new LibraryStore(Path.Combine(Output,"data"));
        store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
        var window=new MainWindow(store);
        window.LoadedReady+=()=>window.CheckInteractions(Output,Interactive);
        if(Interactive)
        {
            var area=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+24,area.Y+24,Math.Min(1440,area.Width-48),Math.Min(940,area.Height-48)));
        }
        else window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-20000,-20000,1440,940));
        window.Activate();
    }
}

public sealed partial class MainWindow
{
    private bool interactionFixtureClosed;
    private readonly List<string> interactionChecks=new();
    private readonly List<object> interactionVisuals=new();

    private static IEnumerable<FrameworkElement> InteractionElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is FrameworkElement element)yield return element;
            foreach(var descendant in InteractionElements(child))yield return descendant;
        }
    }

    private void InteractionCheck(bool value,string message)
    {
        if(!value)throw new InvalidOperationException(message);
        interactionChecks.Add(message);
        File.AppendAllText(Path.Combine(App.Output,"checks.txt"),message+"\n");
    }

    private async Task InteractionSettle()
    {
        Root.UpdateLayout();
        await Task.Delay(100);
        Root.UpdateLayout();
    }

    private static ContainerVisual? InteractionFindOverlay(Visual? visual)
    {
        if(visual is not ContainerVisual container)return null;
        if(container.Comment=="InteractionFeedback")return container;
        foreach(var child in container.Children)
            if(InteractionFindOverlay(child) is { } found)return found;
        return null;
    }

    private ContainerVisual InteractionOverlay(Control control)
    {
        var surface=InteractionFocus.SurfaceFor(control);
        var visual=InteractionFindOverlay(ElementCompositionPreview.GetElementChildVisual(surface));
        InteractionCheck(visual?.Comment=="InteractionFeedback",$"{control.Name}: feedback is attached to its actual template surface");
        return visual!;
    }

    private void InteractionFocused(Control expected,string label)
    {
        var actual=FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        InteractionCheck(InteractionFocus.Target(actual)==expected,label+": native focus resolves to the expected control");
        var surface=InteractionFocus.SurfaceFor(expected);
        var overlay=InteractionOverlay(expected);
        InteractionCheck(overlay.Opacity>.99f,label+": its own feedback visual is visible");
        InteractionCheck(Math.Abs(overlay.Size.X-surface.ActualWidth)<.1&&Math.Abs(overlay.Size.Y-surface.ActualHeight)<.1,
            label+": feedback size follows its own surface");
        var corners=overlay.Children.OfType<ShapeVisual>().Where(v=>v.Comment?.StartsWith("InteractionFeedback.Corner.",StringComparison.Ordinal)==true).ToArray();
        InteractionCheck(corners.Length==4,label+": four corner segments belong to that surface");
        var geometryData=new List<object>();double clippedArea=0;
        foreach(var corner in corners)
        {
            var shapes=corner.Shapes.OfType<CompositionSpriteShape>().ToArray();
            InteractionCheck(shapes.Length==1,label+": corner has one outline stroke");
            var stroke=shapes[0];
            var color=(stroke.StrokeBrush as CompositionColorBrush)?.Color;
            InteractionCheck(color is {A:255,R:229,G:245,B:0},label+": focus uses the accent outline");
            InteractionCheck(stroke.Geometry is CompositionRoundedRectangleGeometry,label+": corner has rounded rectangle geometry");
            var clip=corner.Clip as InsetClip;
            InteractionCheck(clip!=null,label+": each independent radius segment is clipped to its corner");
            double left=clip!.LeftInset,top=clip.TopInset;
            double right=overlay.Size.X-clip.RightInset,bottom=overlay.Size.Y-clip.BottomInset;
            InteractionCheck(left>=-.1&&top>=-.1&&right<=surface.ActualWidth+.1&&bottom<=surface.ActualHeight+.1&&right>left&&bottom>top,
                label+": corner clip stays within the surface bounds");
            clippedArea+=(right-left)*(bottom-top);
            geometryData.Add(new {corner.Comment,left,top,right,bottom});
        }
        InteractionCheck(Math.Abs(clippedArea-surface.ActualWidth*surface.ActualHeight)<1,
            label+": the four corner clips cover the complete surface");
        interactionVisuals.Add(new {label,control=expected.Name,surface=surface.Name,width=surface.ActualWidth,height=surface.ActualHeight,corners=geometryData});
    }

    private async Task InteractionFocusControl(Control control,string label,Control? previous=null)
    {
        control.StartBringIntoView(new BringIntoViewOptions {AnimationDesired=false});
        await InteractionSettle();
        InteractionCheck(control.Focus(FocusState.Pointer),label+": native pointer focus succeeds");
        await InteractionSettle();
        InteractionFocused(control,label);
        if(previous!=null&&previous!=control)
        {
            var old=InteractionOverlay(previous);
            InteractionCheck(old.Opacity<.01f,label+": the old control no longer shows a focus outline");
            InteractionCheck(old!=InteractionOverlay(control),label+": controls never share one feedback visual");
        }
    }

    private async Task InteractionCapture(string name)
    {
        var bitmap=new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        using var stream=File.Create(Path.Combine(App.Output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private void InteractionCloseFixture()
    {
        if(interactionFixtureClosed)return;
        interactionFixtureClosed=true;
        ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
        wallpapers?.Dispose();store.Dispose();App.ChildProcesses.Dispose();
    }

    private async void InteractionSimulateApply(object sender,RoutedEventArgs e)
    {
        if(applying>0)return;
        applying++;RefreshPlaybackStatus();
        await Task.Delay(350);
        if(interactionFixtureClosed)return;
        applying--;RefreshPlaybackStatus();
        Inform("隔离交互测试：已模拟应用忙碌状态，未启动壁纸。");
    }

    private void PrepareInteractionFixture(string output)
    {
        posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
        // The empty controller supplies availability checks only. No Restore,
        // Apply, preview, widget controller, desktop shell or MCP server runs.
        wallpapers=new WallpaperController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));
        ApplyButton.Click-=Apply_Click;HomeApplyAllButton.Click-=ApplyAll_Click;
        ApplyButton.Click+=InteractionSimulateApply;HomeApplyAllButton.Click+=InteractionSimulateApply;
        PlayQueueButton.Click-=PlayQueue_Click;
        HomePreviousButton.Click-=PlaybackPrevious_Click;HomeNextButton.Click-=PlaybackNext_Click;
        PlaybackPreviousButton.Click-=PlaybackPrevious_Click;PlaybackNextButton.Click-=PlaybackNext_Click;
        string posterSource=Path.Combine(AppContext.BaseDirectory,"Assets","Branding","wordmark.png");
        for(int i=0;i<12;i++)
        {
            string id="interaction-"+i;
            string path=Path.Combine(output,"sample-"+i+".mp4");
            File.WriteAllText(path,"Synthetic UI fixture; this is not playable media.");
            var asset=new MediaAsset(id,path,"交互测试视频 "+(i+1),23700000,12,2560,1440,"h264","SDR","Software");
            store.Upsert(asset);store.RememberAssetAudio(asset,false);
            File.Copy(posterSource,store.Poster(id),true);
        }
        RefreshLibrary();LoadPlaybackSettings();
        var first=store.Assets().First();
        homePreviewRequest=new HomePreviewRequest(first,SelectedScreen?.DeviceName,null);
        Navigate("wallpaper");RefreshStatus();
        Closed+=(_,_)=> {InteractionCloseFixture();Application.Current.Exit();};
    }

    internal async void CheckInteractions(string output,bool interactive)
    {
        try
        {
            PrepareInteractionFixture(output);
            await InteractionSettle();
            if(interactive)
            {
                Title="ZZZwallpaper · 隔离交互测试";
                File.WriteAllText(Path.Combine(output,"interactive-ready.json"),JsonSerializer.Serialize(new {ready=true,process=Environment.ProcessId,dataRoot=store.Root}));
                return;
            }
            await InteractionFocusControl(ApplyButton,"Apply current");
            applying++;RefreshPlaybackStatus();await InteractionSettle();
            InteractionCheck(ApplyButton.IsEnabled&&HomeApplyAllButton.IsEnabled,"Both apply controls stay focusable while an operation is busy");
            InteractionFocused(ApplyButton,"Busy apply current retains focus");
            applying--;RefreshPlaybackStatus();
            await InteractionFocusControl(HomeApplyAllButton,"Apply all",ApplyButton);
            var allOverlay=InteractionOverlay(HomeApplyAllButton);
            await InteractionFocusControl(FillPicker,"Fill picker",HomeApplyAllButton);
            InteractionCheck(InteractionOverlay(HomeApplyAllButton)==allOverlay,"Focus transfer preserves the old visual on its original host");
            await InteractionCapture("home-fill-focus-layout");

            FillPicker.IsDropDownOpen=true;await InteractionSettle();
            var option=FillPicker.ContainerFromIndex(1) as ComboBoxItem??FillPicker.Items[1] as ComboBoxItem
                ??throw new InvalidOperationException("Fill popup item was not realized");
            await InteractionFocusControl(option,"Fill popup option",FillPicker);
            FillPicker.IsDropDownOpen=false;await InteractionSettle();
            await InteractionFocusControl(HomeMoreButton,"More menu button");
            var flyout=(MenuFlyout)HomeMoreButton.Flyout;
            flyout.ShowAt(HomeMoreButton);await InteractionSettle();
            var menuItem=(MenuFlyoutItem)flyout.Items[0];
            await InteractionFocusControl(menuItem,"Flyout menu item",HomeMoreButton);
            flyout.Hide();await InteractionSettle();
            InteractionCheck(!menuItem.IsLoaded||InteractionFindOverlay(ElementCompositionPreview.GetElementChildVisual(InteractionFocus.SurfaceFor(menuItem)))?.Opacity is null or <.01f,
                "Closing a popup removes its active outline");

            await InteractionFocusControl(LibraryNav,"Library navigation");
            Navigate("library");await InteractionSettle();
            LibraryList.SelectedItem=LibraryList.Items[0];await InteractionSettle();
            var card=(GridViewItem)LibraryList.ContainerFromItem(LibraryList.Items[0]);
            await InteractionFocusControl(card,"Selected media card",LibraryNav);
            InteractionCheck(LibraryList.SelectedItems.Count==1,"Moving focus leaves the media selection intact");
            InteractionCheck(LibraryInformationPanel.Visibility==Visibility.Visible,"Selected media displays its information");
            double initialCardWidth=card.ActualWidth;
            await InteractionCapture("library-card-focus-layout");
            await InteractionFocusControl(LibraryApplyButton,"Media detail action",card);
            InteractionCheck(LibraryList.SelectedItems.Count==1,"Detail action focus does not replace the media selection");
            ClearLibrarySelection();await InteractionSettle();
            LibraryList.UpdateLayout();
            var unselectedCard=(GridViewItem)LibraryList.ContainerFromItem(LibraryList.Items[0]);
            InteractionCheck(Math.Abs(unselectedCard.ActualWidth-initialCardWidth)<1,"Opening and closing information preserves the card's default width when it fits");
            await InteractionFocusControl(unselectedCard,"Unselected media card");
            InteractionCheck(LibraryList.SelectedItems.Count==0,"Focusing a card alone does not select it");

            var selectionCheck=InteractionElements(unselectedCard).OfType<CheckBox>().First(c=>c.Name=="LibrarySelectionToggle");
            await InteractionFocusControl(selectionCheck,"Card selection checkbox",unselectedCard);
            InteractionCheck(LibraryList.SelectedItems.Count==0,"Checkbox focus alone does not select media");

            // The internal text editor resolves to its NumberBox surface.
            Navigate("wallpaper");await InteractionSettle();
            PlaybackIntervalBox.IsEnabled=true;
            var editor=InteractionElements(PlaybackIntervalBox).OfType<TextBox>().First();
            InteractionCheck(editor.Focus(FocusState.Programmatic),"Number editor accepts native focus");
            await InteractionSettle();
            InteractionFocused(PlaybackIntervalBox,"NumberBox composite focus");
            InteractionCheck(wallpapers!.Running.Count==0&&wallpapers.AllPlayersSnapshot().Count==0,"All checks complete without starting playback");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=interactionChecks,visuals=interactionVisuals,dpi=Root.XamlRoot.RasterizationScale},new JsonSerializerOptions {WriteIndented=true}));
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Environment.ExitCode=1;
            if(interactive)return;
        }
        finally
        {
            if(!interactive) {InteractionCloseFixture();Close();Application.Current.Exit();}
        }
    }
}
