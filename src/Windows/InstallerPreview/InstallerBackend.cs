namespace ZZZwallpaper.InstallerPreview;

internal sealed record InstallationRequest(string Directory, bool DesktopShortcut,
    string AcceptedAgreementVersion = "", bool CloseRunningApplication = false);

internal class InstallerException(string message) : Exception(message);
internal sealed class ApplicationRunningException() : InstallerException("ZZZwallpaper 正在运行。退出软件、壁纸、桌面组件及 AI 连接后继续安装。");

// Both the packaged engine and the UI-only preview use this boundary.
internal interface IInstallerBackend
{
    Task InstallAsync(InstallationRequest request, IProgress<InstallationProgress> progress, CancellationToken cancellationToken);
    Task FinishAsync(InstallationRequest request, bool launchApplication, CancellationToken cancellationToken);
}

// Used only by UI-only builds and explicit render/verification paths. No file,
// process, registry or network operations, even with the launch option selected.
internal sealed class NoOpInstallerBackend : IInstallerBackend
{
    private readonly TimeSpan stepDelay;
    public NoOpInstallerBackend() : this(TimeSpan.FromMilliseconds(160)) { }
    internal NoOpInstallerBackend(TimeSpan stepDelay) => this.stepDelay = stepDelay;

    public async Task InstallAsync(InstallationRequest request, IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        for (var percent = 2; percent <= 100; percent += 2)
        {
            await Task.Delay(stepDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new(percent, File: "ZZZwallpaper.dll", CompletedFiles: percent * 15, TotalFiles: 1500));
        }
    }

    public Task FinishAsync(InstallationRequest request, bool launchApplication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
