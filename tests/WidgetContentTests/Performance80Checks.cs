using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaWall.Contracts;
using LumaWall.WidgetHost;
internal static class Performance80Checks
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowLong(nint window,int index);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    internal static void Run(Action<bool,string> check)
    {
        check(PerformanceWidgetContent.FormatMetric(PerformanceMetrics.CpuTemperature,64.5).Contains("64.5")&&PerformanceWidgetContent.FormatMetric(PerformanceMetrics.GpuPower,123.4).EndsWith(" W"),"Sensor units distinguish temperature and watts");
        check(PerformanceWidgetContent.FormatMetric(PerformanceMetrics.CpuClock,4321).Contains("4.32 GHz"),"Clock units preserve useful precision");
        check(PerformanceWidgetContent.FormatMetric(PerformanceMetrics.DiskReadLatency,.35).Contains("0.35 ms"),"Disk latency is not formatted as throughput");
        check(!PerformanceWidgetContent.FormatMetric(PerformanceMetrics.MemoryAvailable,1073741824).Contains("/s"),"Available memory is a capacity");
        check(PerformanceWidgetContent.FormatMetric(PerformanceMetrics.CpuPower,null)=="—","Missing sensors are never fabricated as zero");
        using(var sampler=new SystemPerformanceSampler()) {
            var ordinary=sampler.Sample(PerformanceMetrics.MemoryAvailable|PerformanceMetrics.MemoryCommitted|PerformanceMetrics.ProcessCount|PerformanceMetrics.ThreadCount|PerformanceMetrics.HandleCount|PerformanceMetrics.SystemUptime)!;
            check(ordinary.Get(PerformanceMetrics.MemoryAvailable)>0&&ordinary.Get(PerformanceMetrics.ProcessCount)>0&&ordinary.Get(PerformanceMetrics.SystemUptime)>0,"Read-only Windows details return real system values");
            check(ordinary.Get(PerformanceMetrics.CpuTemperature)==null,"Unselected hardware sensors are not sampled");
            var watch=Stopwatch.StartNew();var hardware=sampler.Sample(HardwarePerformanceSampler.Fields);watch.Stop();
            check(watch.ElapsedMilliseconds<1000,"Driver sensor initialization never blocks basic sampling");
            for(int i=0;i<15;i++){Thread.Sleep(100);hardware=sampler.Sample(HardwarePerformanceSampler.Fields);if(hardware?.Get(PerformanceMetrics.CpuClock)>0)break;}
            check(hardware?.Get(PerformanceMetrics.CpuClock)>0,"CPU frequency reads existing Windows power information");
            Console.WriteLine("Existing-interface sensor availability: "+string.Join(", ",PerformanceFields.Selected(HardwarePerformanceSampler.Fields).Select(f=>$"{f}={hardware?.Get(f)?.ToString("0.##")??"unavailable"}")));
            sampler.Sample(PerformanceFields.Default); // Releases optional providers when deselected.
        }
        var widget=new WidgetInstance {Id="hidden-topmost-test",Kind=WidgetKind.Text,AlwaysOnTop=true};var state=new WidgetConfiguration {Widgets=[widget]};
        var window=new WidgetWindow(widget,state,_=>{});
        try {
            var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            check(handle!=0&&GetForegroundWindow()!=handle,"Creating a hidden widget leaves it inactive");
            window.LowerToDesktop();
            check(!window.IsVisible&&window.Topmost&&(GetWindowLong(handle,-20).ToInt64()&8)!=0&&GetWindowLong(handle,-8)==0,"Owned hidden widget uses native topmost with no desktop owner");
            window.Apply(widget with {AlwaysOnTop=false},state);window.LowerToDesktop();
            check(!window.Topmost&&(GetWindowLong(handle,-20).ToInt64()&8)==0,"Disabling topmost restores ordinary desktop band");
            window.Apply(widget,state);window.LowerToDesktop();
            check(window.Topmost&&GetForegroundWindow()!=handle,"Restacking topmost never steals foreground activation");
        }finally{window.CloseHost();}
    }
}
