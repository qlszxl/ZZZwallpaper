using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LumaWall.Contracts;

namespace LumaWall.DesktopIntegration;

public sealed record HookObservation(uint Message,int X,int Y,bool Ready,bool Suppressed,long PulseAge,HitSnapshot? Snapshot,long Epoch);

public sealed class DesktopMenuController : IDisposable
{
    private readonly DesktopHitTestService hit;
    private readonly Action<Action> dispatch;
    private readonly Action<MenuRequest> showMenu;
    private readonly nint menuOwner;
    private readonly Thread thread;
    private readonly Native.HookProc hookProc;
    private readonly Native.WinEventProc eventProc;
    private readonly RightButtonSequence sequence = new();
    private readonly ConcurrentQueue<MenuRequest> pending = new();
    private nint hook;
    private uint threadId;
    private volatile bool enabled=true, stopping, menuOpen, failed;
    private long pulse=Environment.TickCount64, observed, registered, requestAt;
    private int failures, swapped, dragX=4, dragY=4;
    private string[] enabledScreens=[];
    private sealed record PopupArea(DesktopRect Bounds);
    private PopupArea? popupArea;
    public void SetMenuBounds(DesktopRect? bounds)=>Volatile.Write(ref popupArea,bounds is { } area?new PopupArea(area):null);
    public void SetEnabledScreens(IEnumerable<string> screens)=>Volatile.Write(ref enabledScreens,screens.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    private bool ScreenEnabled(string screen)=>Volatile.Read(ref enabledScreens).Contains(screen,StringComparer.OrdinalIgnoreCase);
    public bool MenuVerified { get; private set; }
    public string Status => failed ? UiLanguage.Text("接管失败 · 可从托盘重试") : !Enabled ? UiLanguage.Text("桌面右键接管已关闭") : hook==0 ? UiLanguage.Text("正在准备桌面菜单") : MenuVerified ? UiLanguage.Text("桌面菜单已启用") : UiLanguage.Text("右键服务就绪 · 等待桌面验证");
    public bool Enabled => enabled && !failed && Volatile.Read(ref enabledScreens).Length>0;
    public bool HookRegistered => hook!=0;
    public long LastInputObserved => Interlocked.Read(ref observed);
    public HookObservation? LastDecision { get; private set; }
    public event Action<string>? Faulted;

    public DesktopMenuController(DesktopHitTestService hit, Action<Action> dispatch, Action<MenuRequest> showMenu,nint menuOwner,bool safeDisabled=false,IEnumerable<string>? enabledScreenIds=null)
    {
        this.hit=hit; this.dispatch=dispatch; this.showMenu=showMenu; this.menuOwner=menuOwner;enabled=!safeDisabled; failed=safeDisabled;
        hit.SetMenuOwner(menuOwner);
        enabledScreens=enabledScreenIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()??[];
        hookProc=OnMouse;
        eventProc=(_,evt,hwnd,obj,child,_,_) =>
        {
            hit.ObserveWindowEvent(evt,hwnd,obj,child);
        };
        thread=new Thread(Run) { IsBackground=true, Name="Desktop right-button hook" }; thread.Start();
    }
    public void Pulse() => Interlocked.Exchange(ref pulse,Environment.TickCount64);
    public void SetEnabled(bool value) { enabled=value; if(value) { failed=false; failures=0; Pulse(); } hit.Invalidate(); }
    public void SetMenuOpen(bool value) { menuOpen=value;if(value)Interlocked.Exchange(ref requestAt,0);Pulse();if(!value)Native.PostThreadMessage(threadId,0x8001,0,0); }
    public void ConfirmMenuShown() => MenuVerified=true;

    private void Fail(string reason)
    {
        if (failed) return;
        AppLog.Error("DesktopMenu",reason);
        failed=true; enabled=false; hit.Invalidate();
        dispatch(()=>Faulted?.Invoke(reason));
    }

    private void Run()
    {
        Native.SetThreadDpiAwarenessContext(-4);
        threadId=Native.GetCurrentThreadId();
        Native.PeekMessage(out _,0,0,0,0);
        var events = new[] { Native.SetWinEventHook(3,3,0,eventProc,0,0,0), Native.SetWinEventHook(0x20,0x20,0,eventProc,0,0,0), Native.SetWinEventHook(0x8000,0x800C,0,eventProc,0,0,0) };
        var timer=Native.SetTimer(0,0,250,0);
        try
        {
            Maintain();
            while (!stopping && Native.GetMessage(out var msg,0,0,0)>0)
            {
                if (msg.Id==0x113) Maintain();
                else if(msg.Id==0x8001) DrainMenuRequests();
                else { Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg); }
            }
        }
        finally
        {
            if(hook!=0) Native.UnhookWindowsHookEx(hook); hook=0;
            Native.KillTimer(0,timer);
            foreach(var h in events) if(h!=0) Native.UnhookWinEvent(h);
        }
    }

    private void Maintain()
    {
        var now=Environment.TickCount64;
        swapped=Native.GetSystemMetrics(23); dragX=Math.Max(4,Native.GetSystemMetrics(68)); dragY=Math.Max(4,Native.GetSystemMetrics(69));
        long pendingAt=Interlocked.Read(ref requestAt);
        if (pendingAt!=0 && now-pendingAt>1000) Fail(UiLanguage.Text("菜单服务未响应，已释放右键接管"));
        if (Enabled && now-Interlocked.Read(ref pulse)>2500 && !menuOpen) Fail(UiLanguage.Text("控制中心无响应，已释放右键接管"));
        bool released=(Native.GetAsyncKeyState(1)&0x8000)==0 && (Native.GetAsyncKeyState(2)&0x8000)==0;
        // Registration is a lease, not proof of health. Refresh only with both buttons up and no menu/sequence.
        if (!sequence.IsCaptured && !menuOpen && released)
        {
            if(hook!=0 && (!Enabled || now-registered>15000)) { Native.UnhookWindowsHookEx(hook); hook=0; }
            if(Enabled && hook==0)
            {
                hook=Native.SetWindowsHookEx(14,hookProc,Native.GetModuleHandle(null),0); registered=now;
                if(hook==0 && ++failures>=3) Fail(UiLanguage.Text("鼠标钩子注册连续失败，接管已安全停用"));
            }
        }
    }

    private nint OnMouse(int code, nuint message, nint ptr)
    {
        if(code<0) return Native.CallNextHookEx(hook,code,message,ptr);
        var data=Marshal.PtrToStructure<Native.MouseData>(ptr);
        Interlocked.Exchange(ref observed,Environment.TickCount64);
        var msg=(uint)message;
        var down=swapped!=0?0x201u:0x204u;
        var up=swapped!=0?0x202u:0x205u;
        var now=Environment.TickCount64;
        // An outside secondary click may close the current menu and open the next one.
        // Inside clicks are excluded by the popup's cached occlusion rectangle.
        // The menu's first click must not wait for the desktop occlusion cache.
        // Its own input always passes through, including on rapid reopen.
        bool insideMenu=Volatile.Read(ref popupArea)?.Bounds.Contains(data.Point.X,data.Point.Y)==true;
        bool ready=!insideMenu && Enabled && requestAt==0 && (menuOpen||now-Interlocked.Read(ref pulse)<1000);
        if(menuOpen&&(msg is 0x201 or 0x204 or 0x207))Native.PostMessage(menuOwner,DesktopShell.CheckMenuClickMessage,unchecked((nuint)data.Point.X),data.Point.Y);
        InputDecision decision=default;
        var snapshot=msg==down||msg==up?hit.Hit(data.Point.X,data.Point.Y):null;
        if(msg==down) decision=sequence.Down(data.Point.X,data.Point.Y,snapshot,now,hit.Epoch,ready&&snapshot!=null&&ScreenEnabled(snapshot.ScreenId));
        else if(msg==up) decision=sequence.Up(data.Point.X,data.Point.Y,ready);
        else if(msg==0x200) sequence.Move(data.Point.X,data.Point.Y,dragX,dragY);
        if(msg==down||msg==up)LastDecision=new HookObservation(msg,data.Point.X,data.Point.Y,ready,decision.Suppress,now-Interlocked.Read(ref pulse),snapshot,hit.Epoch);
        if(decision.Menu is { } request)
        {
            Interlocked.Exchange(ref requestAt,now); pending.Enqueue(request);
            Native.PostThreadMessage(threadId,0x8001,0,0);
        }
        return decision.Suppress?1:Native.CallNextHookEx(hook,code,message,ptr);
    }

    private void DrainMenuRequests()
    {
        if(menuOpen)return; // Never nest popup loops; closing one wakes this queue.
        while(pending.TryDequeue(out var request))
        {
            dispatch(()=>
            {
                if(!Enabled || Environment.TickCount64-Interlocked.Read(ref requestAt)>1000) { Fail(UiLanguage.Text("菜单请求超时，系统菜单已恢复")); return; }
                try { showMenu(request); }
                catch(Exception ex) {AppLog.Error("DesktopMenu","Menu display failed",ex);Fail(UiLanguage.Text("自定义菜单显示失败，系统菜单已恢复"));}
                finally { SetMenuOpen(false); }
            });
        }
    }
    public void Dispose() { enabled=false; stopping=true; Native.PostThreadMessage(threadId,0x12,0,0); thread.Join(1500); }
}
