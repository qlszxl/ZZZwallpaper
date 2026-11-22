using LumaWall.Contracts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LumaWall.ControlCenter;
public static class Program
{
    private static int startupFailureReported;
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            int restartIndex=Array.IndexOf(args,"--automation-restart-parent");
            if(restartIndex>=0) {
                if(restartIndex+1>=args.Length||!int.TryParse(args[restartIndex+1],out int parent)||parent<=0)return;
                try {using var previous=System.Diagnostics.Process.GetProcessById(parent);if(!previous.WaitForExit(30000))return;}catch(ArgumentException) { }
            }
            using var lifetime = new LumaWall.Lifecycle.InstallationLifetime(AppContext.BaseDirectory);
            if (lifetime.IsStopping) return;
            // The app opens its log after any default-data migration so an
            // early log handle cannot prevent the verified directory swap.
            try {LumaWall.DesktopIntegration.TaskbarIdentity.InitializeProcess();}
            catch(Exception ex) {WriteRuntimeError(ex);}
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(parameters=>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        }
        catch(Exception ex) {ReportStartupFailure(ex);}
    }
    internal static void ReportStartupFailure(Exception ex)
    {
        // A XAML startup fault cannot reliably be displayed by another XAML control.
        if(Interlocked.Exchange(ref startupFailureReported,1)!=0)return;
        if(AppLog.DirectoryPath==null) {
            string[] arguments=Environment.GetCommandLineArgs();int dataIndex=Array.IndexOf(arguments,"--data-root");
            bool explicitRoot=dataIndex>=0&&dataIndex+1<arguments.Length;
            // An interrupted directory swap may intentionally leave LumaWall
            // absent. Error reporting must not create it ahead of recovery.
            AppLog.Initialize("ControlCenter",explicitRoot?arguments[dataIndex+1]:Path.Combine(Path.GetTempPath(),"ZZZwallpaper"),
                fallbackToDefaultDirectory:explicitRoot);
        }
        Environment.ExitCode=1;
        string? log=WriteStartupError(ex);
        System.Windows.Forms.MessageBox.Show(UiLanguage.Text("启动失败：")+ex.Message+(log==null?"":UiLanguage.Text("\n\n错误详情已保存：")+log),"ZZZwallpaper",
            System.Windows.Forms.MessageBoxButtons.OK,System.Windows.Forms.MessageBoxIcon.Error);
    }
    internal static string? WriteStartupError(Exception ex)=>WriteError(ex,"Startup");
    internal static string? WriteRuntimeError(Exception ex)=>WriteError(ex,"Runtime");
    private static string? WriteError(Exception ex,string category)
    {
        AppLog.Error(category,"Application exception",ex);
        return AppLog.LastError==null?AppLog.CurrentFile:null;
    }
}
