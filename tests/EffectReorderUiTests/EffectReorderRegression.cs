using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class EffectReorderRegressionProgram
{
    internal static string Output="";
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length!=1||!Path.IsPathFullyQualified(args[0]))throw new ArgumentException("Pass one new absolute results directory.");
        Output=Path.GetFullPath(args[0]);
        if(Directory.Exists(Output)&&Directory.EnumerateFileSystemEntries(Output).Any())throw new ArgumentException("Results directory must be new or empty.");
        Directory.CreateDirectory(Output);
        var temp=Directory.CreateDirectory(Path.Combine(Output,"temp")).FullName;
        Environment.SetEnvironmentVariable("TEMP",temp);Environment.SetEnvironmentVariable("TMP",temp);
        AppContext.SetData("LumaWall.ExpansionPackRoot",Path.Combine(Output,"expansions"));
        _=Task.Run(async()=> {
#if SETTINGS_REFINEMENT_REGRESSION || PRESETS_REGRESSION || STEAM_LIBRARY_REGRESSION || LIBRARY_PERFORMANCE_REGRESSION
            await Task.Delay(180000);
#else
            await Task.Delay(90000);
#endif
            Fail(new TimeoutException("The isolated UI regression timed out."));
        });
        try {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(parameters=> {
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        } catch(Exception error) {Fail(error);}
    }
    internal static void Fail(Exception error)
    {
        File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());
        Environment.Exit(1);
    }
}

public partial class App : Microsoft.UI.Xaml.Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    internal static ReorderWindowIsolation? ReorderIsolation {get;private set;}
    public App() {
        InitializeComponent();UnhandledException+=(_,e)=>EffectReorderRegressionProgram.Fail(e.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try {
            string output=EffectReorderRegressionProgram.Output;AppLog.Initialize("EffectReorderRegression",output);
            var store=new LibraryStore(Path.Combine(output,"data"));
#if !DIAGNOSTIC_EXPORT_REGRESSION
            store.SetSetting("appearance.v1",new AppearanceOptions {Motion=true,Pattern=false}.ToJson());
#endif
            var modules=new ControlCenterServices(store);
            // The test targets replace production placement restoration with
            // no-op methods. Leave WinUI's internal window creation untouched.
            ReorderIsolation=new ReorderWindowIsolation();
            var window=new MainWindow(store,modules);
            ReorderIsolation.Protect(WinRT.Interop.WindowNative.GetWindowHandle(window));
            window.PrepareEffectReorderFixture();
#if DECODE_DIALOG_REGRESSION
            window.LoadedReady+=()=>window.CheckDecodeDialog(output);
#elif LIBRARY_FILTER_REGRESSION
            window.LoadedReady+=()=>window.CheckLibraryFilters(output);
#elif DIAGNOSTIC_EXPORT_REGRESSION
            window.LoadedReady+=()=>window.CheckDiagnosticExport(output);
#elif SETTINGS_REFINEMENT_REGRESSION
            window.LoadedReady+=()=>window.CheckSettingsRefinements(output);
#elif PRESETS_REGRESSION
            window.LoadedReady+=()=>window.CheckPresetsUi(output);
#elif STEAM_LIBRARY_REGRESSION
            window.LoadedReady+=()=>window.CheckSteamLibraryUi(output);
#elif LIBRARY_PERFORMANCE_REGRESSION
            window.LoadedReady+=()=>window.CheckLibraryPerformanceUi(output);
#else
            window.LoadedReady+=()=>window.CheckEffectReorderUi(output);
#endif
            ReorderIsolation.Place(WinRT.Interop.WindowNative.GetWindowHandle(window));
            // AppWindow.Show(false) displays this off-screen HWND without
            // activating it. Do not call Window.Activate or inject input.
            window.AppWindow.Show(false);
            ReorderIsolation.Observe("after Show(false)");
        } catch(Exception error) {ReorderIsolation?.WriteReport();EffectReorderRegressionProgram.Fail(error);}
    }
}

// Test-only protection for the completed MainWindow HWND. No creation hooks,
// internal WinUI window changes, or operations on other applications' focus.
internal sealed class ReorderWindowIsolation
{
    private const uint NoActivate=0x08000000;
    private nint testWindow;
    private readonly List<object> observations=[];
    internal bool ForegroundActivated {get;private set;}
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetStyle(nint handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern nint SetStyle(nint handle,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(nint handle,nint after,int x,int y,int width,int height,uint flags);
    internal ReorderWindowIsolation()
    {
        Observe("before MainWindow construction");
    }
    internal void Protect(nint window)
    {
        testWindow=window;Observe("after MainWindow construction");
        if(GetForegroundWindow()==window)throw new InvalidOperationException("The test constructor unexpectedly activated MainWindow before HWND protection.");
        SetStyle(window,-20,(nint)(GetStyle(window,-20).ToInt64()|NoActivate));
        if(!HasNoActivate(window))throw new InvalidOperationException("Test HWND did not retain WS_EX_NOACTIVATE.");
        Observe("protected MainWindow HWND");
    }
    internal static bool HasNoActivate(nint window)=>(GetStyle(window,-20).ToInt64()&NoActivate)!=0;
    internal void Place(nint window)
    {
        // Explicit NOACTIVATE also protects the initial resize/position change.
        if(!SetWindowPos(window,0,-20000,-20000,1440,1100,0x34)) // NOZORDER | NOACTIVATE | FRAMECHANGED
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Cannot place isolated test window.");
        Observe("after SWP_NOACTIVATE positioning");
    }
    internal void Resize(nint window,int width,int height)
    {
        if(window==0||window!=testWindow)throw new InvalidOperationException("Only the protected test HWND may be resized.");
        if(!SetWindowPos(window,0,0,0,width,height,0x16)) // NOMOVE | NOZORDER | NOACTIVATE
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Cannot resize isolated test window.");
        Observe("after SWP_NOACTIVATE resize");
    }
    internal void Observe(string stage)
    {
        nint foreground=GetForegroundWindow();
        ForegroundActivated|=testWindow!=0&&foreground==testWindow;
        observations.Add(new {stage,foreground=foreground.ToInt64(),testWindow=testWindow.ToInt64(),testIsForeground=testWindow!=0&&foreground==testWindow,
            noActivate=testWindow!=0&&HasNoActivate(testWindow)});
    }
    internal void WriteReport()=>File.WriteAllText(Path.Combine(EffectReorderRegressionProgram.Output,"activation-guard.json"),JsonSerializer.Serialize(new {placementRestorationReplaced=true,protectedMainWindowOnly=true,foregroundActivated=ForegroundActivated,observations},new JsonSerializerOptions {WriteIndented=true}));
}

public sealed partial class MainWindow
{
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")] private static extern nint ReorderForegroundWindow();
    private sealed record ReorderTestCard(EffectKind Kind,FrameworkElement Card,Grid Container,Border Outline);
    private readonly List<string> reorderChecks=[];
    private readonly List<object> reorderSnapshots=[];

    internal void PrepareEffectReorderFixture()
    {
        posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
    }
    private void ReorderCheck(bool condition,string label)
    {
        if(!condition)throw new InvalidOperationException(label);
        reorderChecks.Add(label);File.AppendAllText(Path.Combine(EffectReorderRegressionProgram.Output,"checks.txt"),label+Environment.NewLine);
    }
    private void ReorderCheckIsolation()
    {
        App.ReorderIsolation?.Observe("isolation assertion");
        ReorderCheck(wallpapers==null&&widgets==null&&menu==null,"No wallpaper, widget or desktop-menu controller is connected");
        ReorderCheck(ReorderWindowIsolation.HasNoActivate(WinRT.Interop.WindowNative.GetWindowHandle(this)),"The isolated HWND retains WS_EX_NOACTIVATE");
        ReorderCheck(ReorderForegroundWindow()!=WinRT.Interop.WindowNative.GetWindowHandle(this),"The isolated off-screen window has not taken foreground focus");
        var position=AppWindow.Position;
        ReorderCheck(position.X<-10000&&position.Y<-10000,"The test window remains outside the visible desktop");
    }
    private void ReorderResize(int width,int height)
    {
        var isolation=App.ReorderIsolation??throw new InvalidOperationException("The test HWND has not been protected.");
        isolation.Resize(WinRT.Interop.WindowNative.GetWindowHandle(this),width,height);
        ReorderCheckIsolation();
    }
    private object? ReorderInvoke(string method,params object[] arguments)
    {
        var target=typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new MissingMethodException(typeof(MainWindow).FullName,method);
        try {return target.Invoke(this,arguments);}
        catch(TargetInvocationException error) when(error.InnerException!=null) {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private EffectCardReorderSession? ReorderSession()=>
        (EffectCardReorderSession?)typeof(MainWindow).GetField("effectReorder",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(this);
    private ReorderTestCard[] ReorderCards()
    {
        var controls=(IDictionary)typeof(MainWindow).GetField("effectLayerControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(this)!;
        object Read(object value,string name)=>value.GetType().GetProperty(name)!.GetValue(value)!;
        return controls.Keys.Cast<EffectKind>().Select(kind=>new ReorderTestCard(kind,
            (FrameworkElement)Read(controls[kind]!,"Card"),(Grid)Read(controls[kind]!,"Container"),(Border)Read(controls[kind]!,"DragOutline"))).ToArray();
    }
    private Point ReorderCenter(ReorderTestCard card)=>card.Container.TransformToVisual(Root)
        .TransformPoint(new Point(card.Container.ActualWidth/2,card.Container.ActualHeight/2));
    private static Vector3 ReorderTranslation(ReorderTestCard card)
    {
        ElementCompositionPreview.GetElementVisual(card.Container).Properties.TryGetVector3("Translation",out var translation);
        return translation;
    }
    private async Task ReorderSettle(int milliseconds=250)
    {
        Root.UpdateLayout();await Task.Delay(milliseconds);Root.UpdateLayout();
    }
    private async Task ReorderCapture(string output,string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
    private void ReorderRecord(string stage)
    {
        reorderSnapshots.Add(new {stage,preview=ReorderSession()?.PreviewOrder.Select(kind=>kind.ToString()).ToArray(),
            draft=EffectLayerPresentation.Visible(effectSession.Collection).Select(kind=>kind.ToString()).ToArray(),
            cards=ReorderCards().Select(card=>new {kind=card.Kind.ToString(),row=Grid.GetRow(card.Container),column=Grid.GetColumn(card.Container),
                width=card.Container.ActualWidth,height=card.Container.ActualHeight,
                translation=new {x=ReorderTranslation(card).X,y=ReorderTranslation(card).Y,z=ReorderTranslation(card).Z},outline=card.Outline.Visibility.ToString()}).ToArray()});
    }
    internal async void CheckEffectReorderUi(string output)
    {
        try {
            PrepareEffectReorderFixture();await ReorderSettle();ReorderCheckIsolation();
            ReorderCheck(SelectedScreen!=null,"A screen editing target is available without a playback controller");
            Navigate("display");LoadEffectDraft(true);
            EffectKind[] kinds=[EffectKind.Color,EffectKind.RgbSplit,EffectKind.Softening,EffectKind.Glow,EffectKind.Vignette,EffectKind.Noise,EffectKind.Scanlines,EffectKind.Blur,EffectKind.Particles];
            effectSession.Collection=new EffectCollection {Added=kinds.Aggregate((left,right)=>left|right)};
            effectSession.AdoptOriginal(DraftEffects);RefreshEffectCollection();await ReorderSettle();
            // Composition's property bag exposes a base value, not a sampled
            // animated value. First test exact visual destinations without
            // animation, then exercise the animated path separately below.
            Appearance=Appearance with {Motion=false};
            ReorderCheck(selectedEffectKind==null&&EffectsOverview.Visibility==Visibility.Visible,"The real effect overview is visible");
            var cards=ReorderCards();var initial=EffectLayerPresentation.Visible(effectSession.Collection);
            ReorderCheck(cards.Length>=7&&cards.All(card=>card.Container.ActualWidth>0&&card.Container.ActualHeight>0),"At least seven real effect cards have measured layouts");
            ReorderCheck(cards.Select(card=>Grid.GetRow(card.Container)).Distinct().Count()>1,"The fixture exercises multiple actual card rows");
            string key=effectSession.Key!;var collection=effectSession.Collection;long revision=effectSession.Revision;
            var source=cards.Single(card=>card.Kind==initial[0]);
            var target=cards.Where(card=>Grid.GetRow(card.Container)>Grid.GetRow(source.Container)).OrderByDescending(card=>Grid.GetRow(card.Container)).ThenByDescending(card=>Grid.GetColumn(card.Container)).First();
            Point press=ReorderCenter(source),destination=ReorderCenter(target);
            await ReorderCapture(output,"before-drag");ReorderRecord("before");
            ReorderCheck((bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key,press)!,"The production drag entry accepts a current target and measured card");
            ReorderInvoke("MoveEffectCardDrag",destination);
            ReorderCheck(ReorderSession() is {HasChanges:true}&&!ReorderSession()!.PreviewOrder.SequenceEqual(initial),"A cross-row move changes the transient preview order");
            ReorderCheck(ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==revision,"Moving cards does not mutate the draft or its revision before release");
            Vector3 shifted=ReorderTranslation(source);
            ReorderCheck(Math.Abs(shifted.X-(destination.X-press.X))<3&&Math.Abs(shifted.Y-(destination.Y-press.Y))<3,"The lifted source translation follows the supplied pointer delta");
            ReorderCheck(source.Outline.Visibility==Visibility.Visible,"The lifted source has a visible drag outline");
            ReorderCheck(cards.Where(card=>card.Kind!=source.Kind).Any(card=>ReorderTranslation(card).LengthSquared()>1),"Other real cards translate while making room for the preview");
            ReorderRecord("moving");await ReorderSettle();await ReorderCapture(output,"during-drag");
            ReorderCheck(ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==revision,"Waiting for layout animation still leaves the draft unchanged");
            var preview=ReorderSession()!.PreviewOrder.ToArray();
            ReorderInvoke("EndEffectCardDrag",true);await ReorderSettle();
            ReorderCheck(ReorderSession()==null&&EffectLayerPresentation.Visible(effectSession.Collection).SequenceEqual(preview),"Release commits exactly the preview order and ends the drag");
            ReorderCheck(effectSession.Revision==revision+1,"The committed drag advances the draft revision once");
            ReorderCheck(selectedEffectKind==null,"Completing a drag does not open effect parameters");
            ReorderCheck(ReorderCards().All(card=>ReorderTranslation(card).LengthSquared()<.01f&&card.Outline.Visibility!=Visibility.Visible),"Committed cards settle with no remaining lift or outline");
            FlushEffectChanges();await ReorderSettle();ReorderRecord("committed");await ReorderCapture(output,"after-commit");

            collection=effectSession.Collection;revision=effectSession.Revision;cards=ReorderCards();
            var committed=EffectLayerPresentation.Visible(collection);source=cards.Single(card=>card.Kind==committed[0]);target=cards.Single(card=>card.Kind==committed[^1]);
            ReorderCheck((bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key,ReorderCenter(source))!,"A second drag can begin after commit");
            ReorderInvoke("MoveEffectCardDrag",ReorderCenter(target));ReorderCheck(ReorderSession()!.HasChanges,"Cancellation starts from a changed preview");
            ReorderInvoke("EndEffectCardDrag",false);await ReorderSettle();
            ReorderCheck(ReorderSession()==null&&ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==revision,"Cancellation discards preview changes without a draft edit");
            ReorderCheck(ReorderCards().All(card=>ReorderTranslation(card).LengthSquared()<.01f&&card.Outline.Visibility!=Visibility.Visible),"Cancellation clears all real card translations and drag decoration");

            Appearance=Appearance with {Motion=true};
            cards=ReorderCards();source=cards.Single(card=>card.Kind==committed[0]);target=cards.Single(card=>card.Kind==committed[^1]);
            ReorderCheck((bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key,ReorderCenter(source))!,"The motion-enabled path starts on the same real WinUI controls");
            ReorderInvoke("MoveEffectCardDrag",ReorderCenter(target));
            await ReorderSettle(220);
            var animatedCards=ReorderCards();
            ReorderCheck(ReorderSession() is {HasChanges:true}&&cards.All(card=>ReferenceEquals(card.Container,animatedCards.Single(current=>current.Kind==card.Kind).Container)),"Motion-enabled preview preserves card instances instead of rebuilding the layout");
            ReorderCheck(ReorderTranslation(source).LengthSquared()>1&&ElementCompositionPreview.GetElementVisual(source.Container).Scale.X>1,"The motion-enabled source remains translated and lifted");
            ReorderCheck(ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==revision,"Motion-enabled preview and render ticks do not commit the draft");
            var cachedVisuals=effectDragVisuals.ToDictionary(pair=>pair.Key,pair=>pair.Value);
            var animations=cachedVisuals.Values.Select(state=>typeof(EffectDragVisual).GetField("animation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(state)).Where(value=>value!=null).ToArray();
            ReorderCheck(animations.Length>0,"Animated yielding creates gesture-owned animations");
            Point repeated=ReorderCenter(target);
            for(int i=0;i<300;i++)MoveEffectCardDrag(repeated);
            ReorderCheck(cachedVisuals.All(pair=>ReferenceEquals(pair.Value,effectDragVisuals[pair.Key]))&&
                animations.All(animation=>effectDragVisuals.Values.Any(state=>ReferenceEquals(animation,typeof(EffectDragVisual).GetField("animation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(state)))),
                "Repeated positions reuse cached visuals and animations without rebuilding them");
            ReorderRecord("motion-enabled-preview");await ReorderCapture(output,"motion-enabled-preview");
            // End deterministically so no animation base value is mistaken for
            // its sampled destination in the cleanup assertions.
            Appearance=Appearance with {Motion=false};ReorderInvoke("EndEffectCardDrag",false);await ReorderSettle();
            ReorderCheck(ReorderCards().All(card=>ReorderTranslation(card).LengthSquared()<.01f&&card.Outline.Visibility!=Visibility.Visible),"The animated preview can be cancelled and reset without retaining transforms");
            ReorderCheck(effectDragVisuals.Count==0&&effectDragEasing==null&&effectDragRoot==null&&effectDragButton==null&&
                ((EffectReorderButton)source.Card).SampleMouseDrag==null,"Cancellation releases gesture caches and mouse sampling callback");

            // Scroll independently while holding a stable point inside the viewport.
            // This checks cached coordinates against WinUI's actual live transform.
            ReorderResize(1100,650);await ReorderSettle();
            cards=ReorderCards();source=cards.Single(card=>card.Kind==committed[0]);
            ReorderCheck(EffectsParameterScroll.ScrollableHeight>32,"The compact fixture has room to exercise scroll offset changes");
            Point held=EffectsParameterScroll.TransformToVisual(Root).TransformPoint(new Point(200,EffectsParameterScroll.ActualHeight/2));
            Point start=ReorderCenter(source);
            ReorderCheck(BeginEffectCardDrag(source.Kind,key,start),"A scroll-aware drag can start");
            MoveEffectCardDrag(held);
            EffectsParameterScroll.ChangeView(null,Math.Min(64,EffectsParameterScroll.ScrollableHeight),null,true);await ReorderSettle(100);
            MoveEffectCardDrag(held);
            var liveLocal=Root.TransformToVisual(EffectsListPanel).TransformPoint(held);
            var actualTopLeft=effectDragTopLeft;
            ReorderCheck(EffectsParameterScroll.VerticalOffset>0&&Math.Abs(actualTopLeft.X-(liveLocal.X-effectDragGrab.X))<.1&&
                Math.Abs(actualTopLeft.Y-(liveLocal.Y-effectDragGrab.Y))<.1,"Cached drag coordinates follow the actual scrolled layout at a stationary pointer");
            EndEffectCardDrag(false);EffectsParameterScroll.ChangeView(null,0,null,true);await ReorderSettle();
            ReorderCheck(ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==revision,"Scrolling and cancelling do not edit or save the effect order");
            source=ReorderCards().Single(card=>card.Kind==committed[0]);
            ReorderCheck(BeginEffectCardDrag(source.Kind,key,ReorderCenter(source)),"A new drag starts after scroll cleanup");
            ReorderResize(1200,720);await ReorderSettle();
            ReorderCheck(ReorderSession()==null&&effectDragVisuals.Count==0&&ReferenceEquals(effectSession.Collection,collection),
                "Changing viewport layout cancels stale coordinate caches without committing");
            cards=ReorderCards();source=cards.Single(card=>card.Kind==committed[0]);target=cards.Single(card=>card.Kind==committed[^1]);

            ReorderCheck(!(bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key+"|stale",ReorderCenter(source))!,"A stale captured target cannot start a drag");
            ReorderCheck((bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key,ReorderCenter(source))!,"A current target can start before a target change");
            effectSession.ChangeTarget(key+"|changed");long changedRevision=effectSession.Revision;
            ReorderInvoke("MoveEffectCardDrag",ReorderCenter(target));ReorderInvoke("EndEffectCardDrag",true);
            ReorderCheck(ReorderSession()==null&&ReferenceEquals(effectSession.Collection,collection)&&effectSession.Revision==changedRevision,"A target change rejects the stale drag instead of committing to another editor");
            effectSession.ChangeTarget(key);RefreshEffectCollection();await ReorderSettle();
            cards=ReorderCards();source=cards[0];
            ReorderCheck((bool)ReorderInvoke("BeginEffectCardDrag",source.Kind,key,ReorderCenter(source))!,"A fresh drag can begin after target cancellation");
            Navigate("library");await ReorderSettle();
            ReorderCheck(ReorderSession()==null&&ReferenceEquals(effectSession.Collection,collection),"Leaving the effects overview cancels its pending drag");
            ReorderCheckIsolation();
            File.WriteAllText(Path.Combine(output,"layout-states.json"),JsonSerializer.Serialize(reorderSnapshots,new JsonSerializerOptions {WriteIndented=true}));
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks,
                scope="Real off-screen WinUI controls, layout, drag preview and commit/cancel logic driven through production drag methods; no native Pointer event injection or capture verification.",
                nativePointerChainVerified=false,animationInterpolationPixelsVerified=false,foregroundActivated=false,productionDataAccessed=false,desktopHostsStarted=false,
                rasterizationScale=Root.XamlRoot.RasterizationScale},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(output,"layout-states.json"),JsonSerializer.Serialize(reorderSnapshots,new JsonSerializerOptions {WriteIndented=true}));
            Environment.ExitCode=1;
        } finally {
            ReorderInvoke("EndEffectCardDrag",false);effectSession.StopSchedule();
            ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.Observe("before test window close");App.ReorderIsolation?.WriteReport();
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
