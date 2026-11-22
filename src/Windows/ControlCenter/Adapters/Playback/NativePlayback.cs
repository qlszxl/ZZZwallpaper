using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed class NativePlayback : IDisposable
{
    private readonly Process process;
    private readonly object traceGate=new();
    private readonly SortedDictionary<long,OperationContext> configurationTraces=new();
    private OperationContext? lastRendererOperation=OperationTrace.Current;
    private readonly OperationContext? launchOperation=OperationTrace.Current;
    private void TraceRenderer(string stage,string status,long? revision=null,string detail="")
    {
        OperationContext? operation;bool exact=false;int superseded=0;
        lock(traceGate) {
            operation=lastRendererOperation;
            if(revision is { } value&&configurationTraces.TryGetValue(value,out var match)) {operation=match;exact=true;}
            if(revision is { } acknowledged&&status=="acknowledged") {
                var previous=configurationTraces.Keys.Where(k=>k<=acknowledged).ToArray();superseded=Math.Max(0,previous.Length-1);
                foreach(long key in previous)configurationTraces.Remove(key);
            }
        }
        string info=$"pid={ProcessId}; revision={revision}; association={(exact?"configuration_revision":"latest_configuration_candidate")}; superseded={superseded}; {detail}";
        if(status=="failed")OperationTrace.Fault(operation,stage,info);
        else OperationTrace.Record(operation,stage,status,info,sampledEvent:true);
    }
    private readonly TaskCompletionSource prepared=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<Action> dispatch;
    private readonly PlaybackCommandWriter commands;
    private readonly CancellationTokenSource sendingCancellation=new();
    private readonly LumaWall.Automation.ConfigurationAcknowledgement configurationAcknowledgement=new();
    private volatile bool disposed;
    private int commandFailureReported;
    private bool performanceMonitoring;
    private long performanceGeneration;
    private static readonly ConcurrentDictionary<int,string> probes=new();
    private static readonly SemaphoreSlim probeGate=new(1,1);
    public static IReadOnlyList<(int ProcessId,string Name)> ActiveProbes => probes.Select(pair=>(pair.Key,pair.Value)).ToArray();
    public int ProcessId {get;}
    public string SourceName {get;}
    public PerformanceFrame? Performance {get;private set;}
    public JsonElement? RenderTelemetry {get;private set;}
    public DateTimeOffset? RenderTelemetryAt {get;private set;}
    public DateTimeOffset? AudioTelemetryAt {get;private set;}
    public JsonElement? AudioTelemetry {get;private set;}
    public long RequestedRevision=>configurationAcknowledgement.RequestedRevision;
    public long AppliedRevision=>configurationAcknowledgement.ConfirmedRevision;
    private bool automationMonitoring;
    public string Decoder { get; private set; } = UiLanguage.Text("正在准备");
    public string ActualState { get; private set; } = UiLanguage.Text("正在准备");
    public string AudioStatus {get;private set;}=UiLanguage.Text("音频联动未开启");
    public string AudioStatusCode {get;private set;}="disabled";
    public string SoundStatus {get;private set;}=UiLanguage.Text("视频声音准备中");
    public IReadOnlyDictionary<int,(double Value,double Signal)> AudioRuleValues {get;private set;}=new Dictionary<int,(double,double)>();
    public double AudioInput {get;private set;}
    public double AudioLevel {get;private set;}
    public double AudioPulse {get;private set;}
    public ulong AudioBeats {get;private set;}
    public bool AudioActive {get;private set;}
    public bool HasAudioLevels {get;private set;}
    public bool? HasAudioTrack {get;private set;}
    public long OutputWindow {get;private set;}
    public long DesktopParent {get;private set;}
    public bool Layered {get;private set;}
    public uint FirstPresent {get;private set;}
    public bool HasExited => disposed || process.HasExited;
    public event Action<string>? Failed;
    public event Action? Changed;
    public event Action? Ended;
    public bool HasEnded {get;private set;}
    public event Action? AudioLevelsChanged;
    public event Action? PerformanceUpdated;
    public static string Executable => Path.Combine(AppContext.BaseDirectory,"WallpaperHost.exe");
    public static ProcessStartInfo StartInfo(params string[] args)
    {
        var info=new ProcessStartInfo(Executable) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8 };
        info.Environment["ZZZWALLPAPER_BASE_DIRECTORY"]=LumaWall.Contracts.FeaturePacks.BaseDirectory;
        info.Environment["ZZZWALLPAPER_FEATURE_PACK_ROOT"]=LumaWall.Contracts.FeaturePacks.Root;
        foreach(var arg in args) info.ArgumentList.Add(arg);AppLog.ConfigureChild(info);return info;
    }
    public static async Task<ProbeResult> Probe(string path,string poster,CancellationToken token,bool metadataOnly=false)
    {
        await probeGate.WaitAsync(token);
        try {
            try {return await ProbeAttempt(path,poster,false,token,metadataOnly);}
            catch(Exception error) when(!token.IsCancellationRequested&&(error is IOException or JsonException or OperationCanceledException)) {
                AppLog.Warning("Probe","Retrying thumbnail with software decoding: "+Path.GetFileName(path),error);
                return await ProbeAttempt(path,poster,true,token,metadataOnly);
            }
        } finally {probeGate.Release();}
    }
    private static async Task<ProbeResult> ProbeAttempt(string path,string poster,bool software,CancellationToken token,bool metadataOnly)
    {
        string temporary=poster+"."+Guid.NewGuid().ToString("N")+".tmp";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(software?75:45));
        var args=new List<string>{"--probe","--file",path,"--poster",temporary};if(MediaFormats.IsImage(path))args.Add("--image");if(software)args.Add("--software");
        if(metadataOnly)args.Add("--metadata-only");
        using var p=new Process { StartInfo=StartInfo(args.ToArray()) };
        p.Start();int processId=p.Id;probes[processId]=Path.GetFileName(path);
        AppLog.Info("Probe",$"Started; pid={processId}; source={Path.GetFileName(path)}");
        try {
        App.ChildProcesses.Add(p);using var cancel=timeout.Token.Register(()=> {try {if(!p.HasExited)p.Kill(true);}catch {}});
        var errors=AppLog.CaptureStandardErrorAsync(p,"Probe",timeout.Token);
        string output=await p.StandardOutput.ReadToEndAsync(timeout.Token);await p.WaitForExitAsync(timeout.Token);await errors;
        var result=JsonSerializer.Deserialize<ProbeResult>(output,new JsonSerializerOptions {PropertyNameCaseInsensitive=true});
        if(result is null) throw new IOException(UiLanguage.Text("视频探测没有返回结果"));
        if(!result.Ok) throw new IOException(result.Error);
        if(p.ExitCode!=0)throw new IOException(UiLanguage.Format($"视频探测进程异常退出：0x{p.ExitCode:X8}"));
        if(!metadataOnly&&(!File.Exists(temporary)||new FileInfo(temporary).Length<=54))throw new IOException(UiLanguage.Text("未生成有效的视频缩略图"));
        token.ThrowIfCancellationRequested();if(!metadataOnly)File.Move(temporary,poster,true);
        AppLog.Info("Probe",$"Completed; pid={processId}; decoder={result.Decoder}");return result;
        } catch(OperationCanceledException) {AppLog.Warning("Probe",$"Cancelled or timed out; pid={processId}; userCancelled={token.IsCancellationRequested}");throw;
        } catch(Exception ex) {AppLog.Error("Probe",$"Failed; pid={processId}",ex);throw;
        } finally {
            probes.TryRemove(processId,out _);
            try {File.Delete(temporary);}catch(IOException) { }catch(UnauthorizedAccessException) { }
        }
    }
    public NativePlayback(string path,System.Drawing.Rectangle bounds,FillMode fill,Action<Action> dispatch,bool preview=false,VideoEffects? effects=null,bool compareOriginal=false,long behindWindow=0,bool loop=true,int fpsLimit=0,int transitionMs=0,DecoderMode decoderMode=DecoderMode.Auto)
    {
        this.dispatch=dispatch;SourceName=Path.GetFileNameWithoutExtension(path);
        var args=new List<string>{"--file",path,"--rect",bounds.X.ToString(),bounds.Y.ToString(),bounds.Width.ToString(),bounds.Height.ToString()};
        if(MediaFormats.IsImage(path))args.Add("--image");
        args.Add("--decoder-mode");args.Add(((int)decoderMode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        args.Add("--fps-limit");args.Add(Math.Clamp(fpsLimit,0,240).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(fill==FillMode.Contain) args.Add("--contain");
        if(!loop)args.Add("--no-loop");
        if(preview)args.Add("--preview");
        if(!preview&&behindWindow!=0) {args.Add("--behind");args.Add(behindWindow.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        if(!preview&&behindWindow!=0&&transitionMs>0) {args.Add("--transition-ms");args.Add(Math.Clamp(transitionMs,0,1000).ToString(System.Globalization.CultureInfo.InvariantCulture));}
        args.Add("--effects");args.Add((effects??VideoEffects.Original).ToWire());if(compareOriginal)args.Add("--original");
        process=new Process {StartInfo=StartInfo(args.ToArray()),EnableRaisingEvents=true};
        process.Start();ProcessId=process.Id;App.ChildProcesses.Add(process);
        commands=new PlaybackCommandWriter(async line=> {
            await process.StandardInput.WriteLineAsync(line.AsMemory(),sendingCancellation.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(sendingCancellation.Token).ConfigureAwait(false);
        },CommandFailed);
        AppLog.Info("WallpaperHost",$"Started; pid={ProcessId}; preview={preview}; source={SourceName}");
        _ = ReadEvents();_ = AppLog.CaptureStandardErrorAsync(process,"WallpaperHost");
    }
    private async Task ReadEvents()
    {
        string? failure=null;
        try
        {
            while(await process.StandardOutput.ReadLineAsync() is { } line)
            {
                using var json=JsonDocument.Parse(line);var root=json.RootElement;
                if(root.TryGetProperty("decoder",out var decoder)) Decoder=decoder.GetString()??Decoder;
                if(!root.TryGetProperty("event",out var type)) continue;
                switch(type.GetString())
                {
                    case "configured":
                        if(root.TryGetProperty("revision",out var configuredRevision)&&configuredRevision.TryGetInt64(out long revision)){configurationAcknowledgement.Confirm(revision);TraceRenderer("renderer_configuration","acknowledged",revision,"Configuration accepted by renderer; not physical display scanout.");}
                        continue;
                    case "performance":
                        var rawMeasurement=root.Clone();
                        var measurement=ReadPerformance(root);long generation=Interlocked.Read(ref performanceGeneration);
                        dispatch(()=> {
                            if(disposed||!(performanceMonitoring||automationMonitoring)||generation!=performanceGeneration)return;
                            RenderTelemetry=rawMeasurement;RenderTelemetryAt=DateTimeOffset.UtcNow;
                            Performance=measurement;PerformanceUpdated?.Invoke();
                        });continue;
                    case "prepared":
                        HasAudioTrack=root.TryGetProperty("hasAudio",out var track)?track.GetBoolean():null;
                        AppLog.Info("WallpaperHost",$"Prepared; pid={ProcessId}; decoder={Decoder}");
                        OutputWindow=root.GetProperty("hwnd").GetInt64();DesktopParent=root.GetProperty("parent").GetInt64();
                        Layered=root.TryGetProperty("layered",out var layered)&&layered.GetBoolean();prepared.TrySetResult();break;
                    case "ready":
                        OperationTrace.Record(launchOperation,"renderer_start","ready",$"pid={ProcessId}");
                        AppLog.Info("WallpaperHost",$"Playback ready; pid={ProcessId}");
                        FirstPresent=root.TryGetProperty("present",out var present)?present.GetUInt32():0;
                        ActualState=UiLanguage.Text("播放中");ready.TrySetResult();break;
                    case "paused":
                        dispatch(()=> {if(disposed)return;ActualState=UiLanguage.Text("已暂停");Changed?.Invoke();});continue;
                    case "playing":
                        dispatch(()=> {if(disposed)return;ActualState=UiLanguage.Text("播放中");HasEnded=false;Changed?.Invoke();});continue;
                    case "ended":
                        dispatch(()=> {if(disposed)return;HasEnded=true;ActualState=UiLanguage.Text("已播完 · 保持末帧");Ended?.Invoke();Changed?.Invoke();});continue;
                    case "decoder-changed": AppLog.Info("WallpaperHost",$"Decoder changed; pid={ProcessId}; {line}");break;
                    case "fallback": AppLog.Warning("WallpaperHost",$"pid={ProcessId}; hardware decoder fell back to software");Decoder="Software";break;
                    case "audio-level":
                        var rawAudio=root.Clone();
                        var input=ReadAudioUnit(root,"input");var level=ReadAudioUnit(root,"level");var pulse=ReadAudioUnit(root,"pulse");
                        ulong beats=root.TryGetProperty("beats",out var beatValue)&&beatValue.ValueKind==JsonValueKind.Number&&beatValue.TryGetUInt64(out var beatCount)?beatCount:0;
                        bool active=root.TryGetProperty("active",out var activeValue)&&activeValue.ValueKind==JsonValueKind.True;
                        var mappings=new Dictionary<int,(double Value,double Signal)>();
                        if(root.TryGetProperty("rules",out var rules)&&rules.ValueKind==JsonValueKind.Array)foreach(var rule in rules.EnumerateArray()) {
                            if(rule.TryGetProperty("id",out var id)&&id.TryGetInt32(out var ruleId)&&rule.TryGetProperty("value",out var rv)&&rv.TryGetDouble(out var number)&&double.IsFinite(number))mappings[ruleId]=(number,ReadAudioUnit(rule,"signal"));
                        }
                        // Meter updates do not refresh the rest of the control center or its draft controls.
                        dispatch(()=> {
                            if(disposed)return;
                            AudioRuleValues=mappings;AudioInput=input;AudioLevel=level;AudioPulse=pulse;AudioBeats=beats;AudioActive=active;HasAudioLevels=true;
                            AudioTelemetryAt=DateTimeOffset.UtcNow;
                            AudioTelemetry=rawAudio;
                            AudioLevelsChanged?.Invoke();
                        });continue;
                    case "sound-status":
                        AppLog.Info("VideoSound",$"pid={ProcessId}; {line}");
                        string sound=root.GetProperty("status").GetString()??"";
                        dispatch(()=> {
                            if(disposed)return;
                            SoundStatus=sound switch {"playing"=>UiLanguage.Text("视频声音开启"),"no-track"=>UiLanguage.Text("视频无音轨"),"unavailable"=>UiLanguage.Text("声音暂不可用 · 等待恢复"),"paused"=>UiLanguage.Text("视频声音已暂停"),_=>UiLanguage.Text("视频声音准备中")};
                            Changed?.Invoke();
                        });continue;
                    case "audio-status":
                        AppLog.Info("WallpaperAudio",$"pid={ProcessId}; {line}");
                        AudioStatusCode=root.GetProperty("status").GetString()??"";
                        AudioStatus=AudioStatusCode switch {
                            "disabled"=>UiLanguage.Text("音频联动未开启或已临时绕过"),
                            "paused"=>UiLanguage.Text("音频联动已暂停，随画面继续后恢复"),
                            "no-targets"=>UiLanguage.Text("请先开启一个已勾选的效果，并设置非零强度"),
                            "system-listening"=>UiLanguage.Text("正在跟随系统播放声音 · 默认输出设备"),
                            "system-unavailable"=>UiLanguage.Text("系统播放声音暂不可用，正在等待设备恢复；壁纸继续播放"),
                            "video-listening"=>UiLanguage.Text("正在分析壁纸视频音轨"),
                            "video-unavailable"=>UiLanguage.Text("视频音轨不可用或无法解析；可切换为系统播放声音"),
                            _=>UiLanguage.Text("正在准备音频分析")
                        };break;
                    case "configuration-error": configurationAcknowledgement.Fail();var detail=root.GetProperty("error").GetString();TraceRenderer("renderer_configuration","failed",detail:detail??"");AppLog.Error("WallpaperHost",$"pid={ProcessId}; configuration: {detail}");dispatch(()=>Failed?.Invoke(UiLanguage.Text("画面设置未生效：")+detail));break;
                    case "error": failure=root.GetProperty("error").GetString();TraceRenderer("renderer_runtime","failed",detail:failure??"");AppLog.Error("WallpaperHost",$"pid={ProcessId}; {failure}");break;
                }
                dispatch(()=>Changed?.Invoke());
            }
        }
        catch(Exception ex) {if(!disposed)AppLog.Error("WallpaperHost",$"Event stream failed; pid={ProcessId}",ex);failure=ex is JsonException?UiLanguage.Text("渲染进程返回了无效状态"):UiLanguage.Text("渲染连接已关闭");}
        finally
        {
            commands.Complete();
            configurationAcknowledgement.Fail();
            var error=new IOException(failure??UiLanguage.Text("视频渲染进程已退出"));prepared.TrySetException(error);ready.TrySetException(error);
            if(!disposed) {
                string exit="unavailable";
                try {await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));exit=$"0x{process.ExitCode:X8}";}catch(Exception ex) when(ex is InvalidOperationException or TimeoutException) { }
                if(!disposed) {TraceRenderer("renderer_exit","failed",detail:exit+"; "+error.Message);AppLog.Error("WallpaperHost",$"Unexpected exit; pid={ProcessId}; code={exit}; {error.Message}");ActualState=UiLanguage.Text("渲染已停止");dispatch(()=>Failed?.Invoke(error.Message));}
            }
        }
    }
    private static double ReadAudioUnit(JsonElement root,string name) =>
        root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number)&&double.IsFinite(number)
            ?Math.Clamp(number,0,1):0;
    private static double? ReadMetric(JsonElement root,string name) =>
        root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number)&&double.IsFinite(number)&&number>=0?number:null;
    private static int ReadCount(JsonElement root,string name) => (int)Math.Clamp(ReadMetric(root,name)??0,0,int.MaxValue);
    private static bool ReadFlag(JsonElement root,string name) => root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;
    private static PerformanceFrame ReadPerformance(JsonElement root)
    {
        bool gpuValid=ReadFlag(root,"gpuValid");bool paused=ReadFlag(root,"paused");
        double? Gpu(string name)=>gpuValid&&!paused?ReadMetric(root,name):null;
        double? Stage(string name)=>paused?null:ReadMetric(root,name);
        return new(DateTimeOffset.UtcNow,paused?0:ReadMetric(root,"fps")??0,ReadCount(root,"frames"),
            Stage("decodeMs"),Stage("audioMs"),Stage("submitMs"),Stage("presentMs"),
            Gpu("gpuMs"),Gpu("gpuBaseMs"),Gpu("gpuGlowMs"),Gpu("gpuBlurMs"),gpuValid&&!paused,
            ReadCount(root,"gpuSamples"),ReadCount(root,"passes"),ReadCount(root,"width"),ReadCount(root,"height"),
            ReadCount(root,"videoWidth"),ReadCount(root,"videoHeight"),
            root.TryGetProperty("decoder",out var decoder)&&decoder.ValueKind==JsonValueKind.String?decoder.GetString()??UiLanguage.Text("未知"):UiLanguage.Text("未知"),paused);
    }
    public void SetPerformanceMonitoring(bool enabled)
    {
        if(disposed||performanceMonitoring==enabled)return;
        performanceMonitoring=enabled;Interlocked.Increment(ref performanceGeneration);Performance=null;
        Send(enabled||automationMonitoring?"monitor 1":"monitor 0");
    }
    public void SetAutomationMonitoring(bool enabled) {automationMonitoring=enabled;Send(enabled||performanceMonitoring?"monitor 1":"monitor 0");}
    public void SetAudioCaptureDevice(string id) {if(id.Length>1024||id.Any(c=>c<' '||c>126))throw new ArgumentException(UiLanguage.Text("音频设备 ID 无效"));Send("audio-device "+id);}
    public Task Prepare(CancellationToken token) => prepared.Task.WaitAsync(TimeSpan.FromSeconds(25),token);
    public async Task Show(CancellationToken token) {Send("show");await ready.Task.WaitAsync(TimeSpan.FromSeconds(5),token);}
    public void SetFrameRateLimit(int cap)=>Send("fps-limit "+Math.Clamp(cap,0,240).ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void Pause(bool value) => Send(value?"pause":"resume");
    public void SetDecoderMode(DecoderMode mode)=>Send("decoder-mode "+((int)mode).ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void SetSound(bool enabled) => Send(enabled?"sound 1":"sound 0");
    public void SetSoundVolume(int volume) => Send("sound-volume "+Math.Clamp(volume,0,100).ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void SetPosition(WallpaperPosition position) {position.Validate();Send(FormattableString.Invariant($"position {position.X} {position.Y} {position.Scale}"));}
    public void SetLooping(bool enabled)
    {
        if(enabled)HasEnded=false;
        Send(enabled?"loop 1":"loop 0");
    }
    public void RestartEnded()
    {
        HasEnded=false;ActualState=UiLanguage.Text("播放中");Send("restart");
    }
    public bool ConfigureEffects(FillMode fill,VideoEffects effects,bool compareOriginal,bool preset=false)
    {
        if(disposed)return false;
        long revision=configurationAcknowledgement.Begin();
        var operation=OperationTrace.Current;
        lock(traceGate) {
            lastRendererOperation=operation;
            if(operation!=null) {configurationTraces[revision]=operation;while(configurationTraces.Count>64)configurationTraces.Remove(configurationTraces.First().Key);}
        }
        OperationTrace.Record(operation,"renderer_configuration","queued",$"pid={ProcessId}; revision={revision}",sampledEvent:true);
        string command=preset?"configure-preset":"configure";
        if(commands.TryWrite(()=>$"{command} {revision} {(fill==FillMode.Contain?1:0)} {(compareOriginal?1:0)} {effects.ToWire()}",preset?null:"configure"))return true;
        CommandFailed(new IOException(UiLanguage.Text("播放命令队列已关闭或繁忙。")));return false;
    }
    public Task<bool> WaitForConfiguration(CancellationToken token)=>configurationAcknowledgement.Wait(token);
    public void PreviewEffects(FillMode fill,VideoEffects effects,bool compareOriginal) => ConfigureEffects(fill,effects,compareOriginal);
    private void Send(string command)
    {
        if(disposed)return;
        string key=command.Split(' ')[0];
        string? coalesce=key is "sound-volume" or "fps-limit" or "monitor" or "position"?key:null;
        if(!commands.TryWrite(()=>command,coalesce))CommandFailed(new IOException(UiLanguage.Text("播放命令队列已关闭或繁忙。")));
    }
    private void CommandFailed(Exception error)
    {
        TraceRenderer("renderer_command","failed",detail:error.Message);
        configurationAcknowledgement.Fail();
        if(disposed||Interlocked.Exchange(ref commandFailureReported,1)!=0)return;
        prepared.TrySetException(error);ready.TrySetException(error);
        AppLog.Warning("WallpaperHost",$"Cannot send playback command; pid={ProcessId}",error);
        dispatch(()=> {if(!disposed)Failed?.Invoke(UiLanguage.Text("播放进程未能接收操作，请重新应用壁纸。")+error.Message);});
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;commands.Complete("stop");configurationAcknowledgement.Fail();
        AppLog.Info("WallpaperHost",$"Stop requested; pid={ProcessId}");
        // Never synchronously wait for a decoder from the UI/input service thread.
        _=Task.Run(()=>ChildProcessShutdown.StopAsync(process,commands.Completion,sendingCancellation,
            report:error=>AppLog.Warning("WallpaperHost",UiLanguage.Text("播放通信退出未完成"),error)));
    }
}
