using LumaWall.DesktopIntegration;
using LumaWall.Storage;
using Microsoft.UI.Dispatching;
using System.Text.Json;

namespace LumaWall.ControlCenter;

/// <summary>Owns the desktop maintenance clock; construction and disposal are paired by App.</summary>
internal sealed class DesktopRuntimePump : IDisposable
{
    private readonly DispatcherQueueTimer timer;
    private readonly DispatcherQueue queue;
    private readonly LibraryStore store;
    private readonly WallpaperController wallpapers;
    private readonly DesktopMenuController menu;
    private readonly DesktopHitTestService hit;
    private readonly DesktopShell shell;
    private readonly Action refreshStatus;
    private readonly bool diagnostics;
    private int ticks;
    private bool refreshQueued,maintenanceDue,disposed;
    private Task diagnosticWrite=Task.CompletedTask;

    internal DesktopRuntimePump(DispatcherQueue queue, LibraryStore store, WallpaperController wallpapers,
        DesktopMenuController menu, DesktopHitTestService hit, DesktopShell shell, Action refreshStatus, bool diagnostics)
    {
        this.store=store;this.wallpapers=wallpapers;this.menu=menu;this.hit=hit;this.shell=shell;
        this.queue=queue;this.refreshStatus=refreshStatus;this.diagnostics=diagnostics;
        timer=queue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(250);
        timer.Tick+=OnTick;timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        menu.Pulse();
        // Polling must not turn a background effect write into a UI-thread wait.
        // Playback clocks account for elapsed time on their next successful tick.
        if(!store.HasPendingEffectWrite)wallpapers.TickPlayback();
        if(++ticks%4!=0)return;
        if(ticks%1200==0)maintenanceDue=true;
        wallpapers.CheckFullscreen();
        if(!refreshQueued)refreshQueued=queue.TryEnqueue(DispatcherQueuePriority.Low,RefreshWhenIdle);
        if(!diagnostics||!diagnosticWrite.IsCompleted)return;
        // Freeze UI-owned collections before the worker serializes/writes them.
        var snapshot=new {
            menu.Status,menu.HookRegistered,menu.LastInputObserved,menu.LastDecision,
            hit.Diagnostic,hit.Snapshot,hit.Epoch,shell.LastMenuActivated,shell.OutsideDismissals,
            outputs=wallpapers.Running.Select(pair=>new {screen=pair.Key,pair.Value.Player.ActualState,
                pair.Value.Player.OutputWindow,pair.Value.Player.DesktopParent,pair.Value.Player.Layered,pair.Value.Player.FirstPresent}).ToArray(),
            now=Environment.TickCount64
        };
        string destination=Path.Combine(store.Root,"desktop-diagnostics.json");
        diagnosticWrite=Task.Run(()=> {
            try {File.WriteAllText(destination,JsonSerializer.Serialize(snapshot));}
            catch(Exception error){AppLog.Warning("DesktopDiagnostics","Cannot write desktop snapshot",error);}
        });
    }

    private void RefreshWhenIdle()
    {
        refreshQueued=false;
        if(disposed||store.HasPendingEffectWrite)return;
        refreshStatus();
        if(!maintenanceDue)return;
        maintenanceDue=false;
        // SQLite and undo history stay on their owning thread; only independent
        // generated pixel-file retention may run on a worker.
        store.MaintainStorage();_ = Task.Run(LibraryStore.MaintainPixelFiles);
    }

    public void Dispose(){disposed=true;timer.Stop();timer.Tick-=OnTick;}
}
