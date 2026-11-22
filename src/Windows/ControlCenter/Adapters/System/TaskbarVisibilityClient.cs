using System.Diagnostics;
using LumaWall.Contracts;
using System.Threading.Channels;

namespace LumaWall.ControlCenter;

// The helper deliberately stays outside the player job. EOF or parent death lets
// it restore only taskbars it hid, including after an unexpected app exit.
internal sealed class TaskbarVisibilityClient : IDisposable
{
    private Process? process;
    public int? ProcessId {get {try {return process is {HasExited:false}?process.Id:null;}catch {return null;}}}
    private string last="";
    private Channel<string>? outgoing;
    public void Update(IEnumerable<(string Screen,TaskbarStyle Style,TaskbarVisualOptions Visual)> displays)
    {
        string next=string.Join("|",displays.OrderBy(value=>value.Screen,StringComparer.Ordinal).Select(value=>$"{value.Screen}={value.Visual.ToCommand(value.Style)}"));
        try {
            if(process?.HasExited==true) {AppLog.Warning("TaskbarHost",$"Unexpected exit; code=0x{process.ExitCode:X8}");outgoing?.Writer.TryComplete();outgoing=null;process.Dispose();process=null;last="";}
            // Keep one helper alive across rapid toggles: an exiting old helper
            // must not show a taskbar after a new helper has hidden it.
            if(next.Length==0&&process==null)return;
            if(process==null) {
                var start=new ProcessStartInfo(Path.Combine(LumaWall.Contracts.FeaturePacks.BaseDirectory,"TaskbarHost.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,ArgumentList={Environment.ProcessId.ToString()}};
                start.Environment["ZZZWALLPAPER_INSTALL_DIRECTORY"]=AppContext.BaseDirectory;
                process=Process.Start(start);
                if(process!=null) {
                    _=AppLog.CaptureStandardErrorAsync(process,"TaskbarHost");
                    outgoing=Channel.CreateBounded<string>(new BoundedChannelOptions(1) {SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest});
                    var active=process;var pending=outgoing;
                    _=Task.Run(async()=> {
                        try {await foreach(var message in pending.Reader.ReadAllAsync()) {await active.StandardInput.WriteLineAsync(message);await active.StandardInput.FlushAsync();}}
                        catch(Exception error) {AppLog.Warning("TaskbarHost","Taskbar update channel closed",error);}
                    });
                }
                last="";
            }
            if(process!=null&&last!=next&&outgoing?.Writer.TryWrite(next)==true)last=next;
        } catch(Exception ex) {AppLog.Warning("TaskbarHost","Taskbar visibility update failed",ex);Dispose();}
    }
    public void Dispose()
    {
        if(process==null)return;
        outgoing?.Writer.TryComplete();outgoing=null;
        try {process.StandardInput.Close();}catch { }
        process.Dispose();process=null;last="";
    }
}
