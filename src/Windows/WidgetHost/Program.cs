namespace LumaWall.WidgetHost;

internal static class Program
{
    [STAThread] private static int Main()
    {
        LumaWall.Contracts.UiLanguage.Initialize(Environment.GetEnvironmentVariable("ZZZWALLPAPER_UI_LANGUAGE"), System.Globalization.CultureInfo.CurrentUICulture);
        AppLog.Initialize("WidgetHost",inherit:true);
        string stage="startup";
        WidgetHostChannel? channel=null;
        try {
            // WinExe pipes do not provide a console code-page handle.
            channel=new WidgetHostChannel(Console.OpenStandardInput(),Console.OpenStandardOutput());
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            using var context=new WidgetHostRuntime(channel);
            int exitCode=0;
            ThreadExceptionEventHandler onException=(_,e)=> {exitCode=1;channel.ReportFault("ui",e.Exception);context.Shutdown();};
            Application.ThreadException+=onException;
            System.Windows.Threading.DispatcherUnhandledExceptionEventHandler onWpfException=(_,e)=> {e.Handled=true;exitCode=1;channel.ReportFault("ui",e.Exception);context.Shutdown();};
            var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;dispatcher.UnhandledException+=onWpfException;
            try {stage="ui";Application.Run(context);}
            finally {Application.ThreadException-=onException;dispatcher.UnhandledException-=onWpfException;}
            stage="shutdown";Environment.ExitCode=exitCode;return exitCode;
        }catch(Exception ex) {
            Environment.ExitCode=1;
            if(channel!=null)channel.ReportFault(stage,ex);else AppLog.Error("Widget."+stage,"Widget host startup failed",ex);
            return 1;
        }finally {channel?.Dispose();}
    }
}
