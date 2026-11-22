using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumaWall.ControlCenter;

public sealed record ProcessGpuUsage(double? Percent,long? DedicatedBytes,long? SharedBytes);
public sealed record ProcessGpuSnapshot(ProcessGpuUsage Total,IReadOnlyDictionary<int,ProcessGpuUsage> Processes)
{
    public double? ExternalPercent {get;init;}
}

// Collect once for all app PIDs. Sum contributions to the same physical engine,
// then report the busiest engine; utilization percentages are never summed.
internal sealed class ProcessGpuSampler : IDisposable
{
    private const uint MoreData=0x800007D2,Format=0x200|0x1000|0x8000;
    private SafeQueryHandle? query;
    private nint gpu,dedicated,shared;
    private bool hasBaseline,disposed;
    private long retryAfter;
    private readonly bool enginesOnly;
    internal ProcessGpuSampler(bool enginesOnly=false) {this.enginesOnly=enginesOnly;}
    private sealed record CounterData(bool Healthy,Dictionary<int,Dictionary<string,double>> Values,HashSet<int> Invalid);
    private static ProcessGpuSnapshot Unavailable()=>new(new(null,null,null),new Dictionary<int,ProcessGpuUsage>());
    internal double? Sample(IReadOnlyList<PerformanceProcessTarget> targets)=>SampleResources(targets).Total.Percent;
    internal ProcessGpuSnapshot SampleResources(IReadOnlyList<PerformanceProcessTarget> targets)
    {
        if(disposed||targets.Count==0)return Unavailable();
        try {
            if(query==null) {
                if(Stopwatch.GetTimestamp()<retryAfter)return Unavailable();
                if(PdhOpenQueryW(null,0,out var opened)!=0) {opened?.Dispose();RetryLater();return Unavailable();}
                query=opened;
                gpu=Add(@"\GPU Engine(*)\Utilization Percentage");
                if(!enginesOnly) {
                    dedicated=Add(@"\GPU Process Memory(*)\Dedicated Usage");
                    shared=Add(@"\GPU Process Memory(*)\Shared Usage");
                }
                if(gpu==0&&dedicated==0&&shared==0) {RetryLater();return Unavailable();}
            }
            if(PdhCollectQueryData(query)!=0) {RetryLater();return Unavailable();}
            var engines=hasBaseline?Read(gpu,true):null;hasBaseline=true;
            var dedicatedData=Read(dedicated,false);var sharedData=Read(shared,false);
            var ids=targets.Where(t=>t.ProcessId>0).Select(t=>t.ProcessId).ToHashSet();
            ProcessGpuUsage Usage(IEnumerable<int> processes) {
                var list=processes.ToArray();
                return new(Aggregate(engines,list,true),ToBytes(Aggregate(dedicatedData,list,false)),ToBytes(Aggregate(sharedData,list,false)));
            }
            // Sum other processes on each physical engine, then select the busiest
            // engine. Subtracting two overall GPU percentages is not equivalent.
            var external=enginesOnly&&engines!=null
                ?Aggregate(engines,engines.Values.Keys.Where(id=>!ids.Contains(id)).ToArray(),true):null;
            return new(Usage(ids),ids.ToDictionary(id=>id,id=>Usage([id]))) {ExternalPercent=external};
        }catch(Exception error) when(error is DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException) {
            RetryLater();return Unavailable();
        }
    }
    private nint Add(string path)=>query!=null&&PdhAddEnglishCounterW(query,path,0,out var counter)==0?counter:0;
    private static long? ToBytes(double? number)=>number is { } value&&value<long.MaxValue?(long)value:null;
    private static double? Aggregate(CounterData? data,IReadOnlyList<int> ids,bool engines)
    {
        if(data is not {Healthy:true}||ids.Any(data.Invalid.Contains))return null;
        var totals=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(int id in ids)if(data.Values.TryGetValue(id,out var values))
            foreach(var pair in values)totals[pair.Key]=totals.GetValueOrDefault(pair.Key)+pair.Value;
        return engines?totals.Count==0?0:Math.Clamp(totals.Values.Max(),0,100):totals.Values.Sum();
    }
    private static CounterData? Read(nint counter,bool engines)
    {
        if(counter==0)return null;
        for(int attempt=0;attempt<3;attempt++) {
            uint size=0;
            if(PdhGetFormattedCounterArrayW(counter,Format,ref size,out _,0)!=MoreData||size==0||size>16*1024*1024)return null;
            uint capacity=size;nint buffer=Marshal.AllocHGlobal((int)capacity);
            try {
                uint status=PdhGetFormattedCounterArrayW(counter,Format,ref size,out uint count,buffer);
                if(status==MoreData)continue;
                int stride=Marshal.SizeOf<CounterItem>();
                if(status!=0||(ulong)count*(uint)stride>capacity)return null;
                var values=new Dictionary<int,Dictionary<string,double>>();var invalid=new HashSet<int>();bool healthy=false;
                for(int index=0;index<count;index++) {
                    var item=Marshal.PtrToStructure<CounterItem>(buffer+index*stride);
                    if(!TryParseInstance(Marshal.PtrToStringUni(item.Name),engines,out int pid,out string key))continue;
                    bool valid=item.Value.Status<=1&&double.IsFinite(item.Value.Value)&&item.Value.Value>=0;
                    healthy|=valid;if(!valid) {invalid.Add(pid);continue;}
                    if(!values.TryGetValue(pid,out var group))values[pid]=group=new(StringComparer.OrdinalIgnoreCase);
                    group[key]=group.GetValueOrDefault(key)+item.Value.Value;
                }
                return new(healthy,values,invalid);
            }finally {Marshal.FreeHGlobal(buffer);}
        }
        return null;
    }
    private static bool TryParseInstance(string? name,bool engines,out int pid,out string key)
    {
        pid=0;key="";
        if(name==null||!name.StartsWith("pid_",StringComparison.OrdinalIgnoreCase))return false;
        int endPid=name.IndexOf('_',4);
        if(endPid<=4||!int.TryParse(name.AsSpan(4,endPid-4),NumberStyles.None,CultureInfo.InvariantCulture,out pid))return false;
        int adapter=name.IndexOf("luid_",endPid,StringComparison.OrdinalIgnoreCase);
        if(adapter<0||engines&&name.IndexOf("_eng_",adapter,StringComparison.OrdinalIgnoreCase)<0)return false;
        int duplicate=name.IndexOf('#',adapter);key=duplicate<0?name[adapter..]:name[adapter..duplicate];return true;
    }
    private void RetryLater() {
        query?.Dispose();query=null;gpu=dedicated=shared=0;hasBaseline=false;
        retryAfter=Stopwatch.GetTimestamp()+Stopwatch.Frequency*30;
    }
    public void Dispose() {disposed=true;query?.Dispose();query=null;gpu=dedicated=shared=0;}
    private sealed class SafeQueryHandle : SafeHandleZeroOrMinusOneIsInvalid {
        public SafeQueryHandle():base(true) {}
        protected override bool ReleaseHandle()=>PdhCloseQuery(handle)==0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue {public uint Status;public double Value;}
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem {public nint Name;public CounterValue Value;}
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source,nuint data,out SafeQueryHandle query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(SafeQueryHandle query,string path,nuint data,out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(SafeQueryHandle query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter,uint format,ref uint size,out uint count,nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
