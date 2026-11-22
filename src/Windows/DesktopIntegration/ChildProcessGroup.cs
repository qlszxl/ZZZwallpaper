using LumaWall.Contracts;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

public sealed class ChildProcessGroup : IDisposable
{
    private nint job;
    [StructLayout(LayoutKind.Sequential)] private struct Limits {public long ProcessTime,JobTime;public uint Flags;public nuint MinWorkingSet,MaxWorkingSet;public uint ActiveProcesses;public nuint Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct Extended {public Limits Basic;public Io Io;public nuint ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern nint CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(nint job,int info,ref Extended value,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(nint job,nint process);
    public ChildProcessGroup()
    {
        job=CreateJobObject(0,null);var limits=new Extended {Basic=new Limits {Flags=0x2000}};
        if(job==0||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Extended>()))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Add(System.Diagnostics.Process process)
    {
        if(!AssignProcessToJobObject(job,process.Handle)) {try {process.Kill(true);}catch {}throw new Win32Exception(Marshal.GetLastWin32Error(),UiLanguage.Text("无法隔离壁纸子进程"));}
    }
    public void Dispose() {if(job!=0) {Native.CloseHandle(job);job=0;}}
}
