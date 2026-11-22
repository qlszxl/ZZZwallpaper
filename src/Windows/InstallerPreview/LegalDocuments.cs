using System.IO;
using System.Reflection;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record LegalDocument(string ResourceName, string Title)
{
    public override string ToString() => Title;

    // Read bundled text only; no browser, network, shell, or installed file access.
    public string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing legal document: {ResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

internal static class LegalDocuments
{
    public static readonly LegalDocument Agreement = new("Legal.UserAgreement", "用户协议");
    public static readonly LegalDocument Privacy = new("Legal.PrivacyNotice", "隐私说明");
    public static readonly IReadOnlyList<LegalDocument> ThirdParty = new LegalDocument[]
    {
        new("Legal.ThirdParty", "第三方许可清单"),
        new("Legal.Agpl", "ZZZwallpaper · GNU AGPL v3"),
        new("Legal.Scope", "ZZZwallpaper · 许可范围"),
        new("Legal.SourceDistribution", "对应源码 · 交付说明"),
        new("Legal.DotNet", ".NET Runtime · 许可"),
        new("Legal.DotNetNotices", ".NET Runtime · 第三方声明"),
        new("Legal.WindowsDesktop", "Windows Desktop Runtime · 许可"),
        new("Legal.Font", "思源黑体 · SIL OFL 1.1"),
        new("Legal.Saira", "Saira Condensed · SIL OFL 1.1"),
        new("Legal.Bungee", "Bungee · SIL OFL 1.1"),
        new("Legal.FFmpeg", "FFmpeg · 许可"),
        new("Legal.FFmpegDependencies", "FFmpeg · 依赖许可与版权声明"),
        new("Legal.Gpl", "FFmpeg / FFTW · GNU GPL v3"),
        new("Legal.TranslucentTB", "TranslucentTB ExplorerTAP · GPL v3"),
        new("Legal.Detours", "Microsoft Detours · MIT"),
        new("Legal.Wil", "Microsoft WIL · MIT"),
        new("Legal.CppWinRT", "Microsoft C++/WinRT · MIT"),
        new("Legal.FFmpegLock", "FFmpeg · 版本与来源"),
        new("Legal.WindowsAppSDK", "Windows App SDK · 许可"),
        new("Legal.WindowsAppSDKNotices", "Windows App SDK · 第三方声明"),
        new("Legal.WinUiSource", "WinUI 模板 · MIT"),
        new("Legal.WpfSource", "WPF 参考代码 · MIT"),
        new("Legal.SqliteManaged", "Microsoft.Data.Sqlite · MIT"),
        new("Legal.SqliteRaw", "SQLitePCLRaw · Apache 2.0"),
        new("Legal.NuGet", "NuGet 依赖 · 版权与原始声明"),
        new("Legal.ZXing", "ZXing.Net · Apache 2.0"),
        new("Legal.ZXingThanks", "ZXing.Net · 致谢"),
        new("Legal.Mcp", "MCP C# SDK · Apache 2.0"),
        new("Legal.ApplicationNotices", "完整软件 · 依赖与发布材料"),
        new("Legal.Inno", "Inno Setup · 安装引擎许可")
    };

    public static IEnumerable<LegalDocument> All => new[] { Agreement, Privacy }.Concat(ThirdParty);
}
