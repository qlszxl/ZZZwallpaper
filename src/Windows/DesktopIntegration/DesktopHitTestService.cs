using System.Text;
using System.Runtime.InteropServices;
using LumaWall.Contracts;

namespace LumaWall.DesktopIntegration;

public sealed class DesktopHitTestService : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private readonly AutoResetEvent refresh=new(false);
    private readonly Thread? worker;
    private sealed record CapturedScene(DesktopSceneSnapshot Geometry,nint ListView,nint DefView,nint ShellRoot);
    private CapturedScene? scene;
    private long epoch;
    private nint listView,shellRoot,defView,process,remote;
    private uint explorerPid;
    private nint menuOwner;
    private sealed record MenuWindow(nint Handle,long Until);
    private MenuWindow[] menuWindows=[];
    private long menuTransitionUntil;
    public DesktopSceneSnapshot? Scene => Volatile.Read(ref scene)?.Geometry;
    public HitSnapshot? Snapshot { get; private set; }
    public long Epoch => Interlocked.Read(ref epoch);
    public string Diagnostic { get; private set; }=UiLanguage.Text("正在识别桌面");
    public void Invalidate(bool wake=true) {Interlocked.Increment(ref epoch);Volatile.Write(ref scene,null);if(wake)refresh.Set();}
    public bool IsDesktopEvent(nint hwnd) => hwnd!=0&&(hwnd==listView||hwnd==shellRoot||hwnd==defView);
    internal void SetMenuOwner(nint hwnd)=>Volatile.Write(ref menuOwner,hwnd);
    public void TrackMenuWindow(nint hwnd,bool opened)
    {
        if(hwnd==0)return;
        long now=Environment.TickCount64;
        // UI thread publishes immutable identities before show/destroy. Keep a
        // closing HWND briefly for queued out-of-context WinEvent notifications.
        var windows=Volatile.Read(ref menuWindows).Where(item=>item.Handle!=hwnd&&item.Until>=now)
            .Append(new MenuWindow(hwnd,opened?long.MaxValue:now+500)).ToArray();
        Interlocked.Exchange(ref menuTransitionUntil,now+500);
        Volatile.Write(ref menuWindows,windows);refresh.Set();
    }
    private bool IsMenuWindow(nint hwnd)
    {
        if(hwnd==0)return false;
        if(hwnd==Volatile.Read(ref menuOwner))return true;
        long now=Environment.TickCount64;
        return Volatile.Read(ref menuWindows).Any(item=>item.Handle==hwnd&&item.Until>=now);
    }
    internal void ObserveWindowEvent(uint evt,nint hwnd,int objectId,int childId)
    {
        bool desktop=IsDesktopEvent(hwnd);
        if(evt!=0x20) {
            // Popup input is guarded by DesktopMenuController's live bounds.
            // Its lifetime cannot invalidate the underlying desktop geometry.
            if(IsMenuWindow(hwnd))return;
            if(evt==3&&desktop&&(Environment.TickCount64<=Interlocked.Read(ref menuTransitionUntil)
                ||Volatile.Read(ref menuWindows).Any(item=>item.Until==long.MaxValue))) {refresh.Set();return;}
        }
        if(DesktopEventPolicy.ChangesGeometry(evt,objectId,childId,desktop,hwnd!=0&&Native.GetAncestor(hwnd,2)==hwnd))Invalidate();
    }
    public DesktopHitTestService(bool startWorker=true)
    {
        if(!startWorker) {
            var previous=Native.SetThreadDpiAwarenessContext(-4);
            try {Discover();CaptureScene();}finally {if(previous!=0)Native.SetThreadDpiAwarenessContext(previous);}
            return;
        }
        worker=new Thread(Run) {IsBackground=true,Name="Desktop geometry cache"};worker.SetApartmentState(ApartmentState.STA);worker.Start();
    }
    private void Discover()
    {
        ReleaseRemote();listView=0;shellRoot=0;defView=0;explorerPid=0;
        Native.EnumWindows((hwnd,_)=>
        {
            var view=Native.FindWindowEx(hwnd,0,"SHELLDLL_DefView",null);if(view==0)return true;
            shellRoot=hwnd;defView=view;listView=Native.FindWindowEx(view,0,"SysListView32",null);return false;
        },0);
        if(shellRoot==0) {Diagnostic=UiLanguage.Text("未找到 Explorer 桌面，系统菜单已放行");return;}
        Native.GetWindowThreadProcessId(shellRoot,out explorerPid);
        if(listView!=0) {process=Native.OpenProcess(0x38,false,explorerPid);if(process!=0)remote=Native.VirtualAllocEx(process,0,64,0x3000,4);}
    }
    private static bool InteractiveDesktop()
    {
        var desk=Native.OpenInputDesktop(0,false,1);if(desk==0)return false;
        try {var name=new StringBuilder(128);return Native.GetUserObjectInformation(desk,2,name,256,out _)&&name.ToString()=="Default";}
        finally {Native.CloseDesktop(desk);}
    }
    private void Run()
    {
        Native.SetThreadDpiAwarenessContext(-4);
        try
        {
            while(!stop.IsCancellationRequested)
            {
                if(!Native.IsWindow(shellRoot)||!Native.IsWindow(listView)) {Invalidate(false);Discover();}
                CaptureScene();
                if(Native.GetCursorPos(out var point))Snapshot=Hit(point.X,point.Y);
                WaitHandle.WaitAny([stop.Token.WaitHandle,refresh],35);
            }
        }
        catch(Exception ex) {AppLog.Warning("DesktopHitTest","Desktop hit test failed; system menu restored",ex);Diagnostic=UiLanguage.Text("桌面命中服务失败，系统菜单已放行");Invalidate();}
        finally {ReleaseRemote();}
    }
    private static DesktopRect Convert(Native.Rect r,int pad=0) => new(r.Left-pad,r.Top-pad,r.Right+pad,r.Bottom+pad);
    private void CaptureScene()
    {
        long started=Environment.TickCount64,generation=Epoch;
        if(listView==0||remote==0||!InteractiveDesktop()||!Native.GetWindowRect(listView,out var desktop)) {Invalidate(false);return;}
        var icons=new List<DesktopRect>();
        if(Native.IsWindowVisible(listView))
        {
            if(Native.SendMessageTimeout(listView,0x1004,0,0,0x22,30,out var count)==0) {Invalidate(false);return;}
            var origin=new Native.Point(0,0);if(!Native.ClientToScreen(listView,ref origin)) {Invalidate(false);return;}
            for(nuint i=0;i<count;++i)
            {
                if(Environment.TickCount64-started>100) {Invalidate(false);return;}
                var rect=new Native.Rect();
                if(!Native.WriteProcessMemory(process,remote,ref rect,16,out _)||Native.SendMessageTimeout(listView,0x100E,i,remote,0x22,30,out var ok)==0||ok==0||!Native.ReadProcessMemory(process,remote,out rect,16,out _)) {Invalidate(false);return;}
                rect.Left+=origin.X;rect.Right+=origin.X;rect.Top+=origin.Y;rect.Bottom+=origin.Y;icons.Add(Convert(rect,3));
            }
        }
        else if(!Native.GetWindowRect(defView,out desktop)) {Invalidate(false);return;}
        var occlusions=new List<DesktopRect>();var perPixelOcclusions=new List<DesktopRect>();bool reachedDesktop=false;
        void AddOcclusion(nint hwnd,Native.Rect bounds)
        {
            // WPF widgets and other layered windows can pass transparent pixels
            // through to Explorer. Keep disabled windows conservative: USER32's
            // point lookup skips them even when they visibly cover the desktop.
            bool perPixel=(Native.GetWindowLongPtr(hwnd,-20).ToInt64()&0x80000)!=0&&Native.IsWindowEnabled(hwnd);
            (perPixel?perPixelOcclusions:occlusions).Add(Convert(bounds,2));
        }
        Native.EnumWindows((hwnd,_)=>
        {
            if(hwnd==shellRoot) {reachedDesktop=true;return false;}
            if(IsMenuWindow(hwnd))return true;
            if(!Native.IsWindowVisible(hwnd))return true;
            if(Native.DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0&&cloaked!=0)return true;
            if(Native.GetWindowRect(hwnd,out var rect)&&rect.Right>rect.Left&&rect.Bottom>rect.Top)AddOcclusion(hwnd,rect);
            return true;
        },0);
        if(!reachedDesktop||generation!=Epoch) {Volatile.Write(ref scene,null);return;}
        // Desktop widgets inserted above the icon view also remain outside our interception region.
        for(nint child=Native.FindWindowEx(shellRoot,0,null,null);child!=0&&child!=defView;child=Native.FindWindowEx(shellRoot,child,null,null))
        {
            if(Native.IsWindowVisible(child)&&Native.GetWindowRect(child,out var rect))AddOcclusion(child,rect);
        }
        // Screen.AllScreens is a process-wide WinForms cache that may have been
        // populated under another thread's DPI context. Sample monitor bounds
        // in the same PerMonitorV2 context as Explorer/window rectangles instead.
        var screens=new List<DesktopScreen>();bool monitorReadFailed=false;
        if(!Native.EnumDisplayMonitors(0,0,(nint monitor,nint dc,ref Native.Rect bounds,nint data)=> {
            var info=new Native.MonitorInfo {Size=Marshal.SizeOf<Native.MonitorInfo>(),Device=""};
            if(!Native.GetMonitorInfo(monitor,ref info)) {monitorReadFailed=true;return false;}
            screens.Add(new(info.Device,Convert(info.Monitor)));return true;
        },0)||monitorReadFailed||screens.Count==0) {Invalidate(false);return;}
        if(generation!=Epoch) {Volatile.Write(ref scene,null);return;}
        var geometry=new DesktopSceneSnapshot(started,generation,Convert(desktop),screens.ToArray(),icons.ToArray(),occlusions.ToArray()) {PerPixelOcclusions=perPixelOcclusions.ToArray()};
        Volatile.Write(ref scene,new CapturedScene(geometry,listView,defView,shellRoot));
        Diagnostic=UiLanguage.Format($"桌面几何缓存：{icons.Count} 个图标区域，{occlusions.Count} 个遮挡窗口，{perPixelOcclusions.Count} 个逐像素窗口");
    }
    internal HitSnapshot? Hit(int x,int y)
    {
        var captured=Volatile.Read(ref scene);if(captured==null)return null;
        return ResolveHit(captured.Geometry,captured.ListView,captured.DefView,captured.ShellRoot,x,y,Native.WindowFromPhysicalPoint);
    }
    internal static HitSnapshot ResolveHit(DesktopSceneSnapshot geometry,nint listView,nint defView,nint shellRoot,int x,int y,Func<Native.Point,nint> windowAtPoint)
    {
        var candidate=geometry.Hit(x,y,true);if(!candidate.IsBlank)return candidate;
        // A single USER32 lookup uses the hook's physical coordinates and the
        // layered window's actual input shape. No Explorer message, remote read,
        // window enumeration or lock occurs in the mouse hook.
        var target=windowAtPoint(new Native.Point(x,y));
        bool desktop=target!=0&&(target==listView||target==defView||target==shellRoot);
        return candidate with {IsBlank=desktop};
    }
    internal bool IsBlank(Native.Point point) => Hit(point.X,point.Y)?.IsBlank==true;
    private void ReleaseRemote() {if(remote!=0&&process!=0)Native.VirtualFreeEx(process,remote,0,0x8000);if(process!=0)Native.CloseHandle(process);remote=0;process=0;}
    public void Dispose() {stop.Cancel();if(worker!=null)worker.Join(1500);else ReleaseRemote();}
}
