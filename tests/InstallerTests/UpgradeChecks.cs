using System.Text.Json;
using ZZZwallpaper.InstallerPreview;

internal static class UpgradeChecks
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var canonical = Path.Combine(root, "Programs", "ZZZwallpaper");
        var legacy = Path.Combine(root, "旧 安装 & 目录", "ZZZwallpaper.Modular");
        var scratch = Directory.CreateDirectory(Path.Combine(root, "scratch")).FullName;
        void Write(string directory, string name, string data = "fixture")
        { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), data); }
        void Installed(string directory, string appId)
        {
            Write(directory, "install-identity.json", JsonSerializer.Serialize(new { appId, version = "0.1.11.28" }));
            foreach (var file in new[] { "unins000.exe", "unins000.dat", "ZZZwallpaper.dll", "ZZZwallpaper.Launcher.exe" }) Write(directory, file);
        }
        string Uninstall(string directory) => '"' + Path.Combine(directory, "ZZZwallpaper.Uninstall.exe") + "\" --uninstall";
        bool Refuses(Action action) { try { action(); return false; } catch (InstallerException) { return true; } }
        Installed(canonical, ProductIdentity.AppId); Installed(legacy, InstallationUpgrade.LegacyAppId);
        check(ProductIdentity.AppId == "D82EA01A-D414-4393-BE4D-7AD3371671C0" && PreviewSession.DefaultDirectory.EndsWith(@"Programs\ZZZwallpaper"), "Installation uses the original identity and canonical product directory");
        var original = InstallationUpgrade.ValidateRegistration(ProductIdentity.AppId, canonical, Uninstall(canonical));
        var old = InstallationUpgrade.ValidateRegistration(InstallationUpgrade.LegacyAppId, legacy, Uninstall(legacy));
        check(original.Directory == canonical && old.Directory == legacy, "Both known installation identities are recognized from their exact markers and uninstall commands");
        check(Refuses(() => InstallationUpgrade.ValidateRegistration(ProductIdentity.AppId, legacy, Uninstall(legacy))), "A registry identity cannot claim a different product marker");
        check(Refuses(() => InstallationUpgrade.ValidateRegistration(InstallationUpgrade.LegacyAppId, legacy, Uninstall(legacy) + " & unrelated.exe")), "Uninstall command suffixes never become executable migration instructions");
        check(Refuses(() => InstallationUpgrade.ValidateRegistration(InstallationUpgrade.LegacyAppId, legacy, Uninstall(canonical))), "A registration cannot redirect cleanup to another installation");
        check(Refuses(() => InstallationUpgrade.ValidateTarget(Path.Combine(root, "Other"), original, old)), "An existing canonical installation is upgraded in its original directory");
        check(Refuses(() => InstallationUpgrade.ValidateTarget(canonical, original, old with { Version = "99.0.0.0" })), "A newer separate installation is never retired by a lower-version package");
        check(Refuses(() => InstallationUpgrade.ValidateTarget(legacy, null, old)) &&
              Refuses(() => InstallationUpgrade.ValidateTarget(Path.Combine(legacy, "child"), null, old)) &&
              Refuses(() => InstallationUpgrade.ValidateTarget(Path.GetDirectoryName(legacy)!, null, old)), "Upgrade refuses equal, nested and parent destinations that old uninstall could remove");
        InstallationUpgrade.ValidateTarget(canonical, original, old);
        check(Refuses(() => InstallationUpgrade.SafeDirectory(Path.GetPathRoot(root)!)) &&
              Refuses(() => InstallationUpgrade.SafeDirectory(Path.Combine(root, "..", "outside"))), "Cleanup targets reject a drive root and noncanonical parent traversal");
        var start = InstallationUpgrade.CreateLegacyUninstallStart(old, scratch);
        check(!start.UseShellExecute && start.CreateNoWindow && start.FileName == Path.Combine(legacy, "unins000.exe") &&
              start.ArgumentList.Contains("/ZZZUNINSTALL=1") && start.ArgumentList.Contains("/NORESTART") &&
              start.ArgumentList.Any(value => value.StartsWith("/STOPGUARD=Local\\ZZZwallpaper.Stop.")), "Migration invokes only the exact old engine with separate arguments and an owned stop guard");
        var startup = new UpgradeStartup("ZZZwallpaper.Modular", '"' + Path.Combine(legacy, "ZZZwallpaper.Launcher.exe") + "\" --startup", [3, 0, 0, 0]);
        check(InstallationUpgrade.IsOwnedStartup(startup.Command, legacy) && !InstallationUpgrade.IsOwnedStartup(startup.Command, canonical) &&
              !InstallationUpgrade.IsOwnedStartup(startup.Command + " --other", legacy), "Startup migration matches the complete owned command and preserves other portable copies");
        Write(legacy, "personal.mp4"); Write(legacy, "notes.txt"); Write(legacy, "library.db");
        var progress = new Progress<InstallationProgress>();
        var retireCalls = 0; bool partial = false;
        try
        {
            await InstallationUpgrade.CompleteAsync(old, canonical, scratch, [startup], progress,
                (_, _) => throw new IOException("fixture locked"), (_, _, _) => retireCalls++);
        }
        catch (InstallerException error) { partial = error.Message.Contains("已安装成功") && error.Message.Contains("清理未完成"); }
        check(partial && retireCalls == 0 && File.Exists(Path.Combine(canonical, InstallationUpgrade.ReceiptFile)) && File.Exists(Path.Combine(legacy, "ZZZwallpaper.dll")), "Failed retirement reports committed new install and retains a retry receipt without pretending old files were removed");
        var receipt = InstallationUpgrade.ReadReceipt(canonical)!;
        check(receipt.Legacy.Directory == legacy && receipt.Startup.Single().Approval!.SequenceEqual(startup.Approval!), "Retry receipt preserves the verified old location and disabled startup preference");
        var removeCalls = 0;
        await InstallationUpgrade.CompleteAsync(old, canonical, scratch, receipt.Startup, progress,
            (app, _) =>
            {
                removeCalls++;
                foreach (var name in new[] { "install-identity.json", "unins000.exe", "unins000.dat", "ZZZwallpaper.dll", "ZZZwallpaper.Launcher.exe" }) File.Delete(Path.Combine(app.Directory, name));
                return Task.CompletedTask;
            }, (_, _, _) => retireCalls++);
        check(removeCalls == 1 && retireCalls == 1 && !File.Exists(Path.Combine(canonical, InstallationUpgrade.ReceiptFile)), "Retry completes recorded engine retirement and clears the receipt only after final cleanup");
        check(new[] { "personal.mp4", "notes.txt", "library.db" }.All(name => File.Exists(Path.Combine(legacy, name))) && File.Exists(Path.Combine(canonical, "ZZZwallpaper.dll")), "Old user media, unknown files, personal data and the new installation are preserved");
        var gone = InstallationUpgrade.ValidateRegistration(InstallationUpgrade.LegacyAppId, legacy, Uninstall(legacy));
        check(!gone.ProgramPresent, "An interrupted old uninstall with no remaining executables can retire only its stale registration");
        await InstallationUpgrade.CompleteAsync(gone, canonical, scratch, [startup], progress,
            (_, _) => throw new Exception("No executable may run after complete removal"), (_, _, _) => retireCalls++);
        check(retireCalls == 2, "A cleanup-only retry never executes a missing old program or deletes unknown residual files");
        var shortcut = Path.Combine(root, "ZZZwallpaper.Modular.lnk"); Write(root, "ZZZwallpaper.Modular.lnk");
        InstallationUpgrade.RemoveOwnedShortcut(shortcut, legacy, _ => Path.Combine(root, "Portable", "ZZZwallpaper.Launcher.exe"));
        check(File.Exists(shortcut), "A same-name shortcut for another installation is retained");
        InstallationUpgrade.RemoveOwnedShortcut(shortcut, legacy, _ => Path.Combine(legacy, "ZZZwallpaper.Launcher.exe"));
        check(!File.Exists(shortcut), "Only a duplicate shortcut whose target matches the retired installation is removed");
        Write(canonical, InstallationUpgrade.ReceiptFile, JsonSerializer.Serialize(new InstallationUpgradeReceipt(ProductIdentity.AppId, legacy, gone, [startup])));
        check(Refuses(() => InstallationUpgrade.ReadReceipt(canonical)), "A retry receipt cannot redirect cleanup from a different canonical destination");
        File.Delete(Path.Combine(canonical, InstallationUpgrade.ReceiptFile));
    }
}
