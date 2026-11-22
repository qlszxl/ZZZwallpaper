using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

/// <summary>Keeps native small/large icons alive independently of the custom title bar.</summary>
public sealed class WindowIcon : IDisposable
{
    private nint window, small, large;
    private readonly string path;
    private uint appliedDpi;
    private bool applying;
    private bool subclassed;
    private readonly SubclassProc iconProc;
    private const nuint SubclassId=0x4C5749;
    private delegate nint SubclassProc(nint window,uint message,nuint wparam,nint lparam,nuint id,nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd,SubclassProc callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd,SubclassProc callback,nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd,uint message,nuint wparam,nint lparam);
    private const uint GetIcon = 0x007F, SetIcon = 0x0080;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageW(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    public WindowIcon(nint window, string path) { this.window = window; this.path = path;iconProc=WindowMessage; }

    private nint WindowMessage(nint hwnd,uint message,nuint wparam,nint lparam,nuint id,nuint data)
    {
        // The custom WinUI caption can answer WM_GETICON with its default icon.
        // Answer all Shell icon sizes directly, using our DPI-sized owned handles.
        if(message==GetIcon) {
            nint icon=wparam==1?large:small;
            if(icon!=0)return icon;
        }
        if(message==0x0082) {RemoveWindowSubclass(hwnd,iconProc,SubclassId);subclassed=false;}
        return DefSubclassProc(hwnd,message,wparam,lparam);
    }

    public void Apply()
    {
        if(window == 0 || applying || !Native.IsWindow(window)) return;
        applying = true;
        try
        {
            uint dpi = GetDpiForWindow(window);
            if(dpi == 0) dpi = 96;
            if(small == 0 || large == 0 || dpi != appliedDpi)
            {
                nint nextSmall = LoadImageW(0, path, 1, GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi), 0x10);
                nint nextLarge = LoadImageW(0, path, 1, GetSystemMetricsForDpi(11, dpi), GetSystemMetricsForDpi(12, dpi), 0x10);
                if(nextSmall == 0 || nextLarge == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    if(nextSmall != 0) Native.DestroyIcon(nextSmall);
                    if(nextLarge != 0) Native.DestroyIcon(nextLarge);
                    throw new Win32Exception(error, "Cannot load the window icons.");
                }
                // Replace both references before releasing handles owned by us.
                SendMessageW(window, SetIcon, 0, nextSmall);
                SendMessageW(window, SetIcon, 1, nextLarge);
                if(small != 0) Native.DestroyIcon(small);
                if(large != 0) Native.DestroyIcon(large);
                small = nextSmall; large = nextLarge; appliedDpi = dpi;
            }
            else
            {
                // Caption/presenter changes can reset the native icon. Reapply on
                // activation or DPI changes, without allocating on every layout.
                SendMessageW(window, SetIcon, 0, small);
                SendMessageW(window, SetIcon, 1, large);
            }
            if(!subclassed)subclassed=SetWindowSubclass(window,iconProc,SubclassId,0);
        }
        finally { applying = false; }
    }

    public void Dispose()
    {
        if(window == 0) return;
        if(Native.IsWindow(window))
        {
            if(small != 0 && SendMessageW(window, GetIcon, 0, 0) == small) SendMessageW(window, SetIcon, 0, 0);
            if(large != 0 && SendMessageW(window, GetIcon, 1, 0) == large) SendMessageW(window, SetIcon, 1, 0);
            if(subclassed)RemoveWindowSubclass(window,iconProc,SubclassId);
        }
        subclassed=false;
        if(small != 0) Native.DestroyIcon(small);
        if(large != 0) Native.DestroyIcon(large);
        window = small = large = 0;
    }
}
