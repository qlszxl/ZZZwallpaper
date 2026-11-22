using LumaWall.Contracts;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LumaWall.DesktopIntegration;

public sealed record TrayMenuState(bool HasWallpaper,bool AllPaused,bool AnySound,bool AnyEffects,bool AnyWidgets);

public sealed partial class DesktopShell : NativeWindow, IDisposable
{
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    private readonly nint powerNotification;
    private nint activeMenu;
    private MenuPopup? menuPopup;
    private readonly string menuDiagnosticsDirectory;
    private bool desktopMenuActive, trayMenuPending;
    private Point trayMenuAnchor;
    internal const uint CheckMenuClickMessage=0x8002;
    private const int ShowTrayMenuMessage=0x8003;
    public bool LastMenuActivated {get;private set;}
    public int OutsideDismissals {get;private set;}
    public event Action? MenuShown;
    public event Action<Rectangle?>? MenuBoundsChanged;
    public event Action<nint,bool>? MenuWindowChanged;
    public event Action<DesktopMenuVolume>? VolumePreviewRequested,VolumeCommitted;
    public Func<TrayMenuState>? ReadTrayState {get;set;}
    public event Action<int>? TrayCommandRequested;
    public event Action? OpenRequested, ExitRequested, ResumeMenuRequested, DisplayChanged, ExplorerChanged;
    public event Action<bool>? SessionLockChanged, DisplayOffChanged, SuspendChanged;
    public DesktopShell(string? diagnosticsDirectory=null):this(diagnosticsDirectory,true) { }
    internal DesktopShell(string? diagnosticsDirectory,bool showTrayIcon)
    {
        menuDiagnosticsDirectory=diagnosticsDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWall","diagnostics");
        CreateHandle(new CreateParams {Caption="LumaWall.MenuService",Style=unchecked((int)0x80000000),ExStyle=0x80,Width=1,Height=1});
        using var iconStream=typeof(DesktopShell).Assembly.GetManifestResourceStream("ZZZwallpaper.AppIcon")
            ??throw new InvalidOperationException("Missing ZZZwallpaper application icon.");
        using var embeddedIcon=new Icon(iconStream,32,32);
        icon=(Icon)embeddedIcon.Clone();
        tray=new NotifyIcon {Icon=icon,Text="",Visible=showTrayIcon};
        // Keep NotifyIcon's native context-menu trigger (including keyboard
        // invocation), but draw both entry points in our owned borderless popup.
        var menu=new ContextMenuStrip();
        menu.Items.Add(UiLanguage.Text("打开控制中心"));menu.Items.Add(UiLanguage.Text("继续接管桌面右键"));
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add(UiLanguage.Text("退出软件"));
        menu.Opening+=(_,e)=>
        {
            e.Cancel=true;
            if(trayMenuPending || activeMenu!=0)return;
            // Snapshot the invocation, not the cursor after the queued message runs.
            trayMenuAnchor=Cursor.Position;
            trayMenuPending=true;
            if(!Native.PostMessage(Handle,ShowTrayMenuMessage,0,0))trayMenuPending=false;
        };
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>OpenRequested?.Invoke();
        Native.WTSRegisterSessionNotification(Handle,0);
        var guid=new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");powerNotification=Native.RegisterPowerSettingNotification(Handle,ref guid,0);
        SystemEvents.DisplaySettingsChanged+=OnDisplayChanged;
    }
    private void OnDisplayChanged(object? sender,EventArgs e) => DisplayChanged?.Invoke();
    public void Notify(string message) {tray.BalloonTipTitle="ZZZwallpaper";tray.BalloonTipText=message;tray.ShowBalloonTip(4000);}
    public int ShowMenu(int x,int y,bool paused,bool hasWallpaper,bool canCancel=false,bool canStep=false,DesktopMenuVolume? volume=null)
    {
        return RunThemedMenu(x,y,true,menu=>
        {
            Item(menu,canStep?0u:3u,11,UiLanguage.Text("上一段"));
            Item(menu,hasWallpaper?0u:3u,2,paused?UiLanguage.Text("继续播放"):UiLanguage.Text("暂停壁纸"));
            Item(menu,canStep?0u:3u,12,UiLanguage.Text("下一段"));
            if(volume!=null)Item(menu,0,13,UiLanguage.Text("壁纸音量"));
            Item(menu,0,3,UiLanguage.Text("画面效果…"));
            Item(menu,0,5,UiLanguage.Text("桌面组件…"));
            Item(menu,canCancel?0u:3u,9,UiLanguage.Text("取消此屏幕的视频壁纸"));
            Native.AppendMenu(menu,0x800,0,null);
            Item(menu,0,1,UiLanguage.Text("添加素材…"));
            Item(menu,0,6,UiLanguage.Text("打开控制中心"));Item(menu,0,7,UiLanguage.Text("暂停接管并使用系统菜单"));
            Native.AppendMenu(menu,0x800,0,null);Item(menu,0,8,UiLanguage.Text("退出软件"));
        },volume);
    }
    private void Item(nint target,uint flags,uint id,string label) {Native.AppendMenu(target,flags,id,label);menuLabels[id]=label;}
    private void ShowTrayMenu()
    {
        var position=trayMenuAnchor;
        var state=ReadTrayState?.Invoke()??new(false,false,false,true,true);
        int command=RunThemedMenu(position.X,position.Y,false,menu=>
        {
            Item(menu,0,6,UiLanguage.Text("打开控制中心"));
            Item(menu,0,20,UiLanguage.Text("素材库…"));Item(menu,0,21,UiLanguage.Text("画面效果…"));Item(menu,0,22,UiLanguage.Text("桌面组件…"));
            Item(menu,0,23,UiLanguage.Text("预设…"));Item(menu,0,24,UiLanguage.Text("设置…"));
            Native.AppendMenu(menu,0x800,0,null);
            Item(menu,state.HasWallpaper?0u:3u,25,state.AllPaused?UiLanguage.Text("继续全部壁纸"):UiLanguage.Text("暂停全部壁纸"));
            Item(menu,state.HasWallpaper?0u:3u,26,state.AnySound?UiLanguage.Text("静音全部壁纸"):UiLanguage.Text("开启全部壁纸声音"));
            Item(menu,0,27,state.AnyEffects?UiLanguage.Text("停用全部屏幕效果"):UiLanguage.Text("启用全部屏幕效果"));
            Item(menu,0,28,state.AnyWidgets?UiLanguage.Text("停用全部屏幕组件"):UiLanguage.Text("启用全部屏幕组件"));
            Native.AppendMenu(menu,0x800,0,null);Item(menu,0,10,UiLanguage.Text("继续接管桌面右键"));
            Native.AppendMenu(menu,0x800,0,null);Item(menu,0,8,UiLanguage.Text("退出软件"));
        });
        // Execute after the menu and its GDI resources have been released.
        switch(command)
        {
            case 6:OpenRequested?.Invoke();break;
            case 10:ResumeMenuRequested?.Invoke();break;
            case 8:ExitRequested?.Invoke();break;
            case >=20 and <=28:TrayCommandRequested?.Invoke(command);break;
        }
    }
    private int RunThemedMenu(int x,int y,bool desktop,Action<nint> populate,DesktopMenuVolume? volume=null)
    {
        if(activeMenu!=0)return 0;
        menuLabels.Clear();
        var menu=Native.CreatePopupMenu();if(menu==0)throw new InvalidOperationException("CreatePopupMenu failed");
        try {
            populate(menu);Native.SetWindowPos(Handle,0,x,y,1,1,0x0014);
            var entries=PrepareMenu(menu,Screen.FromPoint(new Point(x,y)).WorkingArea);
            activeMenu=menu;desktopMenuActive=desktop;
            using var popup=new MenuPopup(this,entries,new Point(x,y),volume);menuPopup=popup;
            popup.RecordLifecycle("show-dialog-enter");
            popup.ShowDialog(this);
            popup.RecordLifecycle("show-dialog-return",popup.Command.ToString());
            if(popup.RenderError!=null)throw new InvalidOperationException(UiLanguage.Text("菜单绘制失败：")+popup.RenderError);
            return popup.Command;
        }
        catch(Exception error) when(!desktop && error is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            // Keep Open/Exit reachable even if the themed renderer fails.
            AppLog.Error("TrayMenu","Themed menu failed; using native menu",error);
            Native.SetForegroundWindow(Handle);
            return (int)Native.TrackPopupMenuEx(menu,0x182,x,y,Handle,0);
        }
        finally {MenuBoundsChanged?.Invoke(null);menuPopup=null;activeMenu=0;desktopMenuActive=false;Native.DestroyMenu(menu);EndMenuTheme();menuLabels.Clear();}
    }
    public static HashSet<string> FullscreenScreens(int? widgetProcessId=null)=>DesktopPauseDetector.CoveredScreens(widgetProcessId);
    protected override void WndProc(ref System.Windows.Forms.Message msg)
    {
        if(msg.Msg==ShowTrayMenuMessage)
        {
            trayMenuPending=false;ShowTrayMenu();msg.Result=0;return;
        }
        if(activeMenu!=0)
        {
            if(msg.Msg==CheckMenuClickMessage)
            {
                var point=new Point(unchecked((int)msg.WParam),unchecked((int)msg.LParam));
                if(menuPopup is { } popup&&!popup.ContainsScreenPoint(point)) {OutsideDismissals++;popup.Cancel("outside-hook-click");}
                msg.Result=0;return;
            }
            // ShowDialog disables its owner, which sends WM_CANCELMODE here.
            // This cancels modes belonging to this owner, NOT the new popup.
            if(msg.Msg==0x1F)menuPopup?.RecordLifecycle("owner-cancel-mode-ignored");
        }
        if((uint)msg.Msg==taskbarCreated) ExplorerChanged?.Invoke();
        if(msg.Msg==0x2B1) {if(msg.WParam==7)SessionLockChanged?.Invoke(true);if(msg.WParam==8)SessionLockChanged?.Invoke(false);}
        if(msg.Msg==0x218)
        {
            if(msg.WParam==4)SuspendChanged?.Invoke(true);
            if(msg.WParam==7||msg.WParam==18)SuspendChanged?.Invoke(false);
            if(msg.WParam==0x8013 && msg.LParam!=0)DisplayOffChanged?.Invoke(Marshal.ReadInt32(msg.LParam,20)==0);
        }
        base.WndProc(ref msg);
    }
    public void Dispose()
    {
        menuPopup?.Cancel();if(activeMenu==0)EndMenuTheme();
        SystemEvents.DisplaySettingsChanged-=OnDisplayChanged;
        Native.WTSUnRegisterSessionNotification(Handle);if(powerNotification!=0)Native.UnregisterPowerSettingNotification(powerNotification);
        tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();icon.Dispose();DestroyHandle();
    }
}
