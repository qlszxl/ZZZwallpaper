using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private WindowIcon? shellIcon;
    private TaskbarIdentity? shellIdentity;
    private bool shellIconErrorReported;

    private void InitializeShellIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "App.ico");
        try {shellIdentity=new TaskbarIdentity(WinRT.Interop.WindowNative.GetWindowHandle(this),Path.Combine(AppContext.BaseDirectory,"ZZZwallpaper.Launcher.exe"));}
        catch(Exception error) {Program.WriteRuntimeError(error);}
        AppWindow.SetIcon(path);
        shellIcon = new WindowIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), path);
        ApplyShellIcon();
        Activated += ShellIcon_Activated;
        Root.Loaded += (_, _) => ApplyShellIcon();
        Closed += (_, _) => { Activated -= ShellIcon_Activated; shellIdentity?.Dispose();shellIdentity=null;shellIcon?.Dispose(); shellIcon = null; };
    }

    private void ShellIcon_Activated(object sender, WindowActivatedEventArgs args)
    {
        if(args.WindowActivationState != WindowActivationState.Deactivated) ApplyShellIcon();
    }

    private void ApplyShellIcon()
    {
        try { shellIcon?.Apply(); }
        catch(Exception error)
        {
            if(!shellIconErrorReported) { shellIconErrorReported = true; Program.WriteRuntimeError(error); }
        }
    }
}
