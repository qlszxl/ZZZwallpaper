using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LumaWall.Contracts;
namespace LumaWall.ControlCenter;

// One persistent renderer per expanded explanation. Commands are serialized by the UI caller.
internal sealed class EffectImagePreviewClient : IDisposable
{
    internal const int FrameWidth=960,FrameHeight=540;
    private readonly Process process;
    private readonly string output;
    private readonly CancellationTokenSource lifetime=new();
    private bool ready,disposed;
    private Task activeRender=Task.CompletedTask;
    public string Source {get;}
    public int? ProcessId {get {try {return !disposed&&!process.HasExited?process.Id:null;}catch(InvalidOperationException) {return null;}}}
    public EffectImagePreviewClient(string source,string root)
    {
        Source=source;string directory=Path.Combine(root,"effect-preview","cache");Directory.CreateDirectory(directory);OwnedFileRetention.Prune(directory,n=>OwnedFileRetention.HexName(n,".bmp"),8,32L*1024*1024,TimeSpan.FromDays(1),recentGrace:TimeSpan.FromMinutes(2));output=Path.Combine(directory,Guid.NewGuid().ToString("N")+".bmp");
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"WallpaperHost.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["ZZZWALLPAPER_BASE_DIRECTORY"]=LumaWall.Contracts.FeaturePacks.BaseDirectory;
        start.Environment["ZZZWALLPAPER_FEATURE_PACK_ROOT"]=LumaWall.Contracts.FeaturePacks.Root;
        foreach(var arg in new[]{"--file",source,"--image-preview",output,"--rect","0","0",FrameWidth.ToString(CultureInfo.InvariantCulture),FrameHeight.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
        process=new Process {StartInfo=start};
        AppLog.ConfigureChild(start);
        try {if(!process.Start())throw new IOException(UiLanguage.Text("无法启动图片预览"));AppLog.Info("ImagePreview",$"Started; pid={process.Id}");_ = AppLog.CaptureStandardErrorAsync(process,"ImagePreview",lifetime.Token);}catch {process.Dispose();lifetime.Dispose();throw;}
    }
    private async Task WaitEvent(string name)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while(true) {
            string? line=await process.StandardOutput.ReadLineAsync(timeout.Token);if(line==null)throw new IOException(UiLanguage.Text("图片预览进程已退出"));
            using var json=JsonDocument.Parse(line);var root=json.RootElement;
            if(root.TryGetProperty("event",out var type)&&type.GetString()==name)return;
            if(root.TryGetProperty("error",out var error))throw new IOException(error.ToString());
        }
    }
    public Task<byte[]> Render(string preparedEffects,bool original,double seconds,bool audio)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!activeRender.IsCompleted)throw new InvalidOperationException(UiLanguage.Text("图片预览请求尚未完成"));
        var pending=RenderCore(preparedEffects,original,seconds,audio);activeRender=pending;return pending;
    }
    private async Task<byte[]> RenderCore(string preparedEffects,bool original,double seconds,bool audio)
    {
        if(!ready) {await WaitEvent("image-ready");ready=true;}
        // Repeatable quiet -> loud demo, clearly labelled in the UI. Never opens an audio device.
        double phase=seconds%6,level=audio?(phase<2?.02:.65+.3*Math.Sin(phase*5)):0;
        double pulse=audio&&phase>=2?Math.Exp(-((phase-2)%.6)/.13):0,impact=audio&&phase is >=2 and <2.9?Math.Max(0,1-(phase-2)/.9):0;
        string command=string.Create(CultureInfo.InvariantCulture,$"frame {seconds:F4} {(original?1:0)} {level:F4} {pulse:F4} {impact:F4} {preparedEffects}");
        await process.StandardInput.WriteLineAsync(command.AsMemory(),lifetime.Token);await process.StandardInput.FlushAsync(lifetime.Token);await WaitEvent("image-frame");
        return await File.ReadAllBytesAsync(output,lifetime.Token);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();
        try {if(!process.HasExited)process.Kill();}catch(InvalidOperationException) {}catch(System.ComponentModel.Win32Exception) {}
        // Close inherited file handles before removing only this client's output.
        _=Cleanup();
    }
    private async Task Cleanup()
    {
        try {await ChildProcessShutdown.StopAsync(process,activeRender,lifetime,graceMilliseconds:0).ConfigureAwait(false);}
        finally {try {File.Delete(output);}catch(IOException) {}catch(UnauthorizedAccessException) {}}
    }
}
