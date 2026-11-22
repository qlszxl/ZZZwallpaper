using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record InstalledApplication(string AppId, string Directory, string Version, bool ProgramPresent = true);
internal sealed record UpgradeStartup(string Name, string Command, byte[]? Approval);
internal sealed record InstallationUpgradePlan(InstalledApplication? Legacy, UpgradeStartup[] Startup);
internal sealed record InstallationUpgradeReceipt(string AppId, string Target, InstalledApplication Legacy, UpgradeStartup[] Startup);

// The separate modular identity was a packaging mistake. Retire it only after
// the canonical installation has committed, using its own file ownership log.
internal static class InstallationUpgrade
{
    internal const string LegacyAppId = "A3B2E6D1-7C58-4E5A-984C-DF687B45309A";
    internal const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private static readonly string[] LegacyStartupNames = ["ZZZwallpaper.Modular", "LumaWall.Modular"];
    internal const string ReceiptFile = "installation-upgrade.json";

    internal static string RegistrationKey(string appId) => UninstallRoot + @"\{" + appId + "}_is1";

    internal static string SafeDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || directory.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InstallerException("已安装软件的位置无效。");
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Length <= (Path.GetPathRoot(full)?.Length ?? 0) ||
            !full.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("已安装软件的位置无效。");
        for (var node = new DirectoryInfo(full); node is not null; node = node.Parent)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("已安装软件的位置不能包含目录链接。");
        return full;
    }

    internal static bool IsOwnedUninstallCommand(string command, string directory) =>
        command.Trim().Equals('"' + Path.Combine(directory, "ZZZwallpaper.Uninstall.exe") + "\" --uninstall", StringComparison.OrdinalIgnoreCase) ||
        command.Trim().Equals('"' + Path.Combine(directory, "unins000.exe") + '"', StringComparison.OrdinalIgnoreCase);

    internal static InstalledApplication ValidateRegistration(string appId, string directory, string uninstallCommand)
    {
        var target = SafeDirectory(directory);
        if (appId is not (ProductIdentity.AppId or LegacyAppId) || !IsOwnedUninstallCommand(uninstallCommand, target))
            throw new InstallerException("已安装软件的注册信息不匹配，请先修复原安装。");
        var marker = Path.Combine(target, "install-identity.json");
        // An interrupted old uninstaller may already have removed all managed
        // executables. Its exact registration can still be retired on retry.
        if (appId == LegacyAppId && !File.Exists(marker) && UninstallCompletion.ProgramRemoved(target))
            return new(appId, target, "", false);
        try
        {
            if (new FileInfo(marker).Length > 65536) throw new InstallerException("已安装软件的身份文件无效。");
            foreach (var name in new[] { "install-identity.json", "unins000.exe", "unins000.dat" })
                if (!File.Exists(Path.Combine(target, name)) || (File.GetAttributes(Path.Combine(target, name)) & FileAttributes.ReparsePoint) != 0)
                    throw new InstallerException("已安装软件的身份或卸载文件不完整，请先修复原安装。");
            using var identity = JsonDocument.Parse(File.ReadAllText(marker));
            if (identity.RootElement.GetProperty("appId").GetString() != appId)
                throw new InstallerException("已安装软件的身份与注册信息不匹配。");
            var version = identity.RootElement.GetProperty("version").GetString()!;
            _ = Version.Parse(version);
            return new(appId, target, version);
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new InstallerException("无法验证已安装软件，请先修复原安装。"); }
    }

    internal static InstalledApplication? ReadRegistered(string appId)
    {
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = user.OpenSubKey(RegistrationKey(appId));
        if (key is null) return null;
        return ValidateRegistration(appId, key.GetValue("InstallLocation") as string ?? "", key.GetValue("UninstallString") as string ?? "");
    }

    internal static string PreferredDirectory() => ReadRegistered(ProductIdentity.AppId)?.Directory ?? PreviewSession.DefaultDirectory;

    internal static void ValidateTarget(string target, InstalledApplication? canonical, InstalledApplication? legacy)
    {
        target = SafeDirectory(target);
        if (legacy is not null && legacy.AppId != LegacyAppId)
            throw new InstallerException("旧安装身份无效，未执行接管。");
        if (legacy is not null && legacy.Version.Length > 0 && Version.Parse(legacy.Version) > Version.Parse(ProductIdentity.Current.Version))
            throw new InstallerException("已安装更高版本的 ZZZwallpaper，请使用相同或更高版本的安装包升级。");
        if (canonical is not null && !target.Equals(canonical.Directory, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("检测到已安装的 ZZZwallpaper，请使用现有安装位置升级：" + canonical.Directory);
        if (legacy is not null && (Within(target, legacy.Directory) || Within(legacy.Directory, target)))
            throw new InstallerException("旧安装目录需要在升级完成后清理，请将新版安装到 ZZZwallpaper 的统一目录。");
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static ProcessStartInfo CreateLegacyUninstallStart(InstalledApplication legacy, string scratch)
    {
        var start = new ProcessStartInfo(Path.Combine(legacy.Directory, "unins000.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = scratch };
        foreach (var argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ZZZUNINSTALL=1",
            "/STOPGUARD=" + LumaWall.Lifecycle.InstallationLifetime.SignalName(legacy.Directory) + ".Guard",
            "/DONEFILE=" + Path.Combine(scratch, "legacy-uninstall.done"), "/LOG=" + Path.Combine(scratch, "legacy-uninstall.log") })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static UpgradeStartup[] CaptureStartup(InstalledApplication? legacy)
    {
        if (legacy is null) return [];
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var run = user.OpenSubKey(RunKey); using var approved = user.OpenSubKey(ApprovedKey);
        return LegacyStartupNames.Select(name => new UpgradeStartup(name, run?.GetValue(name) as string ?? "", approved?.GetValue(name) as byte[]))
            .Where(item => IsOwnedStartup(item.Command, legacy.Directory)).ToArray();
    }

    internal static bool IsOwnedStartup(string command, string directory) => command.Trim().Equals(
        '"' + Path.Combine(directory, "ZZZwallpaper.Launcher.exe") + "\" --startup", StringComparison.OrdinalIgnoreCase);

    internal static InstallationUpgradePlan Prepare(string target)
    {
        var legacy = ReadRegistered(LegacyAppId);
        var receipt = ReadReceipt(target);
        if (legacy is not null && receipt is not null && !legacy.Directory.Equals(receipt.Legacy.Directory, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("待清理的旧安装位置发生变化，请先完成上一次升级。");
        legacy ??= receipt?.Legacy;
        ValidateTarget(target, ReadRegistered(ProductIdentity.AppId), legacy);
        var startup = CaptureStartup(legacy);
        return new(legacy, startup.Length > 0 ? startup : receipt?.Startup ?? []);
    }

    internal static InstallationUpgradeReceipt? ReadReceipt(string target)
    {
        target = SafeDirectory(target);
        var path = Path.Combine(target, ReceiptFile);
        if (!File.Exists(path)) return null;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 65536)
                throw new InstallerException("升级清理记录无效。");
            var receipt = JsonSerializer.Deserialize<InstallationUpgradeReceipt>(File.ReadAllText(path));
            if (receipt is null || receipt.AppId != ProductIdentity.AppId || receipt.Legacy.AppId != LegacyAppId ||
                !receipt.Target.Equals(target, StringComparison.OrdinalIgnoreCase) || receipt.Startup.Length > 2 ||
                receipt.Startup.Any(item => !LegacyStartupNames.Contains(item.Name) || !IsOwnedStartup(item.Command, receipt.Legacy.Directory)))
                throw new InstallerException("升级清理记录无效。");
            SafeDirectory(receipt.Legacy.Directory); ValidateTarget(target, null, receipt.Legacy);
            return receipt;
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NullReferenceException)
        { throw new InstallerException("无法读取待完成的升级清理记录。"); }
    }

    internal static async Task CompleteAsync(InstalledApplication? legacy, string target, string scratch, UpgradeStartup[] startup,
        IProgress<InstallationProgress> progress, Func<InstalledApplication, string, Task>? removeProgram = null,
        Action<InstalledApplication, string, UpgradeStartup[]>? retireRegistration = null)
    {
        if (legacy is null) return;
        progress.Report(new(100, "新版已安装，正在清理旧安装"));
        try
        {
            target = SafeDirectory(target);
            ValidateTarget(target, null, legacy);
            var receipt = Path.Combine(target, ReceiptFile);
            if (File.Exists(receipt)) _ = ReadReceipt(target);
            var temporaryReceipt = Path.Combine(target, "installation-upgrade-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryReceipt, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new InstallationUpgradeReceipt(ProductIdentity.AppId, target, legacy, startup));
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryReceipt, receipt, overwrite: true);
            }
            finally { File.Delete(temporaryReceipt); }
            if (!UninstallCompletion.ProgramRemoved(legacy.Directory)) await (removeProgram ?? RemoveLegacyProgramAsync)(legacy, scratch);
            if (!UninstallCompletion.ProgramRemoved(legacy.Directory))
                throw new InstallerException("旧安装的程序文件仍在使用。");
            (retireRegistration ?? RetireRegistration)(legacy, target, startup);
            File.Delete(receipt);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or COMException or System.Security.SecurityException)
        { throw new InstallerException("新版 ZZZwallpaper 已安装成功，但旧安装清理未完成。个人数据已保留，请重新运行安装包以完成清理。\n" + ex.Message); }
    }

    private static async Task RemoveLegacyProgramAsync(InstalledApplication legacy, string scratch)
    {
        // Revalidate immediately before executing the exact registered engine.
        ValidateRegistration(legacy.AppId, legacy.Directory, '"' + Path.Combine(legacy.Directory, "unins000.exe") + '"');
        using var process = Process.Start(CreateLegacyUninstallStart(legacy, scratch)) ?? throw new InstallerException("无法启动旧安装的清理程序。");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !await UninstallCompletion.WaitAsync(legacy.Directory, Path.Combine(scratch, "legacy-uninstall.done")))
            throw new InstallerException("旧安装清理尚未完成。");
    }

    private static void RetireRegistration(InstalledApplication legacy, string target, UpgradeStartup[] startup)
    {
        SafeDirectory(legacy.Directory);
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using (var old = user.OpenSubKey(RegistrationKey(LegacyAppId)))
        {
            if (old is not null && (!string.Equals((old.GetValue("InstallLocation") as string)?.TrimEnd('\\'), legacy.Directory, StringComparison.OrdinalIgnoreCase) ||
                !IsOwnedUninstallCommand(old.GetValue("UninstallString") as string ?? "", legacy.Directory)))
                throw new InstallerException("旧安装注册信息发生变化，未删除该注册项。");
        }
        using var run = user.CreateSubKey(RunKey); using var approved = user.CreateSubKey(ApprovedKey);
        foreach (var name in LegacyStartupNames)
            if (IsOwnedStartup(run.GetValue(name) as string ?? "", legacy.Directory))
            { run.DeleteValue(name, false); approved.DeleteValue(name, false); }
        if (startup.FirstOrDefault() is { } preference && (run.GetValue("ZZZwallpaper") is not string current || IsOwnedStartup(current, target)))
        {
            run.SetValue("ZZZwallpaper", '"' + Path.Combine(target, "ZZZwallpaper.Launcher.exe") + "\" --startup");
            if (preference.Approval is { } bytes) approved.SetValue("ZZZwallpaper", bytes, RegistryValueKind.Binary);
            else approved.DeleteValue("ZZZwallpaper", false);
        }
        foreach (var directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            RemoveOwnedShortcut(Path.Combine(directory, "ZZZwallpaper.Modular.lnk"), legacy.Directory);
        user.DeleteSubKeyTree(RegistrationKey(LegacyAppId), throwOnMissingSubKey: false);
    }

    internal static void RemoveOwnedShortcut(string path, string legacyDirectory, Func<string, string>? readTarget = null)
    {
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InstallerException("旧快捷方式是文件链接，未自动删除。");
        if ((readTarget ?? ReadShortcutTarget)(path).Equals(Path.Combine(legacyDirectory, "ZZZwallpaper.Launcher.exe"), StringComparison.OrdinalIgnoreCase))
            File.Delete(path);
    }

    private static string ReadShortcutTarget(string path)
    {
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            return ((dynamic)shortcut).TargetPath as string ?? "";
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
