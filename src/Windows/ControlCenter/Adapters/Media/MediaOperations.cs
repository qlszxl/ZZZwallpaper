using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed record PosterFrameResult(bool Ok,double Seconds,double Duration,double FrameDuration,double SourceFps,int Width,int Height,double Sharpness);
public sealed record DecodeMeasurement
{
    public bool Ok {get;init;}
    public string Decoder {get;init;}="";
    public string Requested {get;init;}="";
    public string? Error {get;init;}
    public int FpsLimit {get;init;}
    public double SourceFps {get;init;}
    public long DecodedFrames {get;init;}
    public long SelectedFrames {get;init;}
    public double ElapsedSeconds {get;init;}
    public double MediaSeconds {get;init;}
    public double Speed {get;init;}
    public double SelectedFps {get;init;}
    public double MeanMs {get;init;}
    public double P95Ms {get;init;}
    public double CpuPercent {get;init;}
    public double? GpuPercent {get;init;}
    public double? PeakGpuPercent {get;init;}
    public double? WorkingSetBytes {get;init;}
    public double? PeakWorkingSetBytes {get;init;}
    public double? PrivateBytes {get;init;}
    public double? PeakPrivateBytes {get;init;}
    public double? DedicatedGpuBytes {get;init;}
    public double? PeakDedicatedGpuBytes {get;init;}
    public double? SharedGpuBytes {get;init;}
    public double? PeakSharedGpuBytes {get;init;}
    public int GpuSamples {get;init;}
    public int MemorySamples {get;init;}
    public string ResourceScope {get;init;}="";
}
public sealed record DecodeReport(string AssetId,int FpsLimit,string SourceStamp,DateTimeOffset SampledAt,IReadOnlyList<DecodeMeasurement> Results,string Recommendation);

public static class MediaOperations
{
    private static readonly ConcurrentDictionary<int,PerformanceProcessTarget> workers=new();
    public static IReadOnlyList<PerformanceProcessTarget> ActiveWorkers=>workers.Values.ToArray();
    internal static IDisposable TrackWorker(int pid,string name,string detail)
    {
        workers[pid]=new(pid,"media:"+pid,name,detail);return new WorkerRegistration(pid);
    }
    private sealed class WorkerRegistration(int pid):IDisposable {public void Dispose()=>workers.TryRemove(pid,out _);}
    private static readonly JsonSerializerOptions JsonOptions=new() {PropertyNameCaseInsensitive=true};
    private static readonly SemaphoreSlim benchmarkGate=new(1,1);
    public static async Task<T> Run<T>(IEnumerable<string> arguments,CancellationToken token,int seconds=90)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var parameters=arguments.ToArray();
        using var process=new Process {StartInfo=NativePlayback.StartInfo(parameters)};
        process.Start();App.ChildProcesses.Add(process);
        using var tracked=TrackWorker(process.Id,parameters.Contains("--benchmark")?UiLanguage.Text("解码性能测试"):UiLanguage.Text("封面选帧"),UiLanguage.Text("独立媒体处理进程"));
        using var cancel=timeout.Token.Register(()=> {try {if(!process.HasExited)process.Kill(true);}catch { }});
        var errors=AppLog.CaptureStandardErrorAsync(process,"MediaOperation",timeout.Token);
        string output=await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);await errors;token.ThrowIfCancellationRequested();
        using var json=JsonDocument.Parse(output);
        if(process.ExitCode!=0||json.RootElement.TryGetProperty("ok",out var ok)&&!ok.GetBoolean())
            throw new IOException(json.RootElement.TryGetProperty("error",out var error)?error.GetString():UiLanguage.Text("媒体操作失败"));
        return JsonSerializer.Deserialize<T>(output,JsonOptions)??throw new IOException(UiLanguage.Text("媒体操作没有返回结果"));
    }
    public static Task<PosterFrameResult> Frame(string path,string destination,double time,int direction,CancellationToken token)
    {
        if(!double.IsFinite(time)||time<0||time>1e7||direction is < -1 or > 1)throw new ArgumentOutOfRangeException(nameof(time));
        return Run<PosterFrameResult>(["--file",path,"--software","--poster",destination,"--poster-time",time.ToString("R",CultureInfo.InvariantCulture),"--poster-direction",direction.ToString(CultureInfo.InvariantCulture)],token,40);
    }
    public static async Task<DecodeReport> Benchmark(MediaAsset asset,int cap,CancellationToken token,Action<string>? progress=null)
    {
        if(asset.IsImage)throw new ArgumentException(UiLanguage.Text("请选择视频素材"));
        await benchmarkGate.WaitAsync(token);
        try {
            string Stamp() {var f=new FileInfo(asset.Path);return $"{f.FullName}:{f.Length}:{f.LastWriteTimeUtc.Ticks}";}
            string sourceStamp=Stamp();
            var measurements=new List<DecodeMeasurement>();
            foreach(int rate in (cap>0?new[]{0,cap}:new[]{0}))foreach(bool software in new[]{false,true}) {
                token.ThrowIfCancellationRequested();
                string requested=software?"Software":"Hardware";
                progress?.Invoke($"{(software?UiLanguage.Text("软件解码"):UiLanguage.Text("硬件解码"))} · {(rate==0?UiLanguage.Text("原始帧率"):rate+" FPS")}…");
                try {
                    var args=new List<string>{"--file",asset.Path,"--benchmark","3","--fps-limit",rate.ToString(CultureInfo.InvariantCulture)};
                    if(software)args.Add("--software");else {args.Add("--decoder-mode");args.Add("1");}
                    measurements.Add((await Run<DecodeMeasurement>(args,token,40)) with {Requested=requested});
                }catch(OperationCanceledException) when(!token.IsCancellationRequested) {measurements.Add(new() {Requested=requested,FpsLimit=rate,Error=UiLanguage.Text("此解码方式处理超时")});}
                catch(OperationCanceledException) {throw;}
                catch(Exception ex) {measurements.Add(new() {Requested=requested,FpsLimit=rate,Error=ex.Message});}
            }
            var target=measurements.Where(m=>m.FpsLimit==cap&&m.Ok).ToArray();
            var hw=target.FirstOrDefault(m=>m.Requested=="Hardware"&&m.Decoder=="D3D11VA");
            var sw=target.FirstOrDefault(m=>m.Requested=="Software");
            string recommendation=hw is {Speed:>=1.2}?"Hardware":sw is {Speed:>=1.2}?"Software":
                hw!=null&&(sw==null||hw.Speed>=sw.Speed)?"Hardware":sw!=null?"Software":"Unavailable";
            if(Stamp()!=sourceStamp)throw new IOException(UiLanguage.Text("测试期间素材已发生变化，请重新测试"));
            return new(asset.Id,cap,sourceStamp,DateTimeOffset.Now,measurements,recommendation);
        }finally {benchmarkGate.Release();}
    }
}

// A private, silent worker for one picker. Closing/cancelling never touches desktop playback.
public sealed class PosterPreviewSession : IDisposable
{
    private readonly Process process;
    private readonly IDisposable tracked;
    private readonly string output;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private bool disposed;
    public string Output=>output;
    public PosterPreviewSession(string path,string output)
    {
        this.output=output;
        process=new() {StartInfo=NativePlayback.StartInfo("--file",path,"--software","--poster-session","--poster",output)};
        process.Start();App.ChildProcesses.Add(process);_=AppLog.CaptureStandardErrorAsync(process,"PosterPreview",lifetime.Token);
        tracked=MediaOperations.TrackWorker(process.Id,UiLanguage.Text("封面预览"),Path.GetFileName(path));
    }
    private bool ready;
    public async Task<PosterFrameResult> Frame(double seconds,int direction,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(40));
        await gate.WaitAsync(timeout.Token);
        try {
            if(!ready) {
                var line=await process.StandardOutput.ReadLineAsync(timeout.Token)??throw new IOException(UiLanguage.Text("封面预览已退出"));
                using var first=JsonDocument.Parse(line);
                if(!first.RootElement.TryGetProperty("event",out var e)||e.GetString()!="poster-ready")throw new IOException(UiLanguage.Text("无法准备封面预览"));
                ready=true;
            }
            await process.StandardInput.WriteLineAsync($"frame {seconds.ToString("R",CultureInfo.InvariantCulture)} {direction}".AsMemory(),timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var reply=await process.StandardOutput.ReadLineAsync(timeout.Token)??throw new IOException(UiLanguage.Text("封面预览已退出"));
            using var json=JsonDocument.Parse(reply);
            if(!json.RootElement.GetProperty("ok").GetBoolean())throw new IOException(json.RootElement.GetProperty("error").GetString());
            return JsonSerializer.Deserialize<PosterFrameResult>(reply,new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
        }catch(OperationCanceledException) {Dispose();throw;}
        finally {gate.Release();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();
        try {if(!process.HasExited)process.Kill(true);}catch { }
        _=Task.Run(async()=> {try {await process.WaitForExitAsync();}catch { }finally {tracked.Dispose();process.Dispose();try {File.Delete(output);}catch { }}});
    }
}
