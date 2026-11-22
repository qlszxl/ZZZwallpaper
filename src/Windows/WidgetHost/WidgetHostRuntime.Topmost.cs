using System.Runtime.InteropServices;
namespace LumaWall.WidgetHost;
internal sealed partial class WidgetHostRuntime
{
    private delegate void WinEvent(nint hook,uint kind,nint window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min,uint max,nint module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    private WinEvent? topmostCallback;
    private nint foregroundHook,locationHook;
    private readonly System.Windows.Forms.Timer topmostSettle=new() {Interval=100};
    private bool topmostReady;
    private void RefreshTopmostHooks()
    {
        if(!current.Widgets.Any(w=>w.Enabled&&w.AlwaysOnTop)){StopTopmostHooks();return;}
        if(topmostCallback!=null)return;
        if(!topmostReady){topmostReady=true;topmostSettle.Tick+=(_,_)=>{topmostSettle.Stop();ApplyStacking();};}
        topmostCallback=(_,kind,window,obj,child,_,_)=> {
            if(stopping||window==0||obj!=0||child!=0||window!=GetForegroundWindow())return;
            // Out-of-context callbacks arrive on this host's message thread.
            // Coalesce resize/foreground bursts; no permanent z-order polling.
            if(!topmostSettle.Enabled)topmostSettle.Start();
        };
        foregroundHook=SetWinEventHook(3,3,0,topmostCallback,0,0,2);
        locationHook=SetWinEventHook(0x800B,0x800B,0,topmostCallback,0,0,2);
    }
    private void StopTopmostHooks()
    {
        topmostSettle.Stop();if(foregroundHook!=0)UnhookWinEvent(foregroundHook);if(locationHook!=0)UnhookWinEvent(locationHook);
        foregroundHook=locationHook=0;topmostCallback=null;
        if(stopping)topmostSettle.Dispose();
    }
}
