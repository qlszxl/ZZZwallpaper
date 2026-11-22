using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ZZZwallpaper.InstallerPreview;

internal enum PreviewStage { Settings, Progress, Complete, Cancelled, Error }
internal enum PreviewScenario { Normal, Upgrade, Running, Space, Permission, Newer, Failure }

// The complete installation model is in memory. There is deliberately no
// installer backend, process execution, network client or system mutation API.
internal sealed class PreviewSession
{
    public static string Version => ProductIdentity.Current.Version;
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ZZZwallpaper");

    public PreviewStage Stage { get; private set; } = PreviewStage.Settings;
    public PreviewScenario Scenario { get; set; }
    public string InstallDirectory { get; set; } = DefaultDirectory;
    public bool DesktopShortcut { get; set; } = true;
    public bool LaunchAfter { get; set; }
    public bool AgreementAccepted { get; set; }
    public double Progress { get; private set; }
    public InstallationProgress Detail { get; private set; } = new(0, "正在准备安装文件");
    public string PathError { get; private set; } = "";
    public string ErrorTitle { get; private set; } = "";
    public bool ApplicationRunning { get; private set; }

    public static string ValidatePath(string input)
    {
        var path = input.Trim().TrimEnd('\\');
        if (!Regex.IsMatch(path, @"^[a-zA-Z]:\\") || path.Length < 4 ||
            path[2..].Contains(':') || path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 ||
            path.Any(char.IsControl))
            return "请输入完整的安装路径。";
        foreach (var segment in path[3..].Split('\\'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') ||
                segment.EndsWith(' ') || Regex.IsMatch(segment,
                    @"^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                return "安装路径无效。";
        }
        return "";
    }

    public void Start(bool closeRunningApplication = false)
    {
        if (Stage != PreviewStage.Settings || !AgreementAccepted) return;
        InstallDirectory = InstallDirectory.Trim().TrimEnd('\\');
        PathError = ValidatePath(InstallDirectory);
        if (PathError.Length > 0) { Stage = PreviewStage.Settings; return; }
        Progress = 0;
        Detail = new(0, "正在准备安装文件");
        LaunchAfter = false;
        ApplicationRunning = false;
        switch (Scenario)
        {
            case PreviewScenario.Running when !closeRunningApplication:
                RequireApplicationExit(); break;
            case PreviewScenario.Space:
                FailInstallation("磁盘空间不足，请更换安装位置。"); break;
            case PreviewScenario.Permission:
                FailInstallation("请选择可写入的安装位置。"); break;
            case PreviewScenario.Newer:
                FailInstallation("已安装更高版本。"); break;
            default: Stage = PreviewStage.Progress; break;
        }
    }

    public void Advance()
    {
        if (Stage != PreviewStage.Progress) return;
        Progress = Math.Min(100, Progress + 2);
        if (Scenario == PreviewScenario.Failure && Progress >= 52)
            FailInstallation("无法写入程序文件，请重试。");
        else if (Progress == 100)
            Stage = PreviewStage.Complete;
    }

    public void Cancel()
    {
        if (Stage == PreviewStage.Progress) Stage = PreviewStage.Cancelled;
    }

    public void ReportProgress(double percent)
    {
        if (Stage == PreviewStage.Progress) Progress = Math.Clamp(percent, Progress, 100);
    }

    public void ReportProgress(InstallationProgress value)
    {
        if (Stage != PreviewStage.Progress) return;
        ReportProgress(value.Percent);
        Detail = value;
    }

    public void Complete()
    {
        if (Stage != PreviewStage.Progress) return;
        Progress = 100;
        Stage = PreviewStage.Complete;
    }

    public void BackToSettings()
    {
        Stage = PreviewStage.Settings;
        Progress = 0;
        PathError = "";
        LaunchAfter = false;
        ApplicationRunning = false;
    }

    public void Reset()
    {
        Scenario = PreviewScenario.Normal;
        InstallDirectory = DefaultDirectory;
        DesktopShortcut = true;
        AgreementAccepted = false;
        BackToSettings();
    }

    public void FailInstallation(string message)
    {
        ApplicationRunning = false;
        Stage = PreviewStage.Error;
        ErrorTitle = message;
    }

    public void RequireApplicationExit()
    {
        FailInstallation(new ApplicationRunningException().Message);
        ApplicationRunning = true;
    }
}

internal static class FlowText
{
    private static readonly Dictionary<string, string> Messages = ReadMessages();
    public static string Get(string key) => Messages.TryGetValue(key, out var value) ? value : key;
    private static Dictionary<string, string> ReadMessages()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlowMessages.isl")
            ?? throw new InvalidOperationException("Missing embedded flow messages.");
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var index = line.IndexOf('=');
            if (index > 0 && char.IsLetter(line[0])) result[line[..index]] = line[(index + 1)..].Replace("%n", "\n");
        }
        return result;
    }
}
