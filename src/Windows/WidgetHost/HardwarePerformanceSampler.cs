using System.IO;
using System.Runtime.InteropServices;
using LumaWall.Contracts;

namespace LumaWall.WidgetHost;

// Optional read-only calls into drivers already installed by the user. No driver,
// service, elevated helper, polling process, or management setting is created.
internal sealed class HardwarePerformanceSampler : IDisposable
{
    internal const PerformanceMetrics Fields=PerformanceMetrics.CpuClock|PerformanceMetrics.CpuTemperature|PerformanceMetrics.CpuPower|
        PerformanceMetrics.GpuTemperature|PerformanceMetrics.GpuPower|PerformanceMetrics.GpuClock|PerformanceMetrics.GpuMemoryClock|PerformanceMetrics.GpuFan|PerformanceMetrics.GpuMemoryTotal;
    private readonly object gate=new();
    private bool pending,disposed;
    private long nextRead,completedAt;
    private IReadOnlyDictionary<PerformanceMetrics,double> snapshot=new Dictionary<PerformanceMetrics,double>();
    private NvidiaSensors? nvidia;private AmdSensors? amd;
    internal IReadOnlyDictionary<PerformanceMetrics,double> Sample(PerformanceMetrics fields)
    {
        lock(gate) {
            long now=Environment.TickCount64;
            if(!disposed&&!pending&&now>=nextRead) {pending=true;nextRead=now+1000;_=Task.Run(()=>Read(fields));}
            return completedAt!=0&&now-completedAt<=5000?snapshot:new Dictionary<PerformanceMetrics,double>();
        }
    }
    private void Read(PerformanceMetrics fields)
    {
        var values=new Dictionary<PerformanceMetrics,double>();
        try {
            if((fields&PerformanceMetrics.CpuClock)!=0&&CpuFrequency() is { } frequency)values[PerformanceMetrics.CpuClock]=frequency;
            if((fields&~PerformanceMetrics.CpuClock)!=0) {
                nvidia??=new();amd??=new();nvidia.Read(fields,values);amd.Read(fields,values);
            }
        } catch(Exception error) when(error is ExternalException or ArgumentException or InvalidOperationException or IOException) { /* Unsupported driver: leave missing readings unavailable. */ }
        finally {
            lock(gate) {
                snapshot=values;completedAt=Environment.TickCount64;pending=false;
                if(disposed)Release();
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessorPower {public uint Number,MaxMhz,CurrentMhz,MhzLimit,MaxIdle,CurrentIdle;}
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level,nint input,uint inputSize,[Out] ProcessorPower[] output,uint outputSize);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    private static double? CpuFrequency()
    {
        uint count=GetActiveProcessorCount(0xffff);if(count is 0 or >4096)return null;
        var cores=new ProcessorPower[count];
        if(CallNtPowerInformation(11,0,0,cores,(uint)(cores.Length*Marshal.SizeOf<ProcessorPower>()))!=0)return null;
        var measured=cores.Where(c=>c.CurrentMhz is >0 and <30000).ToArray();
        return measured.Length>0?measured.Average(c=>(double)c.CurrentMhz):null;
    }
    internal static void Add(Dictionary<PerformanceMetrics,double> target,PerformanceMetrics field,double? reading,bool sum=false)
    {
        if(reading is not double value||!double.IsFinite(value)||value<0)return;
        target[field]=target.TryGetValue(field,out double previous)?sum?previous+value:Math.Max(previous,value):value;
    }
    internal static T? Export<T>(nint library,string name) where T:Delegate=>library!=0&&NativeLibrary.TryGetExport(library,name,out var address)?Marshal.GetDelegateForFunctionPointer<T>(address):null;
    internal static nint Load(string name)
    {
        string path=Path.Combine(Environment.SystemDirectory,name);
        try {return File.Exists(path)&&NativeLibrary.TryLoad(path,out var library)?library:0;}catch(BadImageFormatException){return 0;}
    }
    private void Release(){nvidia?.Dispose();amd?.Dispose();nvidia=null;amd=null;}
    public void Dispose(){lock(gate){disposed=true;snapshot=new Dictionary<PerformanceMetrics,double>();if(!pending)Release();}}
}

internal sealed class NvidiaSensors : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Simple();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle(uint index,out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Value(nint device,out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TypedValue(nint device,int type,out uint value);
    [StructLayout(LayoutKind.Sequential)] private struct Memory {public ulong Total,Free,Used;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryValue(nint device,out Memory value);
    private nint library;
    private readonly Simple? shutdown;private readonly nint[] devices=[];
    private readonly Value? power,fan;private readonly TypedValue? temperature,clock;private readonly MemoryValue? memory;
    internal NvidiaSensors()
    {
        library=HardwarePerformanceSampler.Load("nvml.dll");
        var init=Get<Simple>("nvmlInit_v2");var stop=Get<Simple>("nvmlShutdown");
        if(init==null||stop==null||init()!=0)return;shutdown=stop;
        var count=Get<Count>("nvmlDeviceGetCount_v2");var handle=Get<Handle>("nvmlDeviceGetHandleByIndex_v2");
        if(count==null||handle==null||count(out uint total)!=0||total>64)return;
        var found=new List<nint>();for(uint index=0;index<total;index++)if(handle(index,out var device)==0&&device!=0)found.Add(device);devices=found.ToArray();
        power=Get<Value>("nvmlDeviceGetPowerUsage");fan=Get<Value>("nvmlDeviceGetFanSpeed");temperature=Get<TypedValue>("nvmlDeviceGetTemperature");
        clock=Get<TypedValue>("nvmlDeviceGetClockInfo");memory=Get<MemoryValue>("nvmlDeviceGetMemoryInfo");
    }
    private T? Get<T>(string name) where T:Delegate=>HardwarePerformanceSampler.Export<T>(library,name);
    internal void Read(PerformanceMetrics fields,Dictionary<PerformanceMetrics,double> result)
    {
        foreach(var device in devices) {
            void Add(PerformanceMetrics field,double? value,bool sum=false){if((fields&field)!=0)HardwarePerformanceSampler.Add(result,field,value,sum);}
            if((fields&PerformanceMetrics.GpuTemperature)!=0&&temperature?.Invoke(device,0,out uint temp)==0&&temp<=200)Add(PerformanceMetrics.GpuTemperature,temp);
            if((fields&PerformanceMetrics.GpuPower)!=0&&power?.Invoke(device,out uint watts)==0&&watts<=3000000)Add(PerformanceMetrics.GpuPower,watts/1000d,true);
            if((fields&PerformanceMetrics.GpuFan)!=0&&fan?.Invoke(device,out uint speed)==0&&speed<=200)Add(PerformanceMetrics.GpuFan,speed);
            if((fields&PerformanceMetrics.GpuClock)!=0&&clock?.Invoke(device,0,out uint gpuClock)==0&&gpuClock<=30000)Add(PerformanceMetrics.GpuClock,gpuClock);
            if((fields&PerformanceMetrics.GpuMemoryClock)!=0&&clock?.Invoke(device,2,out uint memoryClock)==0&&memoryClock<=100000)Add(PerformanceMetrics.GpuMemoryClock,memoryClock);
            if((fields&PerformanceMetrics.GpuMemoryTotal)!=0&&memory?.Invoke(device,out Memory vram)==0&&vram.Total>0)Add(PerformanceMetrics.GpuMemoryTotal,vram.Total,true);
        }
    }
    public void Dispose(){if(library==0)return;shutdown?.Invoke();NativeLibrary.Free(library);library=0;}
}

internal sealed class AmdSensors : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Allocate(int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(Allocate allocate,int connected,out nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint context,out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Adapters(nint context,nint buffer,int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Query(nint context,int adapter,ref LogData data);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] private struct Adapter {
        public int Size,Index;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Udid;
        public int Bus,Device,Function,Vendor;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Display;public int Present,Exist;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Driver;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string DriverExt;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Pnp;public int OsDisplay;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Sensor {public int Supported,Value;}
    [StructLayout(LayoutKind.Sequential)] private struct LogData {public int Size;[MarshalAs(UnmanagedType.ByValArray,SizeConst=256)]public Sensor[] Sensors;}
    private nint library,context;private readonly Allocate allocate=size=>size is >0 and <=16777216?Marshal.AllocHGlobal(size):0;
    private readonly Destroy? destroy;private readonly Query? query;private readonly int[] adapters=[];
    internal AmdSensors()
    {
        library=HardwarePerformanceSampler.Load("atiadlxx.dll");var create=Get<Create>("ADL2_Main_Control_Create");destroy=Get<Destroy>("ADL2_Main_Control_Destroy");
        var count=Get<Count>("ADL2_Adapter_NumberOfAdapters_Get");var info=Get<Adapters>("ADL2_Adapter_AdapterInfo_Get");query=Get<Query>("ADL2_New_QueryPMLogData_Get");
        if(create==null||destroy==null||count==null||info==null||query==null||create(allocate,1,out context)!=0||context==0)return;
        if(count(context,out int total)!=0||total is <1 or >64)return;
        int stride=Marshal.SizeOf<Adapter>(),size=stride*total;var buffer=Marshal.AllocHGlobal(size);
        try {
            Marshal.Copy(new byte[size],0,buffer,size);for(int i=0;i<total;i++)Marshal.WriteInt32(buffer+i*stride,stride);
            if(info(context,buffer,size)!=0)return;
            var found=new Dictionary<(int,int,int),int>();
            for(int i=0;i<total;i++){var adapter=Marshal.PtrToStructure<Adapter>(buffer+i*stride);if(adapter.Exist!=0&&adapter.Vendor is 1002 or 0x1002)found.TryAdd((adapter.Bus,adapter.Device,adapter.Function),adapter.Index);}
            adapters=found.Values.ToArray();
        }finally{Marshal.FreeHGlobal(buffer);}
    }
    private T? Get<T>(string name) where T:Delegate=>HardwarePerformanceSampler.Export<T>(library,name);
    internal void Read(PerformanceMetrics fields,Dictionary<PerformanceMetrics,double> result)
    {
        foreach(int adapter in adapters) {
            var data=new LogData {Size=Marshal.SizeOf<LogData>(),Sensors=new Sensor[256]};if(query?.Invoke(context,adapter,ref data)!=0)continue;
            void Add(PerformanceMetrics field,int sensor,double max,bool sum=false){var value=data.Sensors[sensor];if((fields&field)!=0&&value.Supported!=0&&value.Value>=0&&value.Value<=max)HardwarePerformanceSampler.Add(result,field,value.Value,sum);}
            Add(PerformanceMetrics.GpuTemperature,8,200);Add(PerformanceMetrics.GpuClock,1,30000);Add(PerformanceMetrics.GpuMemoryClock,2,100000);Add(PerformanceMetrics.GpuFan,15,200);
            // Board power has the same meaning as NVML total-board power. Do not
            // silently substitute ASIC-only power for an unsupported board sensor.
            Add(PerformanceMetrics.GpuPower,73,3000,true);Add(PerformanceMetrics.CpuTemperature,32,200);Add(PerformanceMetrics.CpuPower,33,3000);
        }
    }
    public void Dispose(){if(context!=0){destroy?.Invoke(context);context=0;}if(library!=0){NativeLibrary.Free(library);library=0;}GC.KeepAlive(allocate);}
}
