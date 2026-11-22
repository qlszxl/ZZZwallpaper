using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using LumaWall.Automation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct AutomationRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",EntryPoint="GetWindowRect")] private static extern bool AutomationGetWindowRect(nint window,out AutomationRect rect);
    [DllImport("dwmapi.dll",EntryPoint="DwmFlush")] private static extern int AutomationDwmFlush();
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private sealed class AutomationDisplayMode {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName="";
        public ushort SpecVersion,DriverVersion,Size,DriverExtra;public uint Fields;
        public int PositionX,PositionY;public uint Orientation,FixedOutput;public short Color,Duplex,YResolution,TTOption,Collate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName="";
        public ushort LogPixels;public uint BitsPerPixel,Width,Height,Flags,Frequency,IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
    }
    [DllImport("user32.dll",EntryPoint="EnumDisplaySettingsW",CharSet=CharSet.Unicode)] private static extern bool AutomationDisplaySettings(string device,int mode,[In,Out] AutomationDisplayMode details);
    private static object AutomationScreenMode(string screen) {
        var value=new AutomationDisplayMode {Size=(ushort)Marshal.SizeOf<AutomationDisplayMode>()};bool read=AutomationDisplaySettings(screen,-1,value);
        return new {refreshHz=read&&value.Frequency>1?(uint?)value.Frequency:null,rotationDegrees=read?(uint?)(value.Orientation*90):null,available=read};
    }
    private JsonElement[] AutomationApplicationLogs(DateTimeOffset? after,int limit,string? operationId=null)
    {
        string? directory=AppLog.DirectoryPath;if(directory==null||!Directory.Exists(directory))return [];
        var result=new List<JsonElement>();
        foreach(var file in new DirectoryInfo(directory).EnumerateFiles("zzzwallpaper-*.jsonl").Where(f=>(f.Attributes&FileAttributes.ReparsePoint)==0).OrderByDescending(f=>f.LastWriteTimeUtc).Take(12)) {
            try {
                using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);long start=Math.Max(0,stream.Length-256*1024);stream.Position=start;
                using var reader=new StreamReader(stream);if(start>0)reader.ReadLine();string? line;
                while((line=reader.ReadLine())!=null) {
                    if(line.Length>64000)continue;
                    try {using var document=JsonDocument.Parse(line);var row=document.RootElement;
                        if(!row.TryGetProperty("session",out var session)||session.GetString()!=AppLog.Session)continue;
                        if(after!=null&&row.GetProperty("time").GetDateTimeOffset()<=after)continue;
                        if(operationId!=null&&(!row.TryGetProperty("operationId",out var rowId)||rowId.GetString()!=operationId))continue;
                        string? Bounded(string key)=>row.TryGetProperty(key,out var value)?value.ToString()[..Math.Min(value.ToString().Length,1000)]:null;
                        result.Add(JsonSerializer.SerializeToElement(new {time=row.GetProperty("time"),session=AppLog.Session,level=Bounded("level"),category=Bounded("category"),message=Bounded("message"),exception=Bounded("exception"),operationId=Bounded("operationId"),data=row.TryGetProperty("data",out var data)&&data.GetRawText().Length<=8000?data.Clone():(JsonElement?)null}));if(result.Count>limit*4)result.RemoveRange(0,result.Count-limit*4);
                    }catch(JsonException) { }
                }
            }catch(IOException) { }
        }
        return result.Concat(OperationTrace.ReadRecent(operationId,after,limit))
            .DistinctBy(r=>(r.GetProperty("time").GetDateTimeOffset(),r.GetProperty("message").GetString(),r.GetProperty("operationId").ToString()))
            .OrderBy(r=>r.GetProperty("time").GetDateTimeOffset()).TakeLast(limit).ToArray();
    }
    private string AutomationArtifactPath(string extension)
    {
        string directory=Path.Combine(store.Root,"automation-artifacts");Directory.CreateDirectory(directory);
        if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("输出目录不能是目录联接");
        store.MaintainAutomationArtifacts();
        return Path.Combine(directory,Guid.NewGuid().ToString("N")+extension);
    }
    private async Task<OperationReply> AutomationDiagnostics(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        switch(request.Name) {
            case "ui_capture_desktop":case "ui_capture_window": {
                Rectangle bounds;string? screenId=null;
                if(request.Name=="ui_capture_desktop") {var screen=OperationScreen(Arg(a,"screenId"));screenId=screen.DeviceName;bounds=screen.Bounds;}
                else {
                    var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
                    if(!IsWindowVisible(handle)||IsIconic(handle)||!AutomationGetWindowRect(handle,out var r))throw new OperationFault("invalid_state","控制中心窗口不可见，请先打开窗口",null);
                    bounds=Rectangle.Intersect(new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top),System.Windows.Forms.SystemInformation.VirtualScreen);
                }
                if(bounds.Width<=0||bounds.Height<=0||(long)bounds.Width*bounds.Height>80_000_000)throw new OperationFault("invalid_state","截图区域不可用或过大",null);
                AutomationDwmFlush();var capturedAt=DateTimeOffset.UtcNow;string path=AutomationArtifactPath(".png");
                using(var bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb)) {using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);bitmap.Save(path,ImageFormat.Png);}
                return OperationReply.Success(new {path,width=bounds.Width,height=bounds.Height,capturedAt,screenId,configurationRevision=store.ConfigurationRevision(),captureMethod="visible_desktop_pixels",occlusion="other_visible_windows_are_included",layers=new[]{"visible_wallpaper","visible_widgets","desktop_and_other_windows"},rendererRevisions=wallpapers!.Running.Where(p=>screenId==null||p.Key==screenId).Select(p=>new {screenId=p.Key,processId=p.Value.Player.ProcessId,requestedRevision=p.Value.Player.RequestedRevision,appliedRevision=p.Value.Player.AppliedRevision}),widgetRevision=widgets!.ConfigurationRevision,appliedWidgetRevision=widgets.AppliedRevision,widgetRevisionConfirmed=widgets.ConfigurationRevision==widgets.AppliedRevision});
            }
            case "diagnostics_get_render_stats": {
                var player=OperationWallpaper(OperationScreen(Arg(a,"screenId")).DeviceName).Player;
                player.SetAutomationMonitoring(!a.TryGetProperty("enabled",out var monitoring)||monitoring.GetBoolean());
                return OperationReply.Success(new {screenId=Arg(a,"screenId"),sampledAt=player.RenderTelemetryAt,stats=player.RenderTelemetry,status=player.RenderTelemetry==null?"warming_up":DateTimeOffset.UtcNow-player.RenderTelemetryAt>TimeSpan.FromSeconds(3)?"stale":"available",decoder=player.Decoder,requestedRevision=player.RequestedRevision,appliedRevision=player.AppliedRevision,measurement="renderer_present_call_completion; not physical display scanout",unavailable=new[]{"physical_display_present_count"}});
            }
            case "audio_get_state": {
                var player=OperationWallpaper(OperationScreen(Arg(a,"screenId")).DeviceName).Player;
                return OperationReply.Success(new {sampledAt=player.AudioTelemetryAt,status=player.AudioStatusCode,active=player.AudioActive,requestedDevice=modules.Settings.Get("audioCaptureDevice:"+Arg(a,"screenId"))??"default",telemetry=player.AudioTelemetry,measurement="linear_rms_amplitude; bass/mid/treble from native band filters"});
            }
            case "diagnostics_get_logs": {
                IEnumerable<JsonElement> entries=automationJournal.List();
                if(Optional(a,"operationId") is { } id)entries=entries.Where(e=>e.GetProperty("operationId").GetString()==id);
                DateTimeOffset? since=null;if(Optional(a,"after") is { } after) {if(!DateTimeOffset.TryParse(after,out var time))throw new OperationFault("invalid_argument","时间格式无效",null);since=time;entries=entries.Where(e=>e.GetProperty("startedAt").GetDateTimeOffset()>time);}
                int limit=a.TryGetProperty("limit",out var count)?count.GetInt32():50;
                var logs=AutomationApplicationLogs(since,limit,Optional(a,"operationId"));
                return OperationReply.Success(new {scope="this_application_session",items=entries.TakeLast(limit).Select(e=>new {operationId=e.GetProperty("operationId"),tool=e.GetProperty("tool"),status=e.GetProperty("status"),startedAt=e.GetProperty("startedAt")}),applicationLogs=logs,boundedTail=true});
            }
            case "diagnostics_export": {
                if(Optional(a,"format")=="bundle") {
                    bool Choice(string name,bool fallback)=>a.TryGetProperty(name,out var value)?value.GetBoolean():fallback;
                    var options=new DiagnosticExportOptions(Choice("includeLogs",true),Choice("includeEnvironment",true),Choice("includeConfiguration",false));
                    options.Validate();string bundle=AutomationArtifactPath(".zip");
                    await ExportDiagnosticBundleAsync(bundle,options,token);
                    return OperationReply.Success(new {path=bundle,createdAt=DateTimeOffset.UtcNow,format="bundle",includes=options});
                }
                if(new[]{"includeLogs","includeEnvironment","includeConfiguration"}.Any(name=>a.TryGetProperty(name,out _)))
                    throw new OperationFault("invalid_argument","诊断内容选项需要 format=bundle","arguments.format");
                string path=AutomationArtifactPath(".json");
                var report=new {version=ApplicationVersion,instanceId=automationJournal.InstanceId,capturedAt=DateTimeOffset.UtcNow,configurationRevision=store.ConfigurationRevision(),tools=ToolCatalog.All.Select(t=>t.Name),operations=automationJournal.List().Select(e=>new {operationId=e.GetProperty("operationId"),tool=e.GetProperty("tool"),status=e.GetProperty("status"),startedAt=e.GetProperty("startedAt")}),renderers=wallpapers!.Running.Select(p=>new {screenId=p.Key,decoder=p.Value.Player.Decoder,p.Value.Player.ActualState,stats=p.Value.Player.RenderTelemetry}),redaction="media paths, user text, shortcuts and image data excluded"};
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}),token);
                return OperationReply.Success(new {path,createdAt=DateTimeOffset.UtcNow});
            }
            default:return await AutomationAudio(request,token);
        }
    }
}
