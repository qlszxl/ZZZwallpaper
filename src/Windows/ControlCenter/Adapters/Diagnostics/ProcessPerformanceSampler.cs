using LumaWall.Contracts;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumaWall.ControlCenter;

public sealed record PerformanceProcessTarget(int ProcessId, string Key, string Name, string Detail);

/// <summary>
/// CPU is normalized to the capacity of all logical processors. Read/write rates are
/// process I/O counters (including cached files and other devices), not physical disk usage.
/// A null value means that the counter could not be read or has no previous sample.
/// </summary>
public sealed record ProcessPerformanceSample(
    int ProcessId,
    string Key,
    string Name,
    string Detail,
    double? CpuPercent,
    long? WorkingSetBytes,
    long? PrivateBytes,
    double? ReadBytesPerSecond,
    double? WriteBytesPerSecond,
    int? ThreadCount,
    int? HandleCount,
    string? Error)
{
    public double? GpuPercent {get;init;}
    public long? DedicatedGpuBytes {get;init;}
    public long? SharedGpuBytes {get;init;}
}

/// <summary>
/// Samples only the explicitly supplied application process IDs. Serialize Sample and
/// Dispose calls (they need not use the same physical thread); dispose and replace the
/// sampler when monitoring stops. Process handles live only for one sample.
/// </summary>
public sealed class ProcessPerformanceSampler : IDisposable
{
    private readonly Dictionary<int, PreviousCounters> previous = new();
    private readonly ProcessGpuSampler gpu = new();
    private bool disposed;
    public ProcessGpuUsage GpuTotals {get;private set;}=new(null,null,null);

    public double? SampleGpu(IReadOnlyList<PerformanceProcessTarget> targets) => disposed ? null : gpu.Sample(targets);

    public IReadOnlyList<ProcessPerformanceSample> Sample(IReadOnlyList<PerformanceProcessTarget> targets)
    {
        if (disposed) return Array.Empty<ProcessPerformanceSample>();

        var activeIds = targets.Select(target => target.ProcessId).ToHashSet();
        foreach (var processId in previous.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            previous.Remove(processId);

        // A process can represent more than one UI item. Read its counters only once so
        // duplicate targets cannot consume each other's rate baseline.
        var measured = new Dictionary<int, ProcessPerformanceSample>();
        var result = new List<ProcessPerformanceSample>(targets.Count);
        foreach (var target in targets)
        {
            if (!measured.TryGetValue(target.ProcessId, out var sample))
            {
                sample = Measure(target);
                measured.Add(target.ProcessId, sample);
            }
            result.Add(sample with { Key = target.Key, Name = target.Name, Detail = target.Detail });
        }
        var graphics=gpu.SampleResources(targets);
        // Exited or unreadable workers must not silently turn into zero totals.
        GpuTotals=result.All(sample=>sample.WorkingSetBytes.HasValue)?graphics.Total:new(null,null,null);
        return result.Select(sample=>sample.WorkingSetBytes.HasValue&&graphics.Processes.TryGetValue(sample.ProcessId,out var usage)
            ?sample with {GpuPercent=usage.Percent,DedicatedGpuBytes=usage.DedicatedBytes,SharedGpuBytes=usage.SharedBytes}:sample).ToArray();
    }

    private ProcessPerformanceSample Measure(PerformanceProcessTarget target)
    {
        if (target.ProcessId <= 0)
            return Unavailable(target, UiLanguage.Text("进程尚未启动"));

        try
        {
            using var process = Process.GetProcessById(target.ProcessId);
            // Keep one OS process identity open for the complete sample. The Process
            // owns this handle and releases it when the using scope ends.
            var processHandle = process.SafeHandle;
            // Establish identity before accepting any deltas: Windows can reuse a PID
            // after a wallpaper host closes and a new host starts.
            var startTime = process.StartTime.ToUniversalTime().Ticks;
            if (!previous.TryGetValue(target.ProcessId, out var old) || old.StartTime != startTime)
                old = new PreviousCounters(startTime, null, null);

            var errors = new List<string>();
            CpuCounter? cpuCounter = null;
            IoCounter? ioCounter = null;
            double? cpuPercent = null;
            double? readRate = null;
            double? writeRate = null;

            try
            {
                var cpuTicks = process.TotalProcessorTime.Ticks;
                var now = Stopwatch.GetTimestamp();
                cpuCounter = new CpuCounter(cpuTicks, now);
                if (old.Cpu is { } oldCpu && cpuTicks >= oldCpu.Ticks)
                {
                    var seconds = ElapsedSeconds(oldCpu.Timestamp, now);
                    if (seconds > 0)
                        cpuPercent = Math.Clamp(
                            (cpuTicks - oldCpu.Ticks) / (double)TimeSpan.TicksPerSecond
                            / seconds / Math.Max(1, Environment.ProcessorCount) * 100, 0, 100);
                }
            }
            catch (Exception error) when (IsCounterFailure(error))
            {
                errors.Add(UiLanguage.Text("CPU 暂不可读"));
            }

            var workingSet = ReadCounter(() => process.WorkingSet64, UiLanguage.Text("内存"), errors);
            var privateBytes = ReadCounter(() => process.PrivateMemorySize64, UiLanguage.Text("专用内存"), errors);
            var threadCount = ReadCounter(() => process.Threads.Count, UiLanguage.Text("线程"), errors);
            var handleCount = ReadCounter(() => process.HandleCount, UiLanguage.Text("句柄"), errors);

            try
            {
                if (GetProcessIoCounters(processHandle, out var io))
                {
                    var now = Stopwatch.GetTimestamp();
                    ioCounter = new IoCounter(io.ReadTransferCount, io.WriteTransferCount, now);
                    if (old.Io is { } oldIo)
                    {
                        var seconds = ElapsedSeconds(oldIo.Timestamp, now);
                        if (seconds > 0)
                        {
                            if (io.ReadTransferCount >= oldIo.ReadBytes)
                                readRate = (io.ReadTransferCount - oldIo.ReadBytes) / seconds;
                            if (io.WriteTransferCount >= oldIo.WriteBytes)
                                writeRate = (io.WriteTransferCount - oldIo.WriteBytes) / seconds;
                        }
                    }
                }
                else
                {
                    errors.Add(UiLanguage.Text("I/O 暂不可读"));
                }
            }
            catch (Exception error) when (IsCounterFailure(error))
            {
                errors.Add(UiLanguage.Text("I/O 暂不可读"));
            }

            // A failed counter loses its baseline. Its next successful read establishes
            // a fresh baseline instead of presenting a rate averaged across the outage.
            previous[target.ProcessId] = new PreviousCounters(startTime, cpuCounter, ioCounter);
            return new ProcessPerformanceSample(target.ProcessId, target.Key, target.Name, target.Detail,
                cpuPercent, workingSet, privateBytes, readRate, writeRate, threadCount, handleCount,
                errors.Count == 0 ? null : string.Join("；", errors));
        }
        catch (Exception error) when (IsCounterFailure(error) || error is ArgumentException)
        {
            previous.Remove(target.ProcessId);
            return Unavailable(target, error switch
            {
                UnauthorizedAccessException => UiLanguage.Text("无权读取此进程"),
                Win32Exception { NativeErrorCode: 5 } => UiLanguage.Text("无权读取此进程"),
                ArgumentException or InvalidOperationException => UiLanguage.Text("进程已退出或尚未启动"),
                _ => UiLanguage.Text("进程信息暂不可读")
            });
        }
    }

    private static T? ReadCounter<T>(Func<T> read, string name, List<string> errors) where T : struct
    {
        try { return read(); }
        catch (Exception error) when (IsCounterFailure(error))
        {
            errors.Add(UiLanguage.Format($"{name}暂不可读"));
            return null;
        }
    }

    private static bool IsCounterFailure(Exception error) =>
        error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;

    private static double ElapsedSeconds(long before, long after) =>
        (after - before) / (double)Stopwatch.Frequency;

    private static ProcessPerformanceSample Unavailable(PerformanceProcessTarget target, string error) =>
        new(target.ProcessId, target.Key, target.Name, target.Detail,
            null, null, null, null, null, null, null, error);

    public void Dispose()
    {
        disposed = true;
        gpu.Dispose();
        previous.Clear();
    }

    private sealed record PreviousCounters(long StartTime, CpuCounter? Cpu, IoCounter? Io);
    private readonly record struct CpuCounter(long Ticks, long Timestamp);
    private readonly record struct IoCounter(ulong ReadBytes, ulong WriteBytes, long Timestamp);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
}
