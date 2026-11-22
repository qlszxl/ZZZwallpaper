using System.Runtime.InteropServices;
using System.Text.Json;
using LumaWall.WidgetSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Rectangle=System.Drawing.Rectangle;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record SavedWindowPlacement(string Monitor,string Device,double X,double Y,double Width,double Height,bool Maximized,int DisplayWidth=0,int DisplayHeight=0,double DpiScale=1,double LayoutScale=0);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct MonitorDisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceKey;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string device,uint index,ref MonitorDisplayDevice info,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct PlacementPoint {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct PlacementRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public uint Length,Flags,ShowCommand;
        public PlacementPoint MinimumPosition,MaximumPosition;
        public PlacementRect NormalPosition;
    }
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window,ref NativeWindowPlacement placement);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]
    private static extern uint PlacementWindowStyle(nint window,int index);
    private SavedWindowPlacement? savedWindowPlacement;
    private DispatcherQueueTimer? placementTimer;
    private bool restoringWindowPlacement,placementStopped,placementLastMaximized;
    private string? lastPlacementJson;
    private double startupPlacementScale=1;
    private static string MonitorIdentity(Screen screen)
    {
        var device=new MonitorDisplayDevice {Size=Marshal.SizeOf<MonitorDisplayDevice>(),DeviceName="",DeviceString="",DeviceID="",DeviceKey=""};
        return EnumDisplayDevices(screen.DeviceName,0,ref device,1)?device.DeviceID:"";
    }
    private static Screen? PlacementScreen(SavedWindowPlacement placement)=>Screen.AllScreens.FirstOrDefault(s=>
        WindowPlacementGeometry.MatchesMonitor(placement.Monitor,placement.Device,
            placement.Monitor.Length>0?MonitorIdentity(s):"",s.DeviceName));
    private void InitializeWindowPlacement()
    {
        try {
            string? json=modules.Settings.Get("window.placement.v1");
            if(json!=null) {
                var value=JsonSerializer.Deserialize<SavedWindowPlacement>(json);
                if(value!=null&&value.Monitor!=null&&value.Device!=null&&new[]{value.X,value.Y,value.Width,value.Height}.All(double.IsFinite)
                    &&value.Width is >=200 and <=32768&&value.Height is >=150 and <=32768&&Math.Abs(value.X)<=32768&&Math.Abs(value.Y)<=32768)
                    savedWindowPlacement=value;
            }
        } catch(Exception ex) {AppLog.Warning("WindowPlacement","Cannot read saved window position",ex);}
        RestoreWindowPlacement();
        placementTimer=DispatcherQueue.CreateTimer();placementTimer.Interval=TimeSpan.FromMilliseconds(350);placementTimer.IsRepeating=false;
        placementTimer.Tick+=(_,_)=>SaveWindowPlacement();
        AppWindow.Changed+=(_,args)=> {
            if(restoringWindowPlacement||placementStopped||!(args.DidPositionChange||args.DidSizeChange||args.DidPresenterChange))return;
            // Movement events are frequent. Read display identities, DPI and the
            // normal rectangle only after movement settles (or on explicit save).
            if(AppWindow.Presenter is OverlappedPresenter presenter&&presenter.State!=OverlappedPresenterState.Minimized)
                placementLastMaximized=presenter.State==OverlappedPresenterState.Maximized;
            placementTimer.Stop();placementTimer.Start();
        };
    }
    private void RestoreWindowPlacement()
    {
        restoringWindowPlacement=true;
        try {
            var saved=savedWindowPlacement;var target=saved==null?null:PlacementScreen(saved);
            bool center=target==null;target??=Screen.PrimaryScreen??Screen.AllScreens[0];
            var area=target.WorkingArea;double scale=WidgetPlacement.Scale(target);
            double factor=saved is {DisplayWidth:>0,DisplayHeight:>0}?Math.Min(target.Bounds.Width/(double)saved.DisplayWidth,target.Bounds.Height/(double)saved.DisplayHeight)*saved.DpiScale/scale:1;
            startupPlacementScale=WindowPlacementGeometry.ResolutionScale(target.Bounds)/scale;
            double sizeFactor=WindowPlacementGeometry.StartupSizeFactor(startupPlacementScale,saved?.LayoutScale??0,factor);
            WindowPlacementGeometry.LogicalBounds? logical=saved==null?new(0,0,WindowPlacementGeometry.DefaultWidth*startupPlacementScale,WindowPlacementGeometry.DefaultHeight*startupPlacementScale):new(saved.X*factor,saved.Y*factor,saved.Width*sizeFactor,saved.Height*sizeFactor);
            var bounds=WindowPlacementGeometry.Restore(logical,area,scale,center);
            bool maximized=saved?.Maximized==true;
            if(AppWindow.Presenter is OverlappedPresenter presenter) {presenter.IsMaximizable=true;presenter.Restore();}
            AppWindow.MoveAndResize(new RectInt32(bounds.X,bounds.Y,bounds.Width,bounds.Height));
            var restored=WindowPlacementGeometry.ToLogical(bounds,area,scale);
            savedWindowPlacement=new(MonitorIdentity(target),target.DeviceName,restored.X,restored.Y,restored.Width,restored.Height,maximized,target.Bounds.Width,target.Bounds.Height,scale,startupPlacementScale);
            // A removed monitor always falls back to a centered normal window.
            if(maximized&&!center&&(AppWindow.Presenter is OverlappedPresenter maxPresenter))maxPresenter.Maximize();
            else savedWindowPlacement=savedWindowPlacement with {Maximized=false};
            placementLastMaximized=savedWindowPlacement.Maximized;
        } finally {restoringWindowPlacement=false;}
    }
    private void CaptureWindowPlacement()
    {
        if(placementStopped||restoringWindowPlacement||AppWindow.Presenter is not OverlappedPresenter presenter)return;
        if(savedWindowPlacement!=null&&PlacementScreen(savedWindowPlacement)==null)return;
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        var native=new NativeWindowPlacement {Length=(uint)Marshal.SizeOf<NativeWindowPlacement>()};
        bool captured=GetWindowPlacement(handle,ref native);
        Rectangle normal;bool workspaceCoordinates=false;
        if(presenter.State==OverlappedPresenterState.Restored) {
            // In normal state use the visible bounds, including Windows Snap.
            var position=AppWindow.Position;var size=AppWindow.Size;
            normal=new(position.X,position.Y,size.Width,size.Height);
        } else {
            if(!captured)return;
            normal=Rectangle.FromLTRB(native.NormalPosition.Left,native.NormalPosition.Top,native.NormalPosition.Right,native.NormalPosition.Bottom);
            workspaceCoordinates=WindowPlacementGeometry.UsesWorkspaceCoordinates(PlacementWindowStyle(handle,-20));
        }
        if(normal.Width<200||normal.Height<150)return;
        var screens=Screen.AllScreens;
        var displays=screens.Select(s=>new WindowPlacementGeometry.Display(s.DeviceName,s.Bounds,s.WorkingArea)).ToArray();
        if(!WindowPlacementGeometry.TryResolveNormalBounds(normal,workspaceCoordinates,displays,Screen.FromHandle(handle).DeviceName,out int index,out var bounds))return;
        var screen=screens[index];var logical=WindowPlacementGeometry.ToLogical(bounds,screen.WorkingArea,WidgetPlacement.Scale(screen));
        bool maximized=captured
            ?WindowPlacementGeometry.RestoreMaximized(native.ShowCommand,native.Flags,placementLastMaximized)
            :presenter.State==OverlappedPresenterState.Maximized;
        placementLastMaximized=maximized;
        savedWindowPlacement=new(MonitorIdentity(screen),screen.DeviceName,logical.X,logical.Y,logical.Width,logical.Height,maximized,screen.Bounds.Width,screen.Bounds.Height,WidgetPlacement.Scale(screen),startupPlacementScale);
    }
    internal void EnsureWindowPlacementVisible()
    {
        if(placementStopped||savedWindowPlacement==null)return;
        // Keep the previous monitor identity until this check, so disconnecting
        // a screen cannot turn an off-screen coordinate into a valid new save.
        var display=PlacementScreen(savedWindowPlacement);
        if(display!=null && (savedWindowPlacement.DisplayWidth<=0 ||
            (savedWindowPlacement.DisplayWidth==display.Bounds.Width && savedWindowPlacement.DisplayHeight==display.Bounds.Height)))return;
        if(AppWindow.Presenter is not OverlappedPresenter presenter)return;
        // Only recover an unreachable caption. WM_DPICHANGED and user resizes keep
        // their native behavior; neither chooses a new application layout scale.
        var position=AppWindow.Position;var size=AppWindow.Size;
        var caption=new Rectangle(position.X,position.Y,Math.Max(1,size.Width),32);
        bool reachable=Screen.AllScreens.Any(s=>Rectangle.Intersect(s.WorkingArea,caption) is {Width:>=64,Height:>=12});
        if(!reachable) {
            var target=display??Screen.PrimaryScreen??Screen.AllScreens[0];
            var area=target.WorkingArea;
            var state=presenter.State;
            restoringWindowPlacement=true;
            try {
                if(state!=OverlappedPresenterState.Restored)presenter.Restore();
                size=AppWindow.Size;
                AppWindow.Move(new PointInt32(area.X+Math.Max(0,(area.Width-size.Width)/2),area.Y+Math.Max(0,(area.Height-size.Height)/2)));
                if(state==OverlappedPresenterState.Maximized)presenter.Maximize();
                else if(state==OverlappedPresenterState.Minimized)presenter.Minimize();
            } finally {restoringWindowPlacement=false;}
        }
        if(display==null)savedWindowPlacement=null;
        SaveWindowPlacement();
    }
    private void SaveWindowPlacement()
    {
        if(placementStopped||restoringWindowPlacement)return;
        placementTimer?.Stop();CaptureWindowPlacement();if(savedWindowPlacement==null)return;
        try {
            string json=JsonSerializer.Serialize(savedWindowPlacement);if(json==lastPlacementJson)return;
            modules.Settings.Set("window.placement.v1",json);lastPlacementJson=json;
        } catch(Exception ex) {AppLog.Warning("WindowPlacement","Cannot save window position",ex);}
    }
    internal void ShutdownWindowPlacement()
    {
        SaveWindowPlacement();placementStopped=true;placementTimer?.Stop();
    }
}
