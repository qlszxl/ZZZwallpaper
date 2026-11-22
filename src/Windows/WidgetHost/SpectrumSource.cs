using System.Diagnostics;
using System.Text.Json;

namespace LumaWall.WidgetHost;

internal sealed record SpectrumSample(bool Available,double[] Bands,long CapturedAt)
{
    internal static SpectrumSample Empty=>new(false,new double[256],Environment.TickCount64);
}
internal sealed class SpectrumSource : IDisposable
{
    private readonly Process process;
    private volatile bool disposed;
    private SpectrumSample latest=SpectrumSample.Empty;
    internal int ProcessId=>process.Id;
    internal SpectrumSample Snapshot {get {var value=Volatile.Read(ref latest);return Environment.TickCount64-value.CapturedAt>1500?SpectrumSample.Empty:value;}}
    internal bool Exited {get {try {return disposed||process.HasExited;}catch(InvalidOperationException) {return true;}}}
    internal SpectrumSource()
    {
        string file=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","AudioSpectrumHost.exe"));
        process=new Process {StartInfo=new(file) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        AppLog.ConfigureChild(process.StartInfo);
        try {process.Start();}catch {process.Dispose();throw;}
        AppLog.Info("AudioSpectrumHost",$"Started; pid={process.Id}");
        _=AppLog.CaptureStandardErrorAsync(process,"AudioSpectrumHost");
        _=Task.Run(async()=> {
            bool? available=null;
            try {
                while(!disposed&&await process.StandardOutput.ReadLineAsync() is { } line) {
                    if(line.Length>16384)break;
                    // Older helpers emit 64 bands; current releases emit all 256.
                    using var json=JsonDocument.Parse(line);var values=json.RootElement.GetProperty("bands");if(values.GetArrayLength() is not (64 or 256))continue;
                    var bands=values.EnumerateArray().Select(v=>v.GetDouble()).Select(v=>double.IsFinite(v)?Math.Clamp(v,0,8):0).ToArray();
                    bool current=json.RootElement.GetProperty("available").GetBoolean();
                    if(current!=available) {if(current)AppLog.Info("AudioSpectrumHost","Audio capture available");else AppLog.Warning("AudioSpectrumHost","Audio capture unavailable; waiting for device recovery");available=current;}
                    Volatile.Write(ref latest,new(current,bands,Environment.TickCount64));
                }
            }catch(Exception ex) when(ex is IOException or InvalidOperationException or JsonException or ObjectDisposedException) {if(!disposed)AppLog.Warning("AudioSpectrumHost","Spectrum stream failed",ex);}
            finally {if(!disposed)AppLog.Warning("AudioSpectrumHost","Spectrum stream ended unexpectedly");Volatile.Write(ref latest,SpectrumSample.Empty);}
        });
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try {process.StandardInput.WriteLine("stop");process.StandardInput.Close();}catch(Exception ex) when(ex is IOException or InvalidOperationException) { }
        _=Task.Run(()=> {try {if(!process.WaitForExit(700))process.Kill(true);}catch(InvalidOperationException) { }finally {process.Dispose();}});
    }
}
