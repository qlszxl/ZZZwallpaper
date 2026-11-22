using System.Runtime.InteropServices;
using System.Security.Principal;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;

namespace LumaWall.ControlCenter;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static ChildProcessGroup ChildProcesses { get; private set; }=null!;
    private Mutex? singleInstance;
    private Mutex? legacyInstance;
    private bool singleInstanceOwned,legacyInstanceOwned;
    private EventWaitHandle? activation;
    private EventWaitHandle? legacyActivation;
    private RegisteredWaitHandle? activationWait;
    private RegisteredWaitHandle? legacyActivationWait;
    private MainWindow? window;
    private LibraryStore? store;
    private DesktopHitTestService? hit;
    private DesktopMenuController? menu;
    private DesktopShell? shell;
    private WallpaperController? wallpapers;
    private WidgetController? widgets;
    private DesktopRuntimePump? runtimePump;
    private bool quitting;
    private bool startupComplete;
    private LumaWall.Lifecycle.InstallationLifetime? installationLifetime;
    public App() {UnhandledException+=OnUnhandled;}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string identity=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
        singleInstance=new Mutex(true,@"Local\LumaWall.Control."+identity,out bool first);
        singleInstanceOwned=first;
        activation=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\LumaWall.Activate."+identity);
        if(!first) {AppLog.Info("Lifecycle","Existing instance activated");if(!Environment.GetCommandLineArgs().Contains("--startup"))activation.Set();Exit();return;}
        try
        {
            // Keep the former identity only as a compatibility gate. Old builds
            // cannot edit the source while it is copied or while this app runs.
            legacyInstance=new Mutex(true,@"Local\LumaWall.Modular.Control."+identity,out bool legacyFirst);
            legacyInstanceOwned=legacyFirst;
            if(!legacyFirst)throw new IOException(UiLanguage.Text("旧模块化版本仍在运行，请先从托盘正常退出它，再启动 ZZZwallpaper。个人数据尚未迁移。"));
            legacyActivation=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\LumaWall.Modular.Activate."+identity);
            installationLifetime=new LumaWall.Lifecycle.InstallationLifetime(AppContext.BaseDirectory);
            if(installationLifetime.IsStopping) {Quit();return;}
            ChildProcesses=new ChildProcessGroup();
            string[] arguments=Environment.GetCommandLineArgs();int dataIndex=Array.IndexOf(arguments,"--data-root");
            string root=dataIndex>=0&&dataIndex+1<arguments.Length?Path.GetFullPath(arguments[dataIndex+1]):
                UnifiedDataMigration.PrepareDefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AppLog.Initialize("ControlCenter",root);
            store=new LibraryStore(root);
            UiLanguage.Initialize(store.GetSetting(UiLanguage.SettingKey), System.Globalization.CultureInfo.CurrentUICulture);
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = UiLanguage.Current;
            InitializeComponent();
            var modules=new ControlCenterServices(store);
            window=new MainWindow(store,modules);
            if(quitting) {window.Close();return;}
            var queue=window.DispatcherQueue;
            if(installationLifetime.IsStopping) {Quit();return;}
            installationLifetime.Listen(()=>queue.TryEnqueue(Quit));
            void Dispatch(Action action) {if(!quitting)queue.TryEnqueue(()=> {if(!quitting)action();});}
            wallpapers=new WallpaperController(store,Dispatch,modules);
            widgets=new WidgetController(store,Dispatch,modules);window.ConnectWidgets(widgets);wallpapers.WidgetProcessId=()=>widgets?.ProcessId;
            widgets.Error+=reason=>window.ShowError(reason);
            widgets.ManageRequested+=async id=> {try {Show();window.Navigate("widgets");if(id!=null)await window.EditWidgetCard(id);}catch(Exception ex) {window.ShowError(UiLanguage.Text("组件编辑未打开：")+ex.Message,ex);}};
            shell=new DesktopShell(Path.Combine(root,"diagnostics"));hit=new DesktopHitTestService();
            void ApplyAppearance(AppearanceOptions value) {shell.ApplyAppearance(value);widgets.SetAppearance(value);}
            ApplyAppearance(window.Appearance);window.AppearanceChanged+=ApplyAppearance;
            menu=new DesktopMenuController(hit,Dispatch,ShowDesktopMenu,shell.Handle,store.GetSetting("menuSafeDisabled")=="true",MainWindow.EnabledMenuScreens(store));
            shell.MenuShown+=menu.ConfirmMenuShown;
            shell.MenuWindowChanged+=hit.TrackMenuWindow;
            shell.MenuBoundsChanged+=bounds=> {menu.SetMenuBounds(bounds is { } area?new DesktopRect(area.Left,area.Top,area.Right,area.Bottom):null);menu.SetMenuOpen(bounds!=null);};
            shell.VolumePreviewRequested+=PreviewDesktopVolume;
            shell.VolumeCommitted+=value=>CommitDesktopVolume(value,modules.Playback);
            window.Connect(wallpapers,menu,Quit);
            menu.Faulted+=reason=> {store.SetSetting("menuSafeDisabled","true");window.ShowError(reason);shell.Notify(reason);};
            wallpapers.Error+=reason=> {window.ShowError(reason);shell.Notify(reason);};
            shell.OpenRequested+=ShowHome;
            shell.ReadTrayState=window.TrayState;shell.TrayCommandRequested+=window.RunTrayCommand;
            shell.ExitRequested+=Quit;
            shell.ResumeMenuRequested+=()=>window.ResumeDesktopMenu();
            shell.DisplayChanged+=()=>Dispatch(async()=> {AppLog.Info("Desktop","Display configuration changed");hit.Invalidate();window.EnsureWindowPlacementVisible();window.RefreshScreens();widgets.Refresh(true);await wallpapers.Rebind();});
            shell.ExplorerChanged+=()=>Dispatch(async()=> {AppLog.Warning("Desktop","Explorer restarted; rebinding wallpaper and widgets");hit.Invalidate();widgets.Restart();await wallpapers.Rebind();});
            shell.SessionLockChanged+=value=> {hit.Invalidate();wallpapers.SetEnvironmentPause(PauseReason.SessionLocked,value);};
            shell.DisplayOffChanged+=value=>wallpapers.SetEnvironmentPause(PauseReason.DisplayOff,value);
            shell.SuspendChanged+=value=> {hit.Invalidate();wallpapers.SetEnvironmentPause(PauseReason.Suspend,value);};
            runtimePump=new DesktopRuntimePump(queue,store,wallpapers,menu,hit,shell,window.RefreshStatus,arguments.Contains("--diagnostics"));
            activationWait=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>Dispatch(ShowHome),null,-1,false);
            legacyActivationWait=ThreadPool.RegisterWaitForSingleObject(legacyActivation,(_,_)=>Dispatch(ShowHome),null,-1,false);
            window.AppWindow.Closing+=(_,e)=> {if(!quitting) {e.Cancel=true;window.HideToTray();}};
            window.LoadedReady+=async()=> {if(quitting)return;startupComplete=true;AppLog.Info("Lifecycle","Main window ready");if(arguments.Contains("--startup"))window.HideToTray();widgets.Refresh();await wallpapers.Restore();window.StartAutomation();await window.ShowInitialOnboarding();if(store.GetSetting("menuSafeDisabled")=="true")window.ShowError(UiLanguage.Text("上次接管故障已安全停用。请在设置中打开对应显示器的右键开关重试。"));};
            window.Activate();
        }
        catch(Exception ex)
        {
            FailStartup(ex);
        }
    }
    private void Show() {window?.ShowControlCenter();}
    private void ShowHome() {Show();window?.OpenHome();}
    private void ShowDesktopMenu(MenuRequest request)
    {
        if(menu==null||shell==null||wallpapers==null||window==null||store==null)return;
        menu.SetMenuOpen(true);
        wallpapers.Running.TryGetValue(request.ScreenId,out var current);
        var volume=new DesktopMenuVolume(request.ScreenId,store.WallpaperVolume(request.ScreenId),wallpapers.VideoSoundEnabled(request.ScreenId));
        int command;
        store.BreakUndoGesture();
        try {
            command=shell.ShowMenu(request.X,request.Y,current?.Pause.Reasons.HasFlag(PauseReason.User)??false,current!=null,wallpapers.CanCancel(request.ScreenId),wallpapers.CanStepPlayback(request.ScreenId),volume);
        } finally {menu.SetMenuOpen(false);store.BreakUndoGesture();}
        if(command==0)return;
        window.SelectScreen(request.ScreenId);
        switch(command)
        {
            case 1: Show();window.Navigate("library");_ = window.Import();break;
            case 2: wallpapers.ToggleUserPause(request.ScreenId);break;
            case 11: _=StepDesktopPlayback(request.ScreenId,-1);break;
            case 12: _=StepDesktopPlayback(request.ScreenId,1);break;
            case 3: Show();window.OpenEffectsForScreen(request.ScreenId);break;
            case 5: Show();window.Navigate("widgets");break;
            case 6: ShowHome();break;
            case 7: menu.SetEnabled(false);shell.Notify(UiLanguage.Text("桌面接管已暂停，请再次右键使用系统菜单。托盘可恢复接管。"));break;
            case 8: Quit();break;
            case 9: try {wallpapers.CancelWallpaper(request.ScreenId);}catch(Exception ex) {window.ShowError(UiLanguage.Text("取消壁纸失败：")+ex.Message,ex);}break;
        }
    }
    private async Task StepDesktopPlayback(string screen,int direction)
    {
        try {if(wallpapers?.CanStepPlayback(screen)==true)await wallpapers.StepPlayback(screen,direction);}
        catch(Exception error) {window?.ShowError(UiLanguage.Text("切换素材失败，当前壁纸已保留。")+error.Message,error);}
    }
    private void OnUnhandled(object sender,Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // UI faults must not change the independent desktop menu policy. Actual menu
        // failures still go through DesktopMenuController.Fail and its watchdog.
        window?.CaptureUiFault(e.Exception,"xaml_runtime",e.Message);
        e.Handled=true;
        // A native render failure can reach this event without a managed stack.
        // Keep the framework message and editor context before reporting it in UI.
        AppLog.Error("XamlRuntime",$"HRESULT=0x{e.Exception.HResult:X8}; message={e.Message}; {window?.DescribeUiFaultContext()??"window=not-created"}",e.Exception);
        Program.WriteRuntimeError(e.Exception);
        if(!startupComplete) {FailStartup(e.Exception);return;}
        menu?.SetMenuOpen(false);
        if(window!=null)window.ShowError(UiLanguage.Text("操作未完成：")+e.Exception.Message,e.Exception);
    }
    private void FailStartup(Exception ex)
    {
        Program.ReportStartupFailure(ex);
        try {Quit();}
        catch(Exception cleanupError) {Program.WriteRuntimeError(cleanupError);Exit();}
    }
    private void Quit()
    {
        if(quitting)return;quitting=true;
        AppLog.Info("Lifecycle","Shutdown requested");
        void Clean(Action action) {try {action();}catch(Exception error) {Program.WriteRuntimeError(error);}}
        Clean(()=>runtimePump?.Dispose());Clean(()=>window?.StopAutomation());Clean(()=>window?.ShutdownWindowPlacement());
        Clean(()=>window?.ShutdownImports());Clean(()=>activationWait?.Unregister(null));Clean(()=>legacyActivationWait?.Unregister(null));
        Clean(()=>menu?.Dispose());Clean(()=>hit?.Dispose());Clean(()=>window?.ShutdownPerformanceMonitor());
        Clean(()=>window?.EndEffectEditing());Clean(()=>wallpapers?.Dispose());Clean(()=>widgets?.Dispose());
        Clean(()=>shell?.Dispose());Clean(()=>store?.Dispose());
        // Always close the child job, even if another component failed to dispose.
        Clean(()=>ChildProcesses?.Dispose());Clean(()=>activation?.Dispose());Clean(()=>legacyActivation?.Dispose());
        Clean(()=>installationLifetime?.Dispose());
        Clean(()=> {if(legacyInstanceOwned)legacyInstance?.ReleaseMutex();});Clean(()=>legacyInstance?.Dispose());
        Clean(()=> {if(singleInstanceOwned)singleInstance?.ReleaseMutex();});Clean(()=>singleInstance?.Dispose());
        Clean(()=>window?.Close());Exit();
    }
}
