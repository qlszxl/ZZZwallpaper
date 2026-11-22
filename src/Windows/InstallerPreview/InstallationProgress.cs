using System.Globalization;
using System.IO;
using System.Text;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record InstallationProgress(double Percent, string Stage = "正在安装文件", string File = "", int CompletedFiles = 0, int TotalFiles = 0)
{
    internal static Dictionary<string, string> ReadStatus(string path)
    {
        var result = new Dictionary<string, string>();
        try
        {
            // The writer atomically replaces this file; keep delete sharing open.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            while (reader.ReadLine() is { } line)
            {
                var split = line.IndexOf('=');
                if (split > 0) result[line[..split]] = line[(split + 1)..];
            }
        }
        catch (IOException) { }
        return result;
    }
    internal static InstallationProgress? FromStatus(Dictionary<string, string> state)
    {
        if (!state.ContainsKey("completed") || !state.ContainsKey("total")) return null;
        int.TryParse(state.GetValueOrDefault("completed"), out var completed);
        int.TryParse(state.GetValueOrDefault("total"), out var total);
        var stage = state.GetValueOrDefault("stage") == "finishing" ? "正在完成安装" : "正在安装文件";
        return new(total>0?Math.Clamp(100d*completed/total,0,100):0, stage,
            state.GetValueOrDefault("file", ""), Math.Max(0, completed), Math.Max(0, total));
    }
}
