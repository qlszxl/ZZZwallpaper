using System.Diagnostics;
using System.Globalization;

namespace LumaWall.Diagnostics;

// Diagnostics are observational: neither persistence nor undo depends on this buffer.
public sealed record OperationContext(string Id,string Page,string Action,string Target,string Source,long Started);

public static class OperationTrace
{
    private sealed record Ambient(OperationContext? Value);
    private sealed record Entry(DateTimeOffset Time,int Thread,OperationContext? Operation,string Stage,string Status,string Detail,double? ElapsedMs);
    private static readonly AsyncLocal<Ambient?> ambient=new();
    private static readonly object gate=new(),writeGate=new();
    private static readonly Queue<Entry> pending=new(),recent=new();
    private static readonly Dictionary<string,long> sampled=new();
    private static readonly Dictionary<string,(long At,int Suppressed)> faults=new();
    private static readonly string userProfile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static RollingFileLog? sink;
    private static System.Threading.Timer? timer;
    private static long sequence,dropped;
    private static int uiThread;
    private static Func<OperationContext?>? uiOperation;
    public static OperationContext? Current {
        get {
            if(ambient.Value is { } value)return value.Value;
            try {return Environment.CurrentManagedThreadId==uiThread?uiOperation?.Invoke():null;}catch {return null;}
        }
    }
    public static void Initialize(string? directory,Func<OperationContext?> currentUiOperation)
    {
        try {
            uiThread=Environment.CurrentManagedThreadId;uiOperation=currentUiOperation;
            if(directory==null||sink!=null)return;
            sink=new RollingFileLog(directory,"Operations",AppLog.Version,AppLog.Session,maxEntriesPerSecond:512);
            using(ExecutionContext.SuppressFlow())timer=new(_=>Flush(),null,500,500);
        } catch { /* A diagnostic failure must not change application behavior. */ }
    }
    public static string PageCode(string page)=>page switch {
        "wallpaper"=>"HOME","library"=>"LIB","display"=>"FX","widgets"=>"WGT","presets" or "sharing"=>"PRE",
        "settings" or "appearance" or "performance" or "history"=>"SET",_=>"SYS"
    };
    public static OperationContext Begin(string page,string action,string target="",string source="ui")
    {
        string id=PageCode(page)+"-"+DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+
            Interlocked.Increment(ref sequence).ToString("D6",CultureInfo.InvariantCulture);
        var operation=new OperationContext(id,Clip(page,32),Clip(action,96),Clip(target,192),Clip(source,16),Stopwatch.GetTimestamp());
        Record(operation,"operation","started");return operation;
    }
    public static IDisposable Enter(OperationContext? operation)=>new Scope(operation);
    private sealed class Scope : IDisposable
    {
        private readonly Ambient? previous=ambient.Value;
        public Scope(OperationContext? operation)=>ambient.Value=new(operation);
        public void Dispose()=>ambient.Value=previous;
    }
    public static void Record(OperationContext? operation,string stage,string status,string detail="",bool sampledEvent=false)
    {
        if(sink==null)return;
        try {
            long now=Environment.TickCount64;
            lock(gate) {
                if(sampledEvent) {
                    string key=operation?.Id+"|"+stage+"|"+status;
                    if(sampled.TryGetValue(key,out long last)&&now-last<500)return;
                    if(sampled.Count>=256)sampled.Clear();sampled[key]=now;
                }
                var entry=new Entry(DateTimeOffset.Now,Environment.CurrentManagedThreadId,operation,Clip(stage,64),Clip(status,40),Clip(detail,2048),
                    operation==null?null:Math.Round(Stopwatch.GetElapsedTime(operation.Started).TotalMilliseconds,2));
                if(pending.Count>=512) {pending.Dequeue();dropped++;}
                pending.Enqueue(entry);
                if(recent.Count>=128)recent.Dequeue();recent.Enqueue(entry);
            }
        } catch { }
    }
    public static void Fault(OperationContext? operation,string stage,string detail)
    {
        if(sink==null)return;
        long now=Environment.TickCount64;string key=stage+"|"+Clip(detail,128);
        lock(gate) {
            if(faults.TryGetValue(key,out var prior)) {
                if(now-prior.At<30000) {faults[key]=(prior.At,prior.Suppressed+1);return;}
                if(prior.Suppressed>0)detail+=$"; repeatedFaultsSuppressed={prior.Suppressed}";
            }
            if(faults.Count>=64)faults.Clear();faults[key]=(now,0);
        }
        Record(operation,stage,"failed",detail);
        // Snapshot is detached from controls and bounded. Later faults do not erase it.
        Entry[] preceding;lock(gate)preceding=recent.TakeLast(32).ToArray();
        Record(operation,"fault_context","snapshot",string.Join("; ",preceding.Select(e=>$"{e.Operation?.Id}:{e.Stage}/{e.Status}")));
        ThreadPool.QueueUserWorkItem(_=>Flush());
    }
    public static void NativeSnapshot(OperationContext? operation,string snapshot)
    {
        // Native snapshots have a separate bounded record size; no full process dump.
        int length=Math.Min(snapshot.Length,48*1024);
        for(int offset=0;offset<length;offset+=1800)
            Record(operation,"native_context","snapshot",$"part={offset/1800+1}; "+snapshot.Substring(offset,Math.Min(1800,length-offset)));
    }
    public static System.Text.Json.JsonElement[] ReadRecent(string? operationId=null,DateTimeOffset? after=null,int limit=100)
    {
        Entry[] snapshot;
        lock(gate)snapshot=recent.Where(e=>(operationId==null||e.Operation?.Id==operationId)&&(after==null||e.Time>after)).TakeLast(Math.Clamp(limit,1,100)).ToArray();
        return snapshot.Select(e=>System.Text.Json.JsonSerializer.SerializeToElement(new {
            time=e.Time,session=AppLog.Session,level=e.Status=="failed"?"Error":"Information",category="OperationTrace",
            message=e.Stage+": "+e.Status,exception=(string?)null,operationId=e.Operation?.Id,data=e
        })).ToArray();
    }
    public static void Flush(bool wait=false)
    {
        if(wait)Monitor.Enter(writeGate);
        else if(!Monitor.TryEnter(writeGate))return;
        try {
            var logger=sink;if(logger==null)return;
            Entry[] batch;long lost;
            lock(gate) {batch=pending.ToArray();pending.Clear();lost=dropped;dropped=0;}
            if(lost>0)logger.Write("Warning","OperationTrace",$"Bounded trace queue dropped {lost} oldest entries.",flush:false);
            foreach(var entry in batch)logger.Write(entry.Status=="failed"||entry.Stage=="native_context"?"Error":"Information","OperationTrace",entry.Stage+": "+entry.Status,
                data:entry,occurredAt:entry.Time,originatingThread:entry.Thread,flush:false,operationId:entry.Operation?.Id);
            logger.Flush();
        } catch { } finally {Monitor.Exit(writeGate);}
    }
    public static void Shutdown()
    {
        timer?.Dispose();timer=null;Flush(wait:true);
        lock(writeGate) {sink?.Dispose();sink=null;}
        uiOperation=null;
    }
    private static string Clip(string? text,int limit)
    {
        if(text==null)return "";
        if(userProfile.Length>0)text=text.Replace(userProfile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);
        return text.Length<=limit?text:text[..limit]+"…";
    }
}
