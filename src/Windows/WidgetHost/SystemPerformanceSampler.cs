using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LumaWall.Contracts;

namespace LumaWall.WidgetHost;

internal sealed record SystemPerformanceSample(double? Cpu,double? Memory,double? UsedGiB,double? TotalGiB,
    double? Gpu,double? GpuMemory,double? DiskRead,double? DiskWrite,double? NetworkReceive,double? NetworkSend,IReadOnlyDictionary<PerformanceMetrics,double>? Extended=null)
{
    internal double? Get(PerformanceMetrics field)=>field switch {
        PerformanceMetrics.Cpu=>Cpu,PerformanceMetrics.Memory=>Memory,PerformanceMetrics.Gpu=>Gpu,PerformanceMetrics.GpuMemory=>GpuMemory,
        PerformanceMetrics.DiskRead=>DiskRead,PerformanceMetrics.DiskWrite=>DiskWrite,PerformanceMetrics.NetworkReceive=>NetworkReceive,PerformanceMetrics.NetworkSend=>NetworkSend,_=>Extended!=null&&Extended.TryGetValue(field,out var value)?value:null
    };
}

// Shared by visible widgets. Only selected counters are collected; unsupported
// counters remain unavailable. Sampling runs off the window message thread.
internal sealed partial class SystemPerformanceSampler : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue {public uint Status;public double Value;}
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem {public nint Name;public CounterValue Value;}
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus {
        public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source,nuint data,out nint query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query,string path,nuint data,out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(nint counter,uint format,out uint type,out CounterValue value);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter,uint format,ref uint size,out uint count,nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private const uint MoreData=0x800007D2,Format=0x200|0x1000|0x8000; // DOUBLE | NOSCALE | NOCAP100
    private readonly object gate=new();
    private nint query,cpu,gpu,vram,diskRead,diskWrite,diskIdle,diskReadLatency,diskWriteLatency;
    private const PerformanceMetrics NetworkFields=PerformanceMetrics.NetworkReceive|PerformanceMetrics.NetworkSend|PerformanceMetrics.NetworkTotal;
    private const PerformanceMetrics PdhFields=PerformanceMetrics.Cpu|PerformanceMetrics.Gpu|PerformanceMetrics.GpuMemory|PerformanceMetrics.DiskRead|PerformanceMetrics.DiskWrite|PerformanceMetrics.DiskBusy|PerformanceMetrics.DiskReadLatency|PerformanceMetrics.DiskWriteLatency;
    private HardwarePerformanceSampler? hardware;
    private PerformanceMetrics selected;
    private bool disposed;
    private Dictionary<string,(long Received,long Sent,long Tick)> network=[];

    private void Configure(PerformanceMetrics fields)
    {
        if(fields==selected)return;
        CloseQuery();selected=fields;network.Clear();
        if((fields&PdhFields)!=0&&PdhOpenQueryW(null,0,out query)==0) {
            cpu=Add(PerformanceMetrics.Cpu,@"\Processor Information(_Total)\% Processor Time");
            gpu=Add(PerformanceMetrics.Gpu,@"\GPU Engine(*)\Utilization Percentage");
            vram=Add(PerformanceMetrics.GpuMemory,@"\GPU Adapter Memory(*)\Dedicated Usage");
            diskRead=Add(PerformanceMetrics.DiskRead,@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            diskWrite=Add(PerformanceMetrics.DiskWrite,@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            diskIdle=Add(PerformanceMetrics.DiskBusy,@"\PhysicalDisk(_Total)\% Idle Time");
            diskReadLatency=Add(PerformanceMetrics.DiskReadLatency,@"\PhysicalDisk(_Total)\Avg. Disk sec/Read");
            diskWriteLatency=Add(PerformanceMetrics.DiskWriteLatency,@"\PhysicalDisk(_Total)\Avg. Disk sec/Write");
            _=PdhCollectQueryData(query);
        }
        if((fields&NetworkFields)!=0)_=NetworkRates();
    }
    private nint Add(PerformanceMetrics field,string path)=>(selected&field)!=0&&PdhAddEnglishCounterW(query,path,0,out var counter)==0?counter:0;
    internal SystemPerformanceSample? Sample(PerformanceMetrics fields)
    {
        lock(gate) {
            if(disposed)return null;
            bool newBaseline=selected!=fields;Configure(fields);
            var extended=new Dictionary<PerformanceMetrics,double>();
            double? cpuPercent=null,gpuPercent=null,gpuBytes=null,read=null,write=null,memoryPercent=null,used=null,total=null;
            if(!newBaseline&&query!=0&&PdhCollectQueryData(query)==0) {
                cpuPercent=Scalar(cpu);if(cpuPercent.HasValue)cpuPercent=Math.Clamp(cpuPercent.Value,0,100);
                read=Scalar(diskRead);write=Scalar(diskWrite);
                if(Scalar(diskIdle) is { } idle)extended[PerformanceMetrics.DiskBusy]=Math.Clamp(100-idle,0,100);
                if(Scalar(diskReadLatency) is { } readDelay)extended[PerformanceMetrics.DiskReadLatency]=readDelay*1000;
                if(Scalar(diskWriteLatency) is { } writeDelay)extended[PerformanceMetrics.DiskWriteLatency]=writeDelay*1000;
                var engines=ArrayValues(gpu);
                if(engines is {Count:>0}) {
                    // Sum processes on the SAME physical engine, then use the
                    // busiest engine; summing different engines is misleading.
                    var totals=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                    foreach(var item in engines) {
                        int index=item.Name.IndexOf("luid_",StringComparison.OrdinalIgnoreCase);
                        if(index<0||!item.Name.Contains("_eng_",StringComparison.OrdinalIgnoreCase))continue;
                        string engine=item.Name[index..];totals[engine]=totals.GetValueOrDefault(engine)+item.Value;
                    }
                    if(totals.Count>0)gpuPercent=Math.Clamp(totals.Values.Max(),0,100);
                }
                var adapters=ArrayValues(vram);
                if(adapters is {Count:>0})gpuBytes=adapters.Sum(item=>item.Value);
            }
            if((fields&(PerformanceMetrics.Memory|PerformanceMetrics.MemoryAvailable))!=0) {
                var memory=new MemoryStatus {Length=(uint)Marshal.SizeOf<MemoryStatus>()};
                if(GlobalMemoryStatusEx(ref memory)&&memory.TotalPhysical>0&&memory.AvailablePhysical<=memory.TotalPhysical) {
                    if((fields&PerformanceMetrics.MemoryAvailable)!=0)extended[PerformanceMetrics.MemoryAvailable]=memory.AvailablePhysical;
                    total=memory.TotalPhysical/1073741824d;used=(memory.TotalPhysical-memory.AvailablePhysical)/1073741824d;memoryPercent=used/total*100;
                }
            }
            (double? Receive,double? Send) rates=(null,null);
            if(!newBaseline&&(fields&NetworkFields)!=0)rates=NetworkRates();
            if((fields&PerformanceMetrics.NetworkTotal)!=0&&rates.Receive is { } receive&&rates.Send is { } send)extended[PerformanceMetrics.NetworkTotal]=receive+send;
            ReadSystemDetails(fields,extended);
            if((fields&HardwarePerformanceSampler.Fields)!=0){hardware??=new();foreach(var pair in hardware.Sample(fields&HardwarePerformanceSampler.Fields))if((fields&pair.Key)!=0)extended[pair.Key]=pair.Value;}
            else {hardware?.Dispose();hardware=null;}
            return new(cpuPercent,memoryPercent,used,total,gpuPercent,gpuBytes,read,write,rates.Receive,rates.Send,extended);
        }
    }
    private static double? Scalar(nint counter)=>counter!=0&&PdhGetFormattedCounterValue(counter,Format,out _,out var value)==0&&Valid(value)?value.Value:null;
    private static bool Valid(CounterValue value)=>value.Status<=1&&double.IsFinite(value.Value)&&value.Value>=0;
    private static List<(string Name,double Value)>? ArrayValues(nint counter)
    {
        if(counter==0)return null;
        for(int attempt=0;attempt<2;attempt++) {
            uint size=0;if(PdhGetFormattedCounterArrayW(counter,Format,ref size,out _,0)!=MoreData||size==0||size>16*1024*1024)return null;
            nint buffer=Marshal.AllocHGlobal((int)size);
            try {
                uint capacity=size,status=PdhGetFormattedCounterArrayW(counter,Format,ref size,out uint count,buffer);
                if(status==MoreData)continue; // Re-probe from zero: returned size is not reliable here.
                int stride=Marshal.SizeOf<CounterItem>();if(status!=0||(ulong)count*(uint)stride>capacity)return null;
                var values=new List<(string,double)>();
                for(int i=0;i<count;i++) {
                    var item=Marshal.PtrToStructure<CounterItem>(buffer+i*stride);
                    if(!Valid(item.Value))continue;
                    string? name=Marshal.PtrToStringUni(item.Name);
                    if(name!=null&&!name.Equals("_Total",StringComparison.OrdinalIgnoreCase))values.Add((name,item.Value.Value));
                }
                return values;
            } finally {Marshal.FreeHGlobal(buffer);}
        }
        return null;
    }
    private (double? Receive,double? Send) NetworkRates()
    {
        try {
            long now=Stopwatch.GetTimestamp();var next=new Dictionary<string,(long Received,long Sent,long Tick)>();
            double received=0,sent=0;bool measured=false,failed=false;
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                if(adapter.OperationalStatus!=OperationalStatus.Up||adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)continue;
                try {
                    var stats=adapter.GetIPStatistics();long rx=stats.BytesReceived,tx=stats.BytesSent;next[adapter.Id]=(rx,tx,now);
                    if(network.TryGetValue(adapter.Id,out var before)&&now>before.Tick&&rx>=before.Received&&tx>=before.Sent) {
                        double seconds=(now-before.Tick)/(double)Stopwatch.Frequency;
                        received+=(rx-before.Received)/seconds;sent+=(tx-before.Sent)/seconds;measured=true;
                    }
                } catch(NetworkInformationException) {failed=true;}
            }
            network=next;
            return failed||(!measured&&next.Count>0)?(null,null):(received,sent);
        } catch(NetworkInformationException) {network.Clear();return(null,null);}
    }
    private void CloseQuery() {if(query!=0)PdhCloseQuery(query);query=cpu=gpu=vram=diskRead=diskWrite=diskIdle=diskReadLatency=diskWriteLatency=0;}
    public void Dispose() {lock(gate) {disposed=true;CloseQuery();network.Clear();hardware?.Dispose();hardware=null;}}
}
