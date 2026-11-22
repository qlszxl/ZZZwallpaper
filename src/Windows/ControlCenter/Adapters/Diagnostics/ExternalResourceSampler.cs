using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumaWall.ControlCenter;

internal sealed record ExternalResourceSample(double? Cpu,double? Gpu);

// One serialized background worker owns this sampler. Never dispose during Sample.
internal sealed class ExternalResourceSampler : IDisposable
{
    private readonly ProcessGpuSampler gpu=new(enginesOnly:true);
    private readonly Dictionary<int,(DateTime Started,long Cpu)> previousProcesses=[];
    private (ulong Idle,ulong Total)? previousSystem;

    internal ExternalResourceSample Sample(IReadOnlyList<PerformanceProcessTarget> ownProcesses)=>
        new(SampleCpu(ownProcesses),gpu.SampleResources(ownProcesses).ExternalPercent);

    private double? SampleCpu(IReadOnlyList<PerformanceProcessTarget> ownProcesses)
    {
        if(!GetSystemTimes(out var idle,out var kernel,out var user)) {
            previousSystem=null;previousProcesses.Clear();return null;
        }
        // Windows kernel time includes idle; kernel + user is the whole machine's capacity.
        ulong total=kernel.Ticks+user.Ticks;
        var previous=previousSystem;previousSystem=(idle.Ticks,total);
        long ownTicks=0;bool complete=true;
        var ids=ownProcesses.Where(t=>t.ProcessId>0).Select(t=>t.ProcessId).ToHashSet();
        foreach(int old in previousProcesses.Keys.Where(id=>!ids.Contains(id)).ToArray())previousProcesses.Remove(old);
        foreach(int id in ids) {
            try {
                using var process=Process.GetProcessById(id);
                var started=process.StartTime.ToUniversalTime();long ticks=process.TotalProcessorTime.Ticks;
                if(previousProcesses.TryGetValue(id,out var before)&&before.Started==started&&ticks>=before.Cpu)
                    ownTicks+=ticks-before.Cpu;
                else complete=false; // Baseline a new process before judging external load.
                previousProcesses[id]=(started,ticks);
            }catch(ArgumentException) {previousProcesses.Remove(id);}
            catch(InvalidOperationException) {previousProcesses.Remove(id);}
            catch(Win32Exception) {previousProcesses.Remove(id);complete=false;}
        }
        if(!complete||previous is not { } last||total<=last.Total||idle.Ticks<last.Idle)return null;
        ulong capacity=total-last.Total,unused=idle.Ticks-last.Idle;
        if(unused>capacity)return null;
        return Math.Clamp(((double)(capacity-unused)-ownTicks)/capacity*100,0,100);
    }
    public void Dispose() {gpu.Dispose();previousProcesses.Clear();previousSystem=null;}
    [StructLayout(LayoutKind.Sequential)] private struct FileTime {
        public uint Low,High;
        public readonly ulong Ticks=>((ulong)High<<32)|Low;
    }
    [DllImport("kernel32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
}
