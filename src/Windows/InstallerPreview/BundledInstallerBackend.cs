using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ZZZwallpaper.InstallerPreview;

// Only a build with the embedded, verified engine selects this backend. Rendering
// and preview builds always use NoOpInstallerBackend, including their self checks.
internal sealed class BundledInstallerBackend : IInstallerBackend
{
    private string? completedDirectory;

    internal static void ValidateDestination(string directory)
    {
        var pathError = PreviewSession.ValidatePath(directory);
        if (pathError.Length != 0) throw new InstallerException(pathError);
        var target = Path.GetFullPath(directory);
        for (var node = new DirectoryInfo(target); node is not null; node = node.Parent)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("安装位置不能包含目录链接。");
        if (File.Exists(target)) throw new InstallerException("安装位置已被文件占用。");
        if (!Directory.Exists(target) || !Directory.EnumerateFileSystemEntries(target).Any()) return;
        var marker = Path.Combine(target, "install-identity.json");
        try
        {
            using var identity = JsonDocument.Parse(File.ReadAllText(marker));
            if (identity.RootElement.GetProperty("appId").GetString() != ProductIdentity.AppId ||
                !File.Exists(Path.Combine(target, "unins000.exe")))
                throw new InstallerException("请选择空文件夹或本软件的安装目录。");
            var installed = Version.Parse(identity.RootElement.GetProperty("version").GetString()!);
            if (installed > Version.Parse(ProductIdentity.Current.Version))
                throw new InstallerException("此位置已安装更高版本。");
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or FormatException)
        { throw new InstallerException("请选择空文件夹或本软件的安装目录。"); }
    }

    internal static void EnsureApplicationStopped()
    {
        InstallationProcesses.EnsureStopped();
    }

    internal static ProcessStartInfo CreateEngineStart(string executable, string scratch, InstallationRequest request)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = scratch };
        // Each value is one argument. User paths are never concatenated into shell code.
        foreach (var argument in new[] {
            "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
            "/ZZZBOOTSTRAP=1", "/LANG=zhCN", "/DIR=" + request.Directory,
            "/TASKS=" + (request.DesktopShortcut ? "desktopicon" : "!desktopicon"),
            "/AGREEMENT=" + request.AcceptedAgreementVersion,
            "/STATUSFILE=" + Path.Combine(scratch, "status.ini"),
            "/CANCELFILE=" + Path.Combine(scratch, "cancel.flag"),
            "/LOG=" + Path.Combine(scratch, "engine.log"),
            "/APPMUTEX=Local\\LumaWall.Control." + WindowsIdentity.GetCurrent().User!.Value + ",Local\\LumaWall.Modular.Control." + WindowsIdentity.GetCurrent().User!.Value
        }) info.ArgumentList.Add(argument);
        return info;
    }

    public async Task InstallAsync(InstallationRequest request, IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        completedDirectory = null;
        if (request.AcceptedAgreementVersion != ProductIdentity.Current.AgreementVersion)
            throw new InstallerException("请先阅读并确认当前用户协议与隐私说明。");
        ValidateDestination(request.Directory);
        var upgrade = InstallationUpgrade.Prepare(request.Directory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.CloseRunningApplication) EnsureApplicationStopped();
        var package = EnginePackageInfo.Read();
        if (!package.CleanPackage || package.Version != ProductIdentity.Current.Version)
            throw new InstallerException("安装包版本信息不一致。");
        var drive = new DriveInfo(Path.GetPathRoot(request.Directory)!);
        if (drive.AvailableFreeSpace < package.PayloadBytes + 128L * 1024 * 1024)
            throw new InstallerException("磁盘空间不足，请更换安装位置。");
        cancellationToken.ThrowIfCancellationRequested();
        var scratch = Directory.CreateTempSubdirectory("ZZZwallpaper-Setup-").FullName;
        try
        {
            var engine = Path.Combine(scratch, "engine.exe");
            await using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Installer.Engine")
                ?? throw new InstallerException("安装文件缺失。"))
            await using (var destination = new FileStream(engine, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                await Task.Run(() => TransferEngine(source, destination, progress, cancellationToken), cancellationToken);
            await using (var read = File.OpenRead(engine))
                if (!Convert.ToHexString(await Task.Run(() => VerifyEngine(read, progress, cancellationToken), cancellationToken)).Equals(package.EngineSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InstallerException("安装文件校验失败，请重新下载安装包。");
            cancellationToken.ThrowIfCancellationRequested();
            using var stopping = await InstallationProcesses.PrepareAsync(request.Directory, request.CloseRunningApplication, progress, cancellationToken,
                protectedDirectories: upgrade.Legacy is { } old ? [old.Directory] : []);
            EnsureApplicationStopped();
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new(15, "正在启动安装引擎"));
            using var process = Process.Start(CreateEngineStart(engine, scratch, request))
                ?? throw new InstallerException("无法启动安装引擎。");
            var cancelled = false;
            var status = new Dictionary<string, string>();
            // Never terminate the engine: cancellation must finish its rollback.
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested && !cancelled)
                {
                    await File.WriteAllTextAsync(Path.Combine(scratch, "cancel.flag"), "cancel", CancellationToken.None);
                    cancelled = true;
                }
                status = InstallationProgress.ReadStatus(Path.Combine(scratch, "status.ini"));
                if (InstallationProgress.FromStatus(status) is { } update) progress.Report(update);
                await Task.Delay(120, CancellationToken.None);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            status = InstallationProgress.ReadStatus(Path.Combine(scratch, "status.ini"));
            if (process.ExitCode == 0 && status.GetValueOrDefault("stage") == "done")
            {
                completedDirectory = request.Directory;
                // From this point installation has committed. Finish the owned
                // legacy cleanup even if cancellation arrived at the last moment.
                await InstallationUpgrade.CompleteAsync(upgrade.Legacy, request.Directory, scratch, upgrade.Startup, progress);
                progress.Report(new(100, "安装完成"));
                return;
            }
            if (cancelled && process.ExitCode is 1 or 2 or 5 or 7 or 8)
                throw new OperationCanceledException(cancellationToken);
            if (status.GetValueOrDefault("error") == "running") throw new ApplicationRunningException();
            throw new InstallerException(status.GetValueOrDefault("error") switch
            {
                "newer" => "此位置已安装更高版本。",
                "occupied" => "请选择空文件夹或本软件的安装目录。",
                "agreement" => "请先阅读并确认当前用户协议与隐私说明。",
                _ => $"安装未完成（错误 {process.ExitCode}），请检查权限与可用空间后重试。"
            });
        }
        finally
        {
            // Only remove the exact files owned by this run, never a chosen install
            // directory, parent directory, recursive tree, or user data directory.
            foreach (var name in new[] { "engine.exe", "status.ini", "status.ini.tmp", "cancel.flag", "engine.log", "legacy-uninstall.done", "legacy-uninstall.log" })
                try { File.Delete(Path.Combine(scratch, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(scratch, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task TransferEngine(Stream source, Stream destination, IProgress<InstallationProgress> progress, CancellationToken token)
    {
        var buffer = new byte[1024 * 1024]; long copied = 0; var last = -1;
        progress.Report(new(0, "正在准备安装文件"));
        while (await source.ReadAsync(buffer, token) is var count && count > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), token); copied += count;
            var tenths = (int)(100 * copied / source.Length);
            if (tenths != last) { last = tenths; progress.Report(new(0, "正在准备安装文件")); }
        }
    }

    private static async Task<byte[]> VerifyEngine(Stream source, IProgress<InstallationProgress> progress, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024]; long checkedBytes = 0; var last = -1;
        while (await source.ReadAsync(buffer, token) is var count && count > 0)
        {
            hash.AppendData(buffer, 0, count); checkedBytes += count;
            var tenths = (int)(50 * checkedBytes / source.Length);
            if (tenths != last) { last = tenths; progress.Report(new(0, "正在校验安装文件")); }
        }
        return hash.GetHashAndReset();
    }

    public Task FinishAsync(InstallationRequest request, bool launchApplication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (launchApplication)
        {
            if (completedDirectory is null || !Path.GetFullPath(request.Directory).Equals(Path.GetFullPath(completedDirectory), StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("安装尚未完成，无法启动软件。");
            Process.Start(new ProcessStartInfo(Path.Combine(completedDirectory, "ZZZwallpaper.Launcher.exe"))
            { UseShellExecute = false, WorkingDirectory = completedDirectory });
        }
        return Task.CompletedTask;
    }
}
