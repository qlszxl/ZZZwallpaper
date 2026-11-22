using LumaWall.Contracts;
using System.Diagnostics;

namespace LumaWall.ControlCenter;

// The MCP bridge is started by the external client, not by the control center.
// Include only the bridge from this installation and Windows session.
internal sealed class OwnedAuxiliaryProcesses
{
    private readonly object gate=new();
    private readonly string bridgePath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"Mcp","ZZZwallpaper.Mcp.exe"));
    private readonly int sessionId=GetSessionId();
    private long checkedAt=long.MinValue;
    private PerformanceProcessTarget[] cached=[];
    private static int GetSessionId() {using var current=Process.GetCurrentProcess();return current.SessionId;}
    internal static bool IsOwned(string? candidate,int candidateSession,string expected,int session)=>
        candidateSession==session&&string.Equals(candidate,expected,StringComparison.OrdinalIgnoreCase);
    internal PerformanceProcessTarget[] Include(IEnumerable<PerformanceProcessTarget> known)
    {
        lock(gate) {
            long now=Environment.TickCount64;
            if(checkedAt==long.MinValue||now-checkedAt>=3000) {
                var found=new List<PerformanceProcessTarget>();
                foreach(var process in Process.GetProcessesByName("ZZZwallpaper.Mcp")) {
                    using(process) {
                        try {
                            if(!process.HasExited&&IsOwned(process.MainModule?.FileName,process.SessionId,bridgePath,sessionId))
                                found.Add(new(process.Id,"mcp:"+process.Id,UiLanguage.Text("MCP 桥接"),""));
                        } catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                    }
                }
                cached=found.ToArray();checkedAt=now;
            }
            // Revalidate cached identities before sampling: an exited bridge's
            // PID may have been reused by an unrelated process between scans.
            return known.Concat(cached.Where(StillOwned)).DistinctBy(p=>p.ProcessId).ToArray();
        }
    }
    private bool StillOwned(PerformanceProcessTarget target)
    {
        try {
            using var process=Process.GetProcessById(target.ProcessId);
            return !process.HasExited&&IsOwned(process.MainModule?.FileName,process.SessionId,bridgePath,sessionId);
        } catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) {return false;}
    }
}
