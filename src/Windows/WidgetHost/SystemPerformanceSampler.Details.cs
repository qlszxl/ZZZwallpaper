using System.Runtime.InteropServices;
using LumaWall.Contracts;
namespace LumaWall.WidgetHost;
internal sealed partial class SystemPerformanceSampler
{
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInformation {
        public uint Size;public nuint CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;
        public uint HandleCount,ProcessCount,ThreadCount;
    }
    [DllImport("psapi.dll")] private static extern bool GetPerformanceInfo(ref PerformanceInformation info,uint size);
    private static void ReadSystemDetails(PerformanceMetrics fields,Dictionary<PerformanceMetrics,double> values)
    {
        if((fields&PerformanceMetrics.SystemUptime)!=0)values[PerformanceMetrics.SystemUptime]=Environment.TickCount64/1000d;
        const PerformanceMetrics infoFields=PerformanceMetrics.MemoryCommitted|PerformanceMetrics.ProcessCount|PerformanceMetrics.ThreadCount|PerformanceMetrics.HandleCount;
        if((fields&infoFields)==0)return;
        uint size=(uint)Marshal.SizeOf<PerformanceInformation>();var info=new PerformanceInformation {Size=size};if(!GetPerformanceInfo(ref info,size))return;
        void Add(PerformanceMetrics field,double value){if((fields&field)!=0)values[field]=value;}
        Add(PerformanceMetrics.MemoryCommitted,(double)info.CommitTotal*info.PageSize);Add(PerformanceMetrics.ProcessCount,info.ProcessCount);
        Add(PerformanceMetrics.ThreadCount,info.ThreadCount);Add(PerformanceMetrics.HandleCount,info.HandleCount);
    }
}
