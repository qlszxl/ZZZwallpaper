using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LumaWall.Lifecycle;

namespace ZZZwallpaper.InstallerPreview;

internal sealed class UninstallProcesses : IDisposable
{
    private sealed record OwnedProcess(Process Process, string Name);
    internal sealed record InspectionFailure(int ProcessId, string Name, int ErrorCode)
    {
        internal InstallerException Exception(bool closing = false) => new(
            $"{(closing ? "后台进程仍未能关闭" : "仍无法确认后台进程已退出")}（{Name}，PID {ProcessId}，错误 {ErrorCode}）。请手动退出该程序后重试。");
    }
    private readonly string target;
    private readonly InstallationLifetime lifetime;
    private readonly Mutex guard;
    private readonly bool forInstallation;
    private readonly Func<Process, string> inspectImage;
    internal string GuardName { get; }
    private static readonly string[] Names = ["ZZZwallpaper", "ZZZwallpaper.Launcher", "ZZZwallpaper.WidgetHost",
        "ZZZwallpaper.Mcp", "WallpaperHost", "AudioSpectrumHost", "TaskbarHost", "RestartAgent", "createdump", "ffmpeg", "ffprobe"];
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nint OpenProcess(uint access,bool inherit,int id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(nint process,int flags,StringBuilder name,ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    internal static string ImagePath(Process process)
    {
        // Querying the executable needs less access than enumerating MainModule,
        // and remains available while a process is still starting or shutting down.
        var handle=OpenProcess(0x1000,false,process.Id);
        if(handle==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            int size=32768;var name=new StringBuilder(size);
            if(!QueryFullProcessImageName(handle,0,name,ref size))throw new Win32Exception(Marshal.GetLastWin32Error());
            return name.ToString();
        }finally {CloseHandle(handle);}
    }

    internal UninstallProcesses(string directory, bool forInstallation = false, Func<Process, string>? inspectImage = null)
    {
        this.forInstallation = forInstallation;
        this.inspectImage = inspectImage ?? ImagePath;
        target = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        GuardName = InstallationLifetime.SignalName(directory) + ".Guard";
        guard = new Mutex(false, GuardName, out var created);
        if (!created) { guard.Dispose(); throw new InstallerException("安装或卸载正在进行，请等待完成。"); }
        try { lifetime = new InstallationLifetime(directory); lifetime.RequestStop(); }
        catch { guard.Dispose(); throw; }
    }

    private List<OwnedProcess> FindOwned(out List<InspectionFailure> unresolved)
    {
        var found = new List<OwnedProcess>();
        unresolved = [];
        using var current = Process.GetCurrentProcess();
        try
        {
            foreach (var name in Names)
                foreach (var process in Process.GetProcessesByName(name))
                {
                    var owned = false;
                    try
                    {
                        if (process.HasExited || process.SessionId != current.SessionId) continue;
                        var image = inspectImage(process);
                        owned = Path.GetFullPath(image).StartsWith(target, StringComparison.OrdinalIgnoreCase);
                        if (owned) found.Add(new(process, name));
                    }
                    catch (InvalidOperationException) { /* Exited during inspection. */ }
                    catch (Win32Exception error)
                    {
                        // Shutdown can invalidate a process between enumeration,
                        // HasExited and the image query, including ERROR_ACCESS_DENIED.
                        // Do not query it again in this catch or count it as gone.
                        unresolved.Add(new(process.Id, name, error.NativeErrorCode));
                    }
                    finally { if (!owned) process.Dispose(); }
                }
            return found;
        }
        catch { foreach (var process in found) process.Process.Dispose(); throw; }
    }

    internal async Task StopAsync(TimeSpan? gracefulTimeout = null,IProgress<string>? progress=null,CancellationToken cancellationToken=default)
    {
        var timer = Stopwatch.StartNew();
        var graceful = gracefulTimeout ?? TimeSpan.FromSeconds(3);
        var emptyScans = 0;
        List<InspectionFailure> unresolved = [], closeFailures = [];
        while (timer.Elapsed < graceful + TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = FindOwned(out unresolved);
            closeFailures.Clear();
            try
            {
                if (processes.Count == 0 && unresolved.Count == 0) { if (++emptyScans >= 3) return; }
                else emptyScans = 0;
                // During installation, allow the main app to save and exit normally.
                // Only orphaned components may be terminated after the grace period.
                if (timer.Elapsed >= graceful && unresolved.Count == 0 && (!forInstallation || !processes.Any(p => p.Name.Equals("ZZZwallpaper",StringComparison.OrdinalIgnoreCase)))) {
                    if(processes.Count>0)progress?.Report("正在关闭后台组件");
                    foreach (var owned in processes)
                        try
                        {
                            // Only the verified executable in this installation. Never kill
                            // arbitrary descendants or an unrelated copy with the same name.
                            if (!owned.Process.HasExited) owned.Process.Kill(entireProcessTree: false);
                        }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception error) { closeFailures.Add(new(owned.Process.Id, owned.Name, error.NativeErrorCode)); }
                }
            }
            finally { foreach (var process in processes) process.Process.Dispose(); }
            await Task.Delay(100,cancellationToken);
        }
        if (unresolved.Count != 0) throw unresolved[0].Exception();
        if (closeFailures.Count != 0) throw closeFailures[0].Exception(closing: true);
        throw new InstallerException(forInstallation ? "程序未能退出，尚未开始安装。请手动退出软件后重试。" : "后台进程未能自动退出，尚未删除程序文件。请稍后重试卸载。");
    }

    public void Dispose() { lifetime.Reset(); lifetime.Dispose(); guard.Dispose(); }
}
