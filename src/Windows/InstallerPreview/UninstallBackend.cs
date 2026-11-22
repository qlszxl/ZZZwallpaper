using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record UninstallRequest(string Directory, bool Confirmed = false, bool KeepPersonalData = true);
internal sealed record UninstallResult(bool KeptPersonalData, bool CleanupIncomplete = false);

internal static class UninstallBackend
{
    internal static bool IsUninstallerBuild => typeof(App).Assembly.GetName().Name == "ZZZwallpaper.Uninstall";

    internal static string ValidateTarget(string directory)
    {
        if (PreviewSession.ValidatePath(directory).Length != 0)
            throw new InstallerException("卸载位置无效。");
        var target = Path.GetFullPath(directory);
        for (var node = new DirectoryInfo(target); node is not null; node = node.Parent)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("卸载位置不能包含目录链接。");
        try
        {
            using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "install-identity.json")));
            if (identity.RootElement.GetProperty("appId").GetString() != ProductIdentity.AppId ||
                !File.Exists(Path.Combine(target, "unins000.exe")) || !File.Exists(Path.Combine(target, "unins000.dat")))
                throw new InstallerException("未找到本软件的卸载文件。");
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        { throw new InstallerException("未找到本软件的卸载文件。"); }
        return target;
    }

    // Run the UI from a private temporary directory so no installed UI file is
    // held open while the engine removes its own recorded application files.
    internal static void LaunchTemporaryUi()
    {
        var target = ValidateTarget(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var source = Path.Combine(target, "ZZZwallpaper.Uninstall.exe");
        var temporary = Directory.CreateTempSubdirectory("ZZZwallpaper-Uninstall-").FullName;
        var executable = Path.Combine(temporary, "ZZZwallpaper.Uninstall.exe");
        try {
            File.Copy(source, executable);
            UninstallTempCleanup.Prepare(temporary, target);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = temporary };
            start.ArgumentList.Add("--uninstall-from");
            start.ArgumentList.Add(target);
            using var process = Process.Start(start) ?? throw new InstallerException("无法打开卸载程序。");
        } catch {
            UninstallTempCleanup.Abandon(temporary);
            throw;
        }
    }

    internal static ProcessStartInfo CreateEngineStart(string target, string logPath)
    {
        var start = new ProcessStartInfo(Path.Combine(target, "unins000.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ZZZUNINSTALL=1",
            "/APPMUTEX=Local\\LumaWall.Control." + WindowsIdentity.GetCurrent().User!.Value,
            "/STOPGUARD=" + LumaWall.Lifecycle.InstallationLifetime.SignalName(target) + ".Guard",
            "/DONEFILE=" + Path.Combine(Path.GetDirectoryName(logPath)!, "uninstall.done"), "/LOG=" + logPath })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<UninstallResult> ExecuteAsync(UninstallRequest request, IProgress<string>? progress = null)
    {
        // Independent confirmation guard; verification invokes only this rejection.
        if (!request.Confirmed) throw new InstallerException("请先确认卸载。");
        var target = ValidateTarget(request.Directory);
        if (Environment.ProcessPath is { } current && Path.GetFullPath(current).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("请重新打开卸载程序。");
        using var stopping = new UninstallProcesses(target);
        progress?.Report("正在退出软件");
        await stopping.StopAsync(progress:progress);
        var scratch = Directory.CreateTempSubdirectory("ZZZwallpaper-UninstallLog-").FullName;
        var log = Path.Combine(scratch, "uninstall.log");
        try
        {
            progress?.Report("正在卸载");
            using var process = Process.Start(CreateEngineStart(target, log))
                ?? throw new InstallerException("无法启动卸载程序。");
            // Uninstall is irreversible after confirmation. Never interrupt or
            // kill the engine midway. Personal data cleanup follows engine success only.
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InstallerException($"卸载未完成（错误 {process.ExitCode}）。请稍后重试。");
            var cleanup=await UninstallFinalizer.FinishAsync(target,Path.Combine(scratch,"uninstall.done"),request.KeepPersonalData,
                PersonalDataCleanup.RemoveCurrentUserData,progress);
            UninstallTempCleanup.MarkProgramRemoved(target);
            return new(request.KeepPersonalData,cleanup?.FailedItems>0);
        }
        finally
        {
            try { File.Delete(log); File.Delete(Path.Combine(scratch, "uninstall.done")); Directory.Delete(scratch, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static async Task<bool> RetryCleanupAsync(string directory)
    {
        if(!UninstallCompletion.ProgramRemoved(directory))throw new InstallerException("检测到程序文件，请重新打开卸载程序。");
        using var stopping=new UninstallProcesses(directory);
        await stopping.StopAsync();
        return (await UninstallFinalizer.CleanAsync(PersonalDataCleanup.RemoveCurrentUserData)).FailedItems==0;
    }

    internal static string[] VerifyBoundary()
    {
        var directory = @"D:\应用 空格 & 字符\ZZZwallpaper";
        var start = CreateEngineStart(directory, @"D:\临时 空格\uninstall.log");
        if (start.UseShellExecute || start.FileName != Path.Combine(directory, "unins000.exe") ||
            !start.ArgumentList.Contains("/VERYSILENT") || !start.ArgumentList.Contains("/NORESTART") ||
            !start.ArgumentList.Contains("/ZZZUNINSTALL=1"))
            throw new InvalidOperationException("Uninstall must use the owned engine and isolated arguments.");
        try
        {
            ExecuteAsync(new UninstallRequest(directory)).GetAwaiter().GetResult();
            throw new InvalidOperationException("Uninstall started without confirmation.");
        }
        catch (InstallerException) { }
        if (!new UninstallRequest(directory).KeepPersonalData)
            throw new InvalidOperationException("Personal data retention must be the default.");
        return ["uninstall arguments preserve paths without a shell", "uninstall requires explicit confirmation before I/O", "uninstall retains personal data by default"];
    }
}
