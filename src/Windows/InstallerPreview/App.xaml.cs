using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using System.Reflection;

namespace ZZZwallpaper.InstallerPreview;

public partial class App : Application
{
    private Mutex? setupMutex;

    protected override void OnExit(ExitEventArgs e)
    {
        setupMutex?.Dispose();
        base.OnExit(e);
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SoftwareTheme.ApplyDefaultPalette();
        if (UninstallBackend.IsUninstallerBuild && (e.Args.Length == 0 || e.Args is ["--uninstall"]))
        {
            UninstallTempCleanup.Sweep();
            try { UninstallBackend.LaunchTemporaryUi(); Shutdown(0); }
            catch (Exception exception)
            {
                MainWindow = new UninstallWindow(AppContext.BaseDirectory, initialError:
                    exception is InstallerException ? exception.Message : "无法打开卸载程序。");
                MainWindow.Show();
            }
            return;
        }
        if (UninstallBackend.IsUninstallerBuild && e.Args is ["--uninstall-from", var target])
        {
            UninstallTempCleanup.TrackCurrentProcess();
            setupMutex = new Mutex(true, @"Local\ZZZwallpaper.Setup.UI", out var first);
            MainWindow = new UninstallWindow(target, initialError: first ? null : "安装或卸载程序已经打开。");
            MainWindow.Show();
            return;
        }
        if (e.Args.Length == 0)
        {
            setupMutex = new Mutex(true, @"Local\ZZZwallpaper.Setup.UI", out var first);
            if (!first)
            {
                MessageBox.Show("安装程序已经打开。", "ZZZwallpaper 安装", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }
            UninstallTempCleanup.Sweep();
            MainWindow = new MainWindow(EnginePackageInfo.IsBundled ? new BundledInstallerBackend() : new NoOpInstallerBackend());
            MainWindow.Show();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package")
        {
            var output = Path.GetFullPath(e.Args[1]);
            Directory.CreateDirectory(output);
            try
            {
                var package = EnginePackageInfo.Read();
                using var engine = Assembly.GetExecutingAssembly().GetManifestResourceStream("Installer.Engine")
                    ?? throw new InvalidOperationException("No embedded installation engine.");
                var actual = Convert.ToHexString(SHA256.HashData(engine));
                if (!actual.Equals(package.EngineSha256, StringComparison.OrdinalIgnoreCase) ||
                    !package.CleanPackage || package.Version != PreviewSession.Version || package.Version != ProductIdentity.Current.Version)
                    throw new InvalidOperationException("Bundled engine identity/hash mismatch.");
                if (package.SourceCommit is { } revision && Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion != $"{package.Version}+{revision}")
                    throw new InvalidOperationException("Installer and payload source revisions differ.");
                var checks = VerifyModel().Concat(VerifyBackend()).Concat(VerifyLegalDocuments()).Concat(VerifyEngineBoundary()).Concat(UninstallBackend.VerifyBoundary()).ToList();
                var window = new MainWindow(true);
                checks.AddRange(window.VerifyLegalFlow());
                checks.AddRange(window.VerifyRunningFlow());
                File.WriteAllText(Path.Combine(output, "package-verification.json"), JsonSerializer.Serialize(new {
                    passed = true, product = ProductIdentity.Current, package, checks,
                    actualInstallationRun = false, engineExecuted = false, verifiedUtc = DateTime.UtcNow
                }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "verification-error.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] is "--render-preview" or "--render-uninstall")
        {
            // Development-only render output, explicitly selected by the caller.
            // This path is never the simulated installation directory.
            var output = Path.GetFullPath(e.Args[1]);
            Directory.CreateDirectory(output);
            try
            {
                var checks = VerifyModel().Concat(VerifyBackend()).Concat(VerifyLegalDocuments()).ToList();
                var window = new MainWindow(true);
                checks.AddRange(window.VerifyLegalFlow());
                checks.AddRange(window.VerifyRunningFlow());
                foreach (var scene in new[] { "settings", "accepted", "agreement", "privacy", "third-party", "license", "progress", "complete", "cancel", "cancelled", "upgrade", "running", "failure", "path", "invalid" })
                {
                    window.PrepareSnapshot(scene);
                    if (SoftwareTheme.Selection.IsFrozen)
                        throw new InvalidOperationException("The shared accent brush must stay mutable for theme animation.");
                    var bitmap = new RenderTargetBitmap(global::ZZZwallpaper.InstallerPreview.MainWindow.DesignWidth,
                        global::ZZZwallpaper.InstallerPreview.MainWindow.DesignHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window.PreviewRoot);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"{scene}.png"));
                    png.Save(stream);
                }
                var uninstallWindow = new UninstallWindow(new PreviewSession().InstallDirectory, renderOnly: true);
                foreach (var scene in new[] { "confirm", "delete-confirm", "progress", "complete", "delete-complete", "cleanup-incomplete", "error" })
                {
                    uninstallWindow.PrepareSnapshot(scene);
                    var bitmap = new RenderTargetBitmap(UninstallWindow.DesignWidth, UninstallWindow.DesignHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(uninstallWindow.PreviewRoot);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"uninstall-{scene}.png"));
                    png.Save(stream);
                }
                checks.AddRange(UninstallBackend.VerifyBoundary());
                File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
                {
                    version = PreviewSession.Version, product = "ZZZwallpaper.Setup",
                    verifiedUtc = DateTime.UtcNow, passed = true, checks,
                    renderer = "Native WPF RenderTargetBitmap", uninstallerBuild = UninstallBackend.IsUninstallerBuild,
                    embeddedInstallationEngine = EnginePackageInfo.IsBundled, actualInstallationRun = false, actualUninstallationRun = false
                }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "render-error.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        MessageBox.Show("不支持的启动参数。",
            "ZZZwallpaper 安装", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown(2);
    }

    private static string[] VerifyModel()
    {
        static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }
        var model = new PreviewSession { InstallDirectory = @"D:\应用 空间\ZZZwallpaper", DesktopShortcut = false };
        Require(!model.AgreementAccepted, "Agreement must not be preselected.");
        model.Start();
        Require(model.Stage == PreviewStage.Settings && model.Progress == 0, "Unaccepted agreement must block installation.");
        model.AgreementAccepted = true;
        void ResetAccepted() { model.Reset(); model.AgreementAccepted = true; }
        model.Start();
        Require(model.Stage == PreviewStage.Progress, "Chinese/space path failed.");
        for (var i = 0; i < 50; i++) model.Advance();
        Require(model.Stage == PreviewStage.Complete && model.Progress == 100 && !model.LaunchAfter && !model.DesktopShortcut,
            "Completion/shortcut/launch defaults failed.");
        model.BackToSettings();
        Require(model.InstallDirectory == @"D:\应用 空间\ZZZwallpaper", "Back must retain choices.");
        model.Start();
        model.Advance();
        model.Cancel();
        model.Advance();
        Require(model.Stage == PreviewStage.Cancelled && model.Progress == 2, "Cancellation must stop progress.");
        ResetAccepted();
        model.Scenario = PreviewScenario.Upgrade;
        model.Start();
        Require(model.Stage == PreviewStage.Progress, "Upgrade simulation failed.");
        foreach (var scenario in new[] { PreviewScenario.Running, PreviewScenario.Space, PreviewScenario.Permission, PreviewScenario.Newer })
        {
            ResetAccepted(); model.Scenario = scenario; model.Start();
            Require(model.Stage == PreviewStage.Error && model.Progress == 0, $"Preflight scenario {scenario} failed.");
        }
        ResetAccepted(); model.Scenario = PreviewScenario.Failure; model.Start();
        for (var i = 0; i < 26; i++) model.Advance();
        Require(model.Stage == PreviewStage.Error && model.Progress == 52, "Mid-install failure failed.");
        foreach (var invalid in new[] { "", @"C:\", "relative", @"D:\Apps\..\ZZZ", @"D:\CON", @"D:\bad?path" })
            Require(PreviewSession.ValidatePath(invalid).Length > 0, $"Invalid path accepted: {invalid}");
        ResetAccepted(); model.InstallDirectory = "relative"; model.Start();
        Require(model.Stage == PreviewStage.Settings && model.PathError.Length > 0, "Invalid path must stay in settings.");
        model.Reset();
        Require(model.DesktopShortcut && model.Progress == 0 && model.Scenario == PreviewScenario.Normal && !model.LaunchAfter && !model.AgreementAccepted,
            "Reset defaults failed.");
        return ["normal completion", "Chinese and space path", "shortcut option", "launch unchecked",
            "cancel freezes progress", "back retains choices", "upgrade", "running", "disk space", "permissions",
            "downgrade blocked", "write failure at 52%", "invalid path rejected", "reset", "agreement unchecked by default", "agreement required before install"];
    }

    private sealed class InlineProgress(Action<int> onProgress) : IProgress<InstallationProgress>
    {
        public void Report(InstallationProgress value) => onProgress((int)value.Percent);
    }

    private static string[] VerifyBackend()
    {
        var backend = new NoOpInstallerBackend(TimeSpan.Zero);
        var request = new InstallationRequest(@"D:\应用 空间\ZZZwallpaper", false);
        if (request.CloseRunningApplication) throw new InvalidOperationException("Application closing must be opt-in.");
        var values = new List<int>();
        backend.InstallAsync(request, new InlineProgress(values.Add), CancellationToken.None).GetAwaiter().GetResult();
        if (!values.SequenceEqual(Enumerable.Range(1, 50).Select(value => value * 2)))
            throw new InvalidOperationException("No-op backend did not report ordered completion.");

        using var cancellation = new CancellationTokenSource();
        values.Clear();
        try
        {
            backend.InstallAsync(request, new InlineProgress(value =>
            {
                values.Add(value);
                if (value == 20) cancellation.Cancel();
            }), cancellation.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("No-op backend ignored cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (values.Count != 10 || values[^1] != 20)
                throw new InvalidOperationException("Progress continued after cancellation.");
        }
        backend.FinishAsync(request, false, CancellationToken.None).GetAwaiter().GetResult();
        backend.FinishAsync(request, true, CancellationToken.None).GetAwaiter().GetResult();
        backend.InstallAsync(request with { CloseRunningApplication = true }, new InlineProgress(_ => { }), CancellationToken.None).GetAwaiter().GetResult();
        var state = new PreviewSession { AgreementAccepted = true };
        state.Start(); state.ReportProgress(20); state.Cancel(); state.ReportProgress(100); state.Complete();
        if (state.Stage != PreviewStage.Cancelled || state.Progress != 20)
            throw new InvalidOperationException("Cancelled UI state accepted stale progress.");
        return ["no-op backend completion", "no-op backend cancellation", "no-op finish with either launch choice", "stale progress ignored after cancel"];
    }

    private static string[] VerifyLegalDocuments()
    {
        if (InstallationDirectory.FromSelectedFolder(@"D:\", @"C:\Apps\ZZZwallpaper") != @"D:\ZZZwallpaper" ||
            InstallationDirectory.FromSelectedFolder(@"D:\", @"D:\") != @"D:\ZZZwallpaper" ||
            InstallationDirectory.FromSelectedFolder(@"D:\软件", @"C:\Apps\ZZZwallpaper") != @"D:\软件\ZZZwallpaper" ||
            InstallationDirectory.FromSelectedFolder(@"D:\软件\ZZZwallpaper\", "") != @"D:\软件\ZZZwallpaper" ||
            InstallationDirectory.FromSelectedFolder(@"D:\自定义 空格", @"D:\自定义 空格") != @"D:\自定义 空格")
            throw new InvalidOperationException("Folder selection must preserve explicit destinations and append the product directory once.");
        foreach (var document in LegalDocuments.All)
            if (string.IsNullOrWhiteSpace(document.Read()))
                throw new InvalidOperationException($"Empty embedded document: {document.Title}");
        if (!LegalDocuments.Agreement.Read().Contains("AGPL-3.0-only") ||
            !LegalDocuments.Agreement.Read().Contains("包括商业使用和收费分发") ||
            !LegalDocuments.ThirdParty.Single(d => d.ResourceName == "Legal.Agpl").Read().Contains("GNU AFFERO GENERAL PUBLIC LICENSE"))
            throw new InvalidOperationException("The selected AGPL v3 license is missing.");
        foreach(var document in new[] {LegalDocuments.Agreement,LegalDocuments.Privacy})
            if(!document.Read().Contains("文档修订号："+ProductIdentity.Current.AgreementVersion) || document.Read().Contains("测试版 v0.1.6"))
                throw new InvalidOperationException("Legal document revision differs from the accepted package revision.");
        var supportUrl = ProductIdentity.Current.SupportUrl;
        if (!Uri.TryCreate(supportUrl, UriKind.Absolute, out var contact) || contact.Scheme != Uri.UriSchemeHttps ||
            !LegalDocuments.Agreement.Read().Contains(supportUrl, StringComparison.Ordinal) ||
            !LegalDocuments.Privacy.Read().Contains(supportUrl, StringComparison.Ordinal))
            throw new InvalidOperationException("The installed feedback link and offline privacy documents must agree.");
        return ["project feedback link matches both offline documents", "folder selection normalizes roots and avoids duplicate product directories", "all legal documents available offline", "AGPL v3 license and commercial-use rights", "legal document and acknowledgement revisions match"];
    }

    private static string[] VerifyEngineBoundary()
    {
        var request = new InstallationRequest(@"D:\安装 空格 & 字符\ZZZwallpaper", false, ProductIdentity.Current.AgreementVersion);
        var start = BundledInstallerBackend.CreateEngineStart(@"D:\临时 目录\engine.exe", @"D:\临时 目录", request);
        if (start.UseShellExecute || !start.ArgumentList.Contains("/DIR=" + request.Directory) ||
            !start.ArgumentList.Contains("/TASKS=!desktopicon") || !start.ArgumentList.Contains("/NOCLOSEAPPLICATIONS") ||
            !start.ArgumentList.Contains("/AGREEMENT=" + request.AcceptedAgreementVersion))
            throw new InvalidOperationException("Engine arguments must retain exact path, consent and no forced application close.");
        try
        {
            new BundledInstallerBackend().InstallAsync(request with { AcceptedAgreementVersion = "" },
                new InlineProgress(_ => throw new InvalidOperationException("Unaccepted request reported installation progress.")),
                CancellationToken.None).GetAwaiter().GetResult();
            throw new InvalidOperationException("Engine accepted a request without current agreement.");
        }
        catch (InstallerException) { }
        return ["embedded engine hash verified", "engine argument isolation", "engine independently requires current agreement"];
    }
}
