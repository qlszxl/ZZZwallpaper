using System.Runtime.InteropServices;
using System.Text;
using LumaWall.Diagnostics;
using Microsoft.UI.Dispatching;

namespace LumaWall.ControlCenter;

// Legacy shell file drops supplement XAML's existing OLE drop target. Nothing
// here changes integrity levels, message filters, or OLE registrations.
internal sealed class NativeFileDropTarget : IDisposable
{
    private const uint WmDropFiles=0x0233,WmNcDestroy=0x0082;
    private static long nextSubclassId=0x4c574400;
    private static readonly object lifetimeLock=new();
    private static readonly HashSet<NativeFileDropTarget> liveTargets=new();

    private readonly nint window;
    private readonly uint threadId,processId;
    private readonly nuint subclassId;
    private readonly Func<nint,int,int,bool> canAcceptAt;
    private readonly Action<string[]> filesDropped;
    private readonly SubclassProcedure subclassProcedure;
    private readonly EnumerateWindowProcedure enumerateWindowProcedure;
    private readonly DispatcherQueue? dispatcher;
    private readonly HashSet<nint> hookedWindows=new();
    private readonly bool ownsWindow;
    private volatile bool enabled,disposed;
    private bool rootDestroyed;

    internal NativeFileDropTarget(nint window,Func<nint,int,int,bool> canAcceptAt,Action<string[]> filesDropped)
    {
        this.window=window;this.canAcceptAt=canAcceptAt;this.filesDropped=filesDropped;
        threadId=GetCurrentThreadId();processId=GetCurrentProcessId();
        subclassId=unchecked((nuint)Interlocked.Increment(ref nextSubclassId));
        subclassProcedure=WindowProcedure;enumerateWindowProcedure=AttachChild;
        dispatcher=DispatcherQueue.GetForCurrentThread();
        ownsWindow=IsOwnedWindow(window);
        if(!ownsWindow)Log("Cannot attach native file drops: the window does not belong to the calling UI thread.");
    }

    // Invoke on the owning UI thread after loading/navigation/layout changes.
    // Re-enumeration also finds input child HWNDs created after initial loading.
    internal void Refresh(bool enabled)
    {
        if(disposed||!ownsWindow||rootDestroyed)return;
        if(GetCurrentThreadId()!=threadId) {Log("Native file drop refresh must run on the owning UI thread.");return;}
        this.enabled=enabled;
        try {
            if(!IsOwnedWindow(window)) {rootDestroyed=true;this.enabled=false;DetachAll();return;}
            if(enabled) {
                Attach(window);
                EnumChildWindows(window,enumerateWindowProcedure,0);
            }
            foreach(var handle in hookedWindows.ToArray()) {
                if(IsOwnedWindow(handle))DragAcceptFiles(handle,enabled);
                else hookedWindows.Remove(handle);
            }
            UpdateLifetime();
        } catch(Exception ex) {Log("Cannot refresh native file drop targets.",ex);}
    }

    private bool IsOwnedWindow(nint handle)=>handle!=0&&IsWindow(handle)
        &&GetWindowThreadProcessId(handle,out uint ownerProcess)==threadId&&ownerProcess==processId;

    private bool AttachChild(nint handle,nint parameter)
    {
        // EnumChildWindows can encounter windows from a different UI thread.
        // The common-control subclass helpers explicitly forbid that case.
        try {if(!disposed&&enabled)Attach(handle);}
        catch(Exception ex) {Log("Cannot register a native file drop child window.",ex);}
        return true;
    }

    private void Attach(nint handle)
    {
        if(hookedWindows.Contains(handle)||!IsOwnedWindow(handle))return;
        if(!SetWindowSubclass(handle,subclassProcedure,subclassId,0)) {
            Log($"SetWindowSubclass failed for native file drop target 0x{handle:X}.");return;
        }
        hookedWindows.Add(handle);UpdateLifetime();
    }

    private nint WindowProcedure(nint handle,uint message,nuint wParam,nint lParam,nuint id,nuint data)
    {
        // No managed exception may escape this reverse P/Invoke callback.
        try {
            if(message==WmDropFiles) {ReceiveFiles(handle,unchecked((nint)wParam));return 0;}
            if(message==WmNcDestroy) {
                if(handle==window) {rootDestroyed=true;enabled=false;}
                DragAcceptFiles(handle,false);
                if(!RemoveWindowSubclass(handle,subclassProcedure,subclassId))
                    Log($"Cannot remove destroyed native file drop target 0x{handle:X}.");
                // This HWND is being destroyed; it cannot retain a callback
                // beyond WM_NCDESTROY, even if removal reported failure.
                hookedWindows.Remove(handle);UpdateLifetime();
            }
        } catch(Exception ex) {Log("Native file drop window callback failed.",ex);}
        try {return DefSubclassProc(handle,message,wParam,lParam);}
        catch(Exception ex) {Log("Cannot forward native file drop window message.",ex);return 0;}
    }

    private void ReceiveFiles(nint handle,nint drop)
    {
        string[]? paths=null;
        try {
            if(drop==0||disposed||!enabled||!DragQueryPoint(drop,out var point))return;
            // DragQueryPoint uses the receiving HWND's client coordinates and
            // returns false for title-bar/non-client drops, which we reject.
            if(!canAcceptAt(handle,point.X,point.Y))return;
            uint count=DragQueryFile(drop,uint.MaxValue,null,0);
            var collected=new List<string>();
            for(uint index=0;index<count;index++) {
                uint length=DragQueryFile(drop,index,null,0);if(length==0)continue;
                var path=new StringBuilder(checked((int)length+1));
                uint copied=DragQueryFile(drop,index,path,(uint)path.Capacity);
                if(copied>0&&copied<path.Capacity)collected.Add(path.ToString());
            }
            if(collected.Count>0)paths=collected.ToArray();
        } catch(Exception ex) {Log("Cannot read native dropped file paths.",ex);}
        finally {
            if(drop!=0) {
                try {DragFinish(drop);}
                catch(Exception ex) {Log("Cannot release native file drop data.",ex);}
            }
        }
        // Only managed path strings escape the callback. The owner queues the
        // actual import, after the shell-owned HDROP memory has been released.
        if(paths!=null&&!disposed) {
            try {filesDropped(paths);}
            catch(Exception ex) {Log("Cannot dispatch native dropped files.",ex);}
        }
    }

    public void Dispose()
    {
        disposed=true;enabled=false;
        if(GetCurrentThreadId()==threadId) {DetachAll();return;}
        // Never remove subclasses across threads. If shutdown no longer allows
        // dispatch, keep delegates rooted until their HWNDs receive NCDESTROY.
        try {
            if(dispatcher?.TryEnqueue(DetachAll)==true)return;
        } catch(Exception ex) {Log("Cannot queue native file drop cleanup.",ex);}
        Log("Native file drop cleanup will finish when its windows are destroyed.");
    }

    private void DetachAll()
    {
        foreach(var handle in hookedWindows.ToArray()) {
            try {
                if(!IsOwnedWindow(handle)) {hookedWindows.Remove(handle);continue;}
                DragAcceptFiles(handle,false);
                if(RemoveWindowSubclass(handle,subclassProcedure,subclassId))hookedWindows.Remove(handle);
                else Log($"RemoveWindowSubclass failed for native file drop target 0x{handle:X}.");
            } catch(Exception ex) {Log("Cannot remove native file drop target.",ex);}
        }
        UpdateLifetime();
    }

    private void UpdateLifetime()
    {
        // Native subclass registration does not root a managed delegate. Retain
        // the instance until every hook is removed, including a failed Dispose.
        lock(lifetimeLock) {
            if(hookedWindows.Count>0)liveTargets.Add(this);else liveTargets.Remove(this);
        }
    }

    private static void Log(string message,Exception? exception=null)
    {
        try {AppLog.Warning("LibraryDrop",message,exception);}catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point {public int X,Y;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint handle,uint message,nuint wParam,nint lParam,nuint id,nuint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumerateWindowProcedure(nint handle,nint parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle,out uint processId);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent,EnumerateWindowProcedure callback,nint parameter);
    [DllImport("comctl32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint handle,SubclassProcedure callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint handle,SubclassProcedure callback,nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint handle,uint message,nuint wParam,nint lParam);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(nint handle,[MarshalAs(UnmanagedType.Bool)]bool accept);
    [DllImport("shell32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DragQueryPoint(nint drop,out Point point);
    [DllImport("shell32.dll",EntryPoint="DragQueryFileW",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint DragQueryFile(nint drop,uint index,StringBuilder? path,uint capacity);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint drop);
}
