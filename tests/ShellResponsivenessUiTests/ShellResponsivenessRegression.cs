using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using LumaWall.WidgetSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public static class ShellResponsivenessProgram
{
    internal static string Output="";
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length!=1||!Path.IsPathFullyQualified(args[0]))throw new ArgumentException("Pass one new absolute results directory.");
        Output=Path.GetFullPath(args[0]);
        if(Directory.Exists(Output)&&Directory.EnumerateFileSystemEntries(Output).Any())throw new ArgumentException("Results directory must be new or empty.");
        Directory.CreateDirectory(Output);
        _=Task.Run(async()=> {await Task.Delay(180000);Fail(new TimeoutException("The isolated shell regression timed out."));});
        try {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(parameters=> {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        } catch(Exception error) {Fail(error);}
    }
    internal static void Fail(Exception error)
    {
        File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());
        App.ShellIsolation?.WriteReport();
        Environment.Exit(1);
    }
}

public partial class App : Microsoft.UI.Xaml.Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    internal static ShellWindowIsolation? ShellIsolation {get;private set;}
    public App() {InitializeComponent();UnhandledException+=(_,e)=>ShellResponsivenessProgram.Fail(e.Exception);}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try {
            string output=ShellResponsivenessProgram.Output;AppContext.SetData("LumaWall.ExpansionPackRoot",Path.Combine(output,"expansions"));AppLog.Initialize("ShellResponsivenessRegression",output);
            var store=new LibraryStore(Path.Combine(output,"data"));
            store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
            var modules=new ControlCenterServices(store);
            ShellIsolation=new ShellWindowIsolation();
            // Generated source copies skip only construction's initial Restore.
            // The production AppWindow.Changed and placement timer stay active.
            var window=new MainWindow(store,modules);
            ShellIsolation.Protect(WinRT.Interop.WindowNative.GetWindowHandle(window));
            window.PrepareShellResponsivenessFixture();
            window.LoadedReady+=()=>window.CheckShellResponsiveness(output);
            ShellIsolation.Place();
            window.AppWindow.Show(false);
            ShellIsolation.Observe("after Show(false)");
        } catch(Exception error) {ShellResponsivenessProgram.Fail(error);}
    }
}

internal sealed class ShellWindowIsolation
{
    private const uint NoActivate=0x08000000;
    private nint testWindow;
    private readonly List<object> observations=[];
    internal bool ForegroundActivated {get;private set;}
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetStyle(nint handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern nint SetStyle(nint handle,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(nint handle,nint after,int x,int y,int width,int height,uint flags);
    internal ShellWindowIsolation() {Observe("before MainWindow construction");}
    internal void Protect(nint window)
    {
        testWindow=window;Observe("after MainWindow construction");
        if(GetForegroundWindow()==window)throw new InvalidOperationException("The test constructor unexpectedly activated MainWindow.");
        SetStyle(window,-20,(nint)(GetStyle(window,-20).ToInt64()|NoActivate));
        if(!HasNoActivate)throw new InvalidOperationException("Test HWND did not retain WS_EX_NOACTIVATE.");
        Observe("protected MainWindow HWND");
    }
    internal bool HasNoActivate=>testWindow!=0&&(GetStyle(testWindow,-20).ToInt64()&NoActivate)!=0;
    internal bool IsForeground=>testWindow!=0&&GetForegroundWindow()==testWindow;
    internal void Place()
    {
        if(testWindow==0||!HasNoActivate)throw new InvalidOperationException("Only the protected fixture HWND can be positioned.");
        if(!SetWindowPos(testWindow,0,-20000,-20000,1440,1100,0x34))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Cannot place isolated test window.");
        Observe("after initial SWP_NOACTIVATE positioning");
    }
    internal void Move(int x,int y)
    {
        if(testWindow==0||x>=-10000||y>=-10000)throw new InvalidOperationException("Only off-screen fixture movement is allowed.");
        // NOSIZE | NOZORDER | NOACTIVATE | NOOWNERZORDER.
        if(!SetWindowPos(testWindow,0,x,y,0,0,0x215))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Cannot move isolated test window.");
    }
    internal void Resize(int width,int height)
    {
        if(testWindow==0||!HasNoActivate||width<400||height<400)throw new InvalidOperationException("Only the protected fixture HWND can be resized.");
        // NOMOVE | NOZORDER | NOACTIVATE | NOOWNERZORDER.
        if(!SetWindowPos(testWindow,0,0,0,width,height,0x216))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Cannot resize isolated test window.");
        Observe("after isolated NOACTIVATE resize");
    }
    internal void Observe(string stage)
    {
        nint foreground=GetForegroundWindow();
        ForegroundActivated|=testWindow!=0&&foreground==testWindow;
        observations.Add(new {stage,foreground=foreground.ToInt64(),testWindow=testWindow.ToInt64(),
            testIsForeground=testWindow!=0&&foreground==testWindow,noActivate=HasNoActivate});
    }
    internal void WriteReport()=>File.WriteAllText(Path.Combine(ShellResponsivenessProgram.Output,"activation-guard.json"),
        JsonSerializer.Serialize(new {onlyInitialRestoreSkipped=true,productionPlacementHandlersPreserved=true,
            protectedMainWindowOnly=true,foregroundActivated=ForegroundActivated,observations},new JsonSerializerOptions {WriteIndented=true}));
}

internal static class ShellPerfInstrumentation
{
    internal sealed record Counter(long Count,double TotalMilliseconds,double MaximumMilliseconds);
    private sealed class Accumulator {internal long Count,Ticks,Maximum;}
    private static readonly Dictionary<string,Accumulator> counters=[];
    private static readonly object gate=new();
    private static bool enabled;
    [ThreadStatic] private static int changedDepth;
    internal static RectInt32[] LastInputRegions {get;private set;}=[];
    internal static RectInt32[] ObserveInputRegions(RectInt32[] regions)
    {
        Count("setInputRegionRects");LastInputRegions=regions;return regions;
    }
    internal readonly struct Scope(string? key,long started,bool changed) : IDisposable
    {
        public void Dispose()
        {
            if(key==null)return;
            long elapsed=Stopwatch.GetTimestamp()-started;
            Record(key,elapsed);
            if(changedDepth>0&&!changed)Record(key+"DuringChanged",elapsed);
            if(changed)changedDepth--;
        }
    }
    internal static Scope Measure(string key)
    {
        if(!enabled)return default;
        bool changed=key is "PlacementChanged" or "ShellWindow_Changed";
        if(changed)changedDepth++;
        return new(key,Stopwatch.GetTimestamp(),changed);
    }
    internal static void Count(string key) {if(enabled)Record(key,0);}
    private static void Record(string key,long ticks)
    {
        lock(gate) {
            if(!counters.TryGetValue(key,out var value))counters.Add(key,value=new());
            value.Count++;value.Ticks+=ticks;value.Maximum=Math.Max(value.Maximum,ticks);
        }
    }
    internal static void Reset() {lock(gate) {counters.Clear();enabled=true;}}
    internal static Dictionary<string,Counter> Snapshot()
    {
        lock(gate)return counters.ToDictionary(pair=>pair.Key,pair=>new Counter(pair.Value.Count,
            pair.Value.Ticks*1000d/Stopwatch.Frequency,pair.Value.Maximum*1000d/Stopwatch.Frequency));
    }
    internal static void Stop() {enabled=false;}
}

public sealed partial class MainWindow
{
#if SHELL_RESPONSIVENESS_OPTIMIZED
    private static readonly bool ShellOptimized=true;
#else
    private static readonly bool ShellOptimized=false;
#endif
    private const int ShellMoveCount=200;
    private readonly List<string> shellChecks=[];
    private readonly List<object> shellMeasurements=[];
    private DispatcherQueueTimer? shellHeartbeat;
    private readonly List<double> shellHeartbeatIntervals=[];
    private long shellHeartbeatLast;
    private int shellPositionChanges,shellSizeChanges,shellPresenterChanges;

    internal void PrepareShellResponsivenessFixture()
    {
        posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();
        homeMotionShutdown=true;StopHomeMotion();StopPageEntry();
        // Do not shut down window placement: its production timer and Changed
        // handlers are precisely what this fixture exercises.
    }
    private void ShellCheck(bool condition,string label)
    {
        if(!condition)throw new InvalidOperationException(label);
        shellChecks.Add(label);File.AppendAllText(Path.Combine(ShellResponsivenessProgram.Output,"checks.txt"),label+Environment.NewLine);
    }
    private void ShellCheckIsolation(string stage)
    {
        var isolation=App.ShellIsolation!;isolation.Observe(stage);
        ShellCheck(wallpapers==null&&widgets==null&&menu==null,"No desktop controller is connected: "+stage);
        ShellCheck(isolation.HasNoActivate&&!isolation.IsForeground&&!isolation.ForegroundActivated,"The fixture HWND stays nonactivating: "+stage);
        ShellCheck(AppWindow.Position.X<-10000&&AppWindow.Position.Y<-10000,"The fixture remains off screen: "+stage);
    }
    private Task ShellDispatchIdle()
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=>completion.SetResult()))
            completion.SetException(new InvalidOperationException("Cannot enqueue fixture idle boundary."));
        return completion.Task;
    }
    private void ShellStartHeartbeat()
    {
        shellHeartbeatIntervals.Clear();shellHeartbeatLast=Stopwatch.GetTimestamp();
        shellHeartbeat=DispatcherQueue.CreateTimer();shellHeartbeat.Interval=TimeSpan.FromMilliseconds(16);
        shellHeartbeat.Tick+=(_,_)=> {
            long now=Stopwatch.GetTimestamp();shellHeartbeatIntervals.Add((now-shellHeartbeatLast)*1000d/Stopwatch.Frequency);
            shellHeartbeatLast=now;
        };
        shellHeartbeat.Start();
    }
    private static object ShellDistribution(IReadOnlyCollection<double> values)
    {
        var sorted=values.OrderBy(value=>value).ToArray();
        double At(double fraction)=>sorted.Length==0?0:sorted[Math.Clamp((int)Math.Ceiling(sorted.Length*fraction)-1,0,sorted.Length-1)];
        return new {count=sorted.Length,medianMilliseconds=At(.5),p95Milliseconds=At(.95),maximumMilliseconds=At(1),
            totalMilliseconds=sorted.Sum()};
    }
    private void ShellObserveChanged(AppWindow sender,AppWindowChangedEventArgs args)
    {
        if(args.DidPositionChange)shellPositionChanges++;
        if(args.DidSizeChange)shellSizeChanges++;
        if(args.DidPresenterChange)shellPresenterChanges++;
    }
    private async Task ShellCapture(string output,string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
    private static long ShellCount(IReadOnlyDictionary<string,ShellPerfInstrumentation.Counter> counters,string key)=>counters.TryGetValue(key,out var value)?value.Count:0;

    private void ShellCheckInputGeometry(string stage)
    {
        RectInt32[] regions=ShellPerfInstrumentation.LastInputRegions;
        ShellCheck(regions.Length>0,"Production submits nonempty input regions: "+stage);
        double scale=Root.XamlRoot.RasterizationScale;
        bool Contains(Point point)=>regions.Any(rect=>point.X>=rect.X&&point.Y>=rect.Y&&point.X<rect.X+rect.Width&&point.Y<rect.Y+rect.Height);
        foreach(var button in new[]{ShellMinimizeButton,ShellMaximizeButton,ShellCloseButton}) {
            Point center=button.TransformToVisual(Root).TransformPoint(new Point(button.ActualWidth/2,button.ActualHeight/2));
            ShellCheck(button.ActualWidth>0&&button.ActualHeight>0&&Contains(new Point(center.X*scale,center.Y*scale)),
                "Caption "+button.GlyphKind+" center remains inside the submitted interactive region: "+stage);
        }
        Rect drag=ShellTitleDragRegion.TransformToVisual(Root).TransformBounds(new Rect(0,0,ShellTitleDragRegion.ActualWidth,ShellTitleDragRegion.ActualHeight));
        bool remainingDragArea=Enumerable.Range(1,9).Any(index=>!Contains(new Point((drag.X+drag.Width*index/10)*scale,(drag.Y+drag.Height*.7)*scale)));
        ShellCheck(drag.Width>40&&drag.Height>20&&remainingDragArea,"The title retains a drag area outside interactive regions: "+stage);
    }
    private async Task ShellCheckResizeAndInputRegions()
    {
        await Task.Delay(700);await ShellDispatchIdle();
        var originalSize=AppWindow.Size;var originalPosition=AppWindow.Position;
        UpdateShellInputRegions();ShellCheckInputGeometry("original size");
        var originalRegions=ShellPerfInstrumentation.LastInputRegions;
        ShellPerfInstrumentation.Reset();
        for(int index=0;index<10;index++)UpdateShellInputRegions();
        var unchanged=ShellPerfInstrumentation.Snapshot();ShellPerfInstrumentation.Stop();
        if(ShellOptimized)ShellCheck(ShellCount(unchanged,"setInputRegionRects")==0,"Repeated unchanged region refreshes do not resubmit native input rectangles");
        ShellPerfInstrumentation.Reset();
        App.ShellIsolation!.Resize(originalSize.Width+120,originalSize.Height+80);
        await Task.Delay(500);Root.UpdateLayout();await ShellDispatchIdle();
        var resized=ShellPerfInstrumentation.Snapshot();ShellPerfInstrumentation.Stop();
        ShellCheck(AppWindow.Position.X==originalPosition.X&&AppWindow.Position.Y==originalPosition.Y,"Isolated resize preserves the off-screen position");
        ShellCheck(AppWindow.Size.Width==originalSize.Width+120&&AppWindow.Size.Height==originalSize.Height+80,"The fixture reaches its resized dimensions");
        ShellCheck(ShellCount(resized,"setInputRegionRects")>0,"A real size change submits updated interactive regions");
        ShellCheck(!originalRegions.SequenceEqual(ShellPerfInstrumentation.LastInputRegions),"Submitted interactive geometry changes with the window dimensions");
        ShellCheckInputGeometry("resized");
        ShellCheckIsolation("after isolated resize");
        App.ShellIsolation.Resize(originalSize.Width,originalSize.Height);
        await Task.Delay(500);Root.UpdateLayout();await ShellDispatchIdle();
        ShellCheck(AppWindow.Size.Width==originalSize.Width&&AppWindow.Size.Height==originalSize.Height,"Resize regression restores the original fixture dimensions");
        ShellCheckInputGeometry("restored size");
        ShellCheckPersistedPosition("after resize restoration");
        shellMeasurements.Add(new {stage="post-measurement-resize-regression",unchangedRegionRefreshes=10,
            countersForUnchangedRefreshes=unchanged,countersForResize=resized,realDpiTransitionExecuted=false});
    }
    private void ShellCheckPersistedPosition(string stage)
    {
        string? json=modules.Settings.Get("window.placement.v1");
        ShellCheck(json!=null,"Production placement timer persisted the fixture position: "+stage);
        using var document=JsonDocument.Parse(json!);var value=document.RootElement;
        var screen=Screen.FromHandle(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var area=screen.WorkingArea;double scale=WidgetPlacement.Scale(screen);
        double savedX=value.GetProperty("X").GetDouble()*scale+area.Left;
        double savedY=value.GetProperty("Y").GetDouble()*scale+area.Top;
        ShellCheck(Math.Abs(savedX-AppWindow.Position.X)<=1&&Math.Abs(savedY-AppWindow.Position.Y)<=1,
            "Saved normal coordinates match the last move: "+stage);
        ShellCheck(!value.GetProperty("Maximized").GetBoolean(),"Pure movement preserves the normal presenter state: "+stage);
        ShellCheck(Math.Abs(value.GetProperty("Width").GetDouble()*scale-AppWindow.Size.Width)<=1&&
            Math.Abs(value.GetProperty("Height").GetDouble()*scale-AppWindow.Size.Height)<=1,
            "Saved dimensions match the unchanged fixture dimensions: "+stage);
    }
    private async Task ShellMeasureMoves(string stage,bool yieldEachMove)
    {
        await Task.Delay(700);await ShellDispatchIdle();
        var initial=AppWindow.Position;var size=AppWindow.Size;
        int positionBefore=shellPositionChanges,sizeBefore=shellSizeChanges,presenterBefore=shellPresenterChanges;
        ShellPerfInstrumentation.Reset();ShellStartHeartbeat();
        var synchronous=new List<double>(ShellMoveCount);var delivered=new List<double>(ShellMoveCount);
        long started=Stopwatch.GetTimestamp();
        for(int index=1;index<=ShellMoveCount;index++) {
            long moveStarted=Stopwatch.GetTimestamp();
            App.ShellIsolation!.Move(initial.X+index,initial.Y+(index%2));
            synchronous.Add((Stopwatch.GetTimestamp()-moveStarted)*1000d/Stopwatch.Frequency);
            if(yieldEachMove)await ShellDispatchIdle();
            delivered.Add((Stopwatch.GetTimestamp()-moveStarted)*1000d/Stopwatch.Frequency);
        }
        await ShellDispatchIdle();
        double dispatchedMilliseconds=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
        var afterDispatch=ShellPerfInstrumentation.Snapshot();
        int deliveredPositionChanges=shellPositionChanges-positionBefore;
        var movingHeartbeat=shellHeartbeatIntervals.ToArray();
        await Task.Delay(700);await ShellDispatchIdle();
        var afterIdle=ShellPerfInstrumentation.Snapshot();
        shellHeartbeat!.Stop();shellHeartbeat=null;ShellPerfInstrumentation.Stop();
        shellMeasurements.Add(new {stage,moveCount=ShellMoveCount,yieldEachMove,
            initial=new {x=initial.X,y=initial.Y},final=new {x=AppWindow.Position.X,y=AppWindow.Position.Y},
            size=new {width=size.Width,height=size.Height},dispatchedMilliseconds,
            synchronousMoves=ShellDistribution(synchronous),movesThroughDispatcherIdle=ShellDistribution(delivered),
            movingHeartbeat=ShellDistribution(movingHeartbeat),heartbeatIncludingIdle=ShellDistribution(shellHeartbeatIntervals),
            deliveredPositionChanges,totalPositionChanges=shellPositionChanges-positionBefore,
            sizeChanges=shellSizeChanges-sizeBefore,presenterChanges=shellPresenterChanges-presenterBefore,
            countersAfterDispatch=afterDispatch,countersAfterSaveIdle=afterIdle});
        ShellCheckIsolation(stage);
        ShellCheck(AppWindow.Position.X==initial.X+ShellMoveCount&&AppWindow.Position.Y==initial.Y,"All requested movement reaches the final position: "+stage);
        ShellCheck(AppWindow.Size.Width==size.Width&&AppWindow.Size.Height==size.Height&&shellSizeChanges==sizeBefore,
            "Movement does not resize the main window: "+stage);
        ShellCheck(shellPresenterChanges==presenterBefore,"Movement does not alter presenter state: "+stage);
        ShellCheck(deliveredPositionChanges>0&&ShellCount(afterDispatch,"ShellWindow_Changed")>0,
            "Real AppWindow.Changed callbacks ran: "+stage);
        if(yieldEachMove)ShellCheck(deliveredPositionChanges>=ShellMoveCount,
            "Each dispatched move reaches the production position handler: "+stage);
        ShellCheck(ShellCount(afterIdle,"SaveWindowPlacement")>0&&ShellCount(afterIdle,"placementWrites")>0,
            "The real deferred placement save ran: "+stage);
        ShellCheckPersistedPosition(stage);
        if(ShellOptimized&&yieldEachMove) {
            ShellCheck(ShellCount(afterDispatch,"CaptureWindowPlacementDuringChanged")==0,
                "Pure position events do not synchronously capture display identity");
            ShellCheck(ShellCount(afterDispatch,"EnumDisplayDevicesDuringChanged")==0,
                "Pure position events perform no synchronous native display enumeration");
            ShellCheck(ShellCount(afterDispatch,"setInputRegionRects")==0,
                "Pure position events do not resubmit unchanged XAML input regions");
            ShellCheck(ShellCount(afterDispatch,"wordmarkLayoutWrites")==0,
                "Pure position events do not rewrite unchanged header layout properties");
        }
    }
    private async Task CheckMonitorMoves(string output)
    {
        var monitors=Screen.AllScreens;
        var observations=new List<object>();
        double launchScale=interfaceScale;
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Hide();
        try {
            foreach(var monitor in monitors.Concat(monitors.Take(1))) {
                if(AppWindow.IsVisible)throw new InvalidOperationException("Cross-monitor checks require a hidden fixture window.");
                var area=monitor.WorkingArea;var before=AppWindow.Size;
                AppWindow.Move(new Windows.Graphics.PointInt32(area.Left+Math.Max(0,(area.Width-before.Width)/2),area.Top+Math.Max(0,(area.Height-before.Height)/2)));
                await Task.Delay(100);
                var native=AppWindow.Size;
                await Task.Delay(650);await ShellDispatchIdle();
                var settled=AppWindow.Size;
                ShellCheck(Screen.FromHandle(hwnd).DeviceName==monitor.DeviceName && !AppWindow.IsVisible,"Hidden fixture moved to "+monitor.DeviceName);
                ShellCheck(interfaceScale==launchScale&&native.Width==settled.Width&&native.Height==settled.Height,"The placement timer preserves native window size and launch scale on "+monitor.DeviceName);
                ShellCheck(savedWindowPlacement?.Device==monitor.DeviceName,"Placement persistence follows the moved window");
                observations.Add(new {monitor.DeviceName,monitor.Bounds.Width,monitor.Bounds.Height,windowWidth=settled.Width,windowHeight=settled.Height,interfaceScale});
            }
            File.WriteAllText(Path.Combine(output,"monitor-moves.json"),JsonSerializer.Serialize(observations));
        } finally {App.ShellIsolation!.Observe("after hidden monitor moves");}
    }
    internal async void CheckShellResponsiveness(string output)
    {
        bool passed=false;
        try {
            PrepareShellResponsivenessFixture();
            await Task.Delay(900);Root.UpdateLayout();await ShellDispatchIdle();
            ShellCheckIsolation("before measurement");
            ShellCheck(!placementStopped&&placementTimer!=null,"Production placement handler and deferred save timer remain active");
            ShellCheck(!Appearance.Motion&&!Appearance.Pattern,"The fixture disables unrelated appearance motion and patterns");
            AppWindow.Changed+=ShellObserveChanged;
            if(Environment.GetEnvironmentVariable("ZZZWALLPAPER_MONITOR_MOVE_FIXTURE")=="1")await CheckMonitorMoves(output);
            else {
                await ShellCapture(output,"shell-before-movement");
                await ShellMeasureMoves("200-dispatched-position-changes",true);
                await ShellMeasureMoves("200-burst-position-changes",false);
                await ShellCapture(output,"shell-after-movement");
                await ShellCheckResizeAndInputRegions();
                ShellCheckIsolation("after all measurements");
            }
            passed=true;
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;
        } finally {
            shellHeartbeat?.Stop();ShellPerfInstrumentation.Stop();
            AppWindow.Changed-=ShellObserveChanged;
            string manifest=Path.Combine(AppContext.BaseDirectory,"shell-instrumentation-manifest.json");
            if(File.Exists(manifest))File.Copy(manifest,Path.Combine(output,"instrumentation-manifest.json"));
            File.WriteAllText(Path.Combine(output,"measurements.json"),JsonSerializer.Serialize(new {
                optimizedAssertionsEnabled=ShellOptimized,moveCountPerStage=ShellMoveCount,measurements=shellMeasurements,
                limitations="Single-machine isolated off-screen move messages; measures production callback work and dispatcher responsiveness, not DWM on-screen drag FPS. No timing pass/fail threshold. Appearance animations and desktop hosts are disabled equally in both modes.",
                countersAreInclusive=true,onlyInitialRestoreSkipped=true,productionPlacementHandlersPreserved=true
            },new JsonSerializerOptions {WriteIndented=true}));
            effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ShellIsolation!.Observe("before fixture close");App.ShellIsolation.WriteReport();
            passed&=!App.ShellIsolation.ForegroundActivated;
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {
                passed,checks=shellChecks,optimizedAssertionsEnabled=ShellOptimized,moveCountPerStage=ShellMoveCount,
                foregroundActivated=App.ShellIsolation.ForegroundActivated,productionDataAccessed=false,
                desktopHostsStarted=false,inputInjected=false,onlyInitialRestoreSkipped=true,
                productionPlacementHandlersPreserved=true,actualMaximizeOrMinimizeExecuted=false
            },new JsonSerializerOptions {WriteIndented=true}));
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
