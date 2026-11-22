using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record ProductIdentity(string Version, string DisplayVersion, string Publisher,
    string SupportUrl, string AgreementVersion)
{
    public const string AppId = "D82EA01A-D414-4393-BE4D-7AD3371671C0";
    public static ProductIdentity Current { get; } = Read<ProductIdentity>("Product.json");
    public static T Read<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing package resource: {name}");
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Invalid package resource: {name}");
    }
}

internal sealed record EnginePackageInfo(string Version, string EngineSha256, long PayloadBytes,
    int PayloadFiles, string PayloadManifestSha256, bool CleanPackage, string? SourceCommit = null)
{
    public static bool IsBundled => Assembly.GetExecutingAssembly().GetManifestResourceInfo("Installer.Engine") is not null;
    public static EnginePackageInfo Read() => ProductIdentity.Read<EnginePackageInfo>("Installer.PackageInfo");
}
