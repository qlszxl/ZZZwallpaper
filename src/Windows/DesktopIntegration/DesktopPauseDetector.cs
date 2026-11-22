namespace LumaWall.DesktopIntegration;

internal readonly record struct PauseScreen(string Id,Rectangle Bounds,Rectangle WorkingArea);
internal readonly record struct PauseWindow(Rectangle Bounds,bool Maximized,int ProcessId,string ClassName,bool Visible=true,bool Minimized=false,bool Cloaked=false,bool Opaque=true);

internal static class DesktopPauseDetector
{
    internal static HashSet<string> CoveredScreens(int? widgetProcessId)
    {
        var screens=Screen.AllScreens.Select(s=>new PauseScreen(s.DeviceName,s.Bounds,s.WorkingArea)).ToArray();
        var windows=new List<PauseWindow>();
        Native.EnumWindows((hwnd,_)=> {
            if(!Native.IsWindowVisible(hwnd)||Native.IsIconic(hwnd))return true;
            if(Native.DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0&&cloaked!=0)return true;
            string name=Native.Class(hwnd);
            // Stop below the visible desktop icon host. In Show Desktop mode,
            // windows remaining underneath it must not keep the wallpaper paused.
            if(name is "Progman" or "WorkerW"&&Native.FindWindowEx(hwnd,0,"SHELLDLL_DefView",null)!=0)return false;
            Native.GetWindowThreadProcessId(hwnd,out uint pid);
            if(IsExcluded(name,unchecked((int)pid),Environment.ProcessId,widgetProcessId))return true;
            if(!IsOpaque(hwnd))return true;
            // DWM bounds exclude invisible resize margins that can otherwise make
            // a near-fullscreen window appear to cover the taskbar gap.
            if(Native.DwmGetWindowBounds(hwnd,9,out var rect,16)!=0&&!Native.GetWindowRect(hwnd,out rect))return true;
            windows.Add(new(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),Native.IsZoomed(hwnd),unchecked((int)pid),name));
            return true;
        },0);
        return CoveredScreens(screens,windows,Environment.ProcessId,widgetProcessId);
    }

    internal static HashSet<string> CoveredScreens(IEnumerable<PauseScreen> screens,IEnumerable<PauseWindow> windows,int ownProcessId,int? widgetProcessId=null)
    {
        var monitors=screens.ToArray();var result=new HashSet<string>(StringComparer.Ordinal);
        foreach(var window in windows) {
            if(!window.Visible||window.Minimized||window.Cloaked||!window.Opaque||IsExcluded(window.ClassName,window.ProcessId,ownProcessId,widgetProcessId))continue;
            foreach(var screen in monitors)
                if(Covers(window.Bounds,screen.Bounds)||(window.Maximized&&Covers(window.Bounds,screen.WorkingArea)))result.Add(screen.Id);
        }
        return result;
    }

    private static bool IsExcluded(string name,int processId,int ownProcessId,int? widgetProcessId)=>processId==ownProcessId||processId==widgetProcessId||
        name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "LumaWall.VideoOutput";
    internal static bool IsOpaque(nint hwnd)
    {
        long style=Native.GetWindowLongPtr(hwnd,-20).ToInt64();
        bool layered=(style&0x80000)!=0;
        byte alpha=255;uint flags=0;
        bool known=layered&&Native.GetLayeredWindowAttributes(hwnd,out _,out alpha,out flags);
        return IsOpaque(style,known,alpha,flags);
    }
    internal static bool IsOpaque(long style,bool attributesKnown,byte alpha,uint flags)
    {
        const long layered=0x80000,transparent=0x20,noRedirection=0x200000;
        // Mouse-transparent ordinary windows are not necessarily visually transparent.
        // DirectComposition overlays using both flags cannot prove opaque coverage.
        if((style&(transparent|noRedirection))==(transparent|noRedirection))return false;
        if((style&layered)==0)return true;
        // UpdateLayeredWindow (per-pixel alpha) does not expose global attributes.
        // Prefer leaving a visible wallpaper running over pausing on an unknown overlay.
        return attributesKnown&&(flags&1)==0&&((flags&2)==0||alpha==255);
    }
    private static bool Covers(Rectangle window,Rectangle target)=>target.Width>0&&target.Height>0&&window.Width>0&&window.Height>0&&
        window.Left<=target.Left&&window.Top<=target.Top&&window.Right>=target.Right&&window.Bottom>=target.Bottom;
}
