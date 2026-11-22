using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ZZZwallpaper.InstallerPreview;

// Holds per-installation stop signals through the engine's entire lifetime so
// an AI client or launcher cannot reopen a process while files are replaced.
internal sealed class InstallationProcesses : IDisposable
{
    private readonly List<UninstallProcesses> stopping = [];
    private static readonly string[] Names = ["ZZZwallpaper", "ZZZwallpaper.Launcher", "ZZZwallpaper.Mcp",
        "ZZZwallpaper.WidgetHost", "WallpaperHost", "AudioSpectrumHost", "TaskbarHost"];

    internal static string? ApplicationDirectory(string image)
    {
        var executable = Path.GetFileNameWithoutExtension(image);
        if (!Names.Contains(executable, StringComparer.OrdinalIgnoreCase)) return null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(image))!;
        if ((executable.Equals("ZZZwallpaper.Mcp", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).Equals("Mcp", StringComparison.OrdinalIgnoreCase)) ||
            (executable.Equals("ZZZwallpaper.WidgetHost", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).Equals("Widgets", StringComparison.OrdinalIgnoreCase)))
            directory = Path.GetDirectoryName(directory)!;
        // A same-name executable elsewhere is not a ZZZwallpaper installation.
        return File.Exists(Path.Combine(directory, "ZZZwallpaper.dll")) && File.Exists(Path.Combine(directory, "ZZZwallpaper.Launcher.exe")) ? directory : null;
    }

    internal static string[] FindRunningDirectories(Func<Process, string>? inspectImage = null)
    {
        inspectImage ??= UninstallProcesses.ImagePath;
        using var current = Process.GetCurrentProcess();
        for (int attempt = 0; ; attempt++) {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UninstallProcesses.InspectionFailure? unresolved = null;
            foreach (var name in Names)
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                        try
                        {
                            if (process.HasExited || process.SessionId != current.SessionId) continue;
                            if (ApplicationDirectory(inspectImage(process)) is { } directory) directories.Add(directory);
                        }
                        catch (InvalidOperationException) { /* Process exited during inspection. */ }
                        catch (Win32Exception error)
                        {
                            unresolved ??= new(process.Id, name, error.NativeErrorCode);
                        }
            if (unresolved == null) return directories.ToArray();
            if (attempt >= 4) throw unresolved.Exception();
            // An empty result is valid only after a fully readable fresh scan.
            Thread.Sleep(100);
        }
    }

    internal static void EnsureStopped()
    {
        if (FindRunningDirectories().Length != 0) throw new ApplicationRunningException();
    }

    internal static async Task<InstallationProcesses> PrepareAsync(string target, bool closeRunningApplication,
        IProgress<InstallationProgress> progress, CancellationToken token,
        Func<string[]>? discover = null, TimeSpan? gracefulTimeout = null, IEnumerable<string>? protectedDirectories = null)
    {
        token.ThrowIfCancellationRequested();
        discover ??= () => FindRunningDirectories();
        var running = discover();
        if (!closeRunningApplication && running.Length != 0) throw new ApplicationRunningException();
        var scope = new InstallationProcesses();
        try
        {
            if (closeRunningApplication) progress.Report(new(0, "正在退出程序"));
            foreach (var directory in running.Append(Path.GetFullPath(target)).Concat(protectedDirectories ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                scope.stopping.Add(new UninstallProcesses(directory, forInstallation: true));
            }
            foreach (var stop in scope.stopping)
                await stop.StopAsync(gracefulTimeout ?? TimeSpan.FromSeconds(8), cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (discover().Length != 0) throw new ApplicationRunningException();
            return scope;
        }
        catch { scope.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var stop in stopping) stop.Dispose();
        stopping.Clear();
    }
}
