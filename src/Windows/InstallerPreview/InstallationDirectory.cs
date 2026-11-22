using System.IO;

namespace ZZZwallpaper.InstallerPreview;

internal static class InstallationDirectory
{
    internal static string Normalize(string value) => Path.TrimEndingDirectorySeparator(value.Trim());

    internal static string FromSelectedFolder(string folder, string current)
    {
        string selected = Path.GetFullPath(folder);
        selected = Path.TrimEndingDirectorySeparator(selected);
        if ((selected != Path.GetPathRoot(selected) && string.Equals(selected, Normalize(current), StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(Path.GetFileName(selected), "ZZZwallpaper", StringComparison.OrdinalIgnoreCase)) return selected;
        return Path.Combine(selected, "ZZZwallpaper");
    }

    internal static string InitialDirectory(string current)
    {
        try {
            string? candidate = Path.IsPathFullyQualified(current) ? Path.GetFullPath(current) : null;
            while (candidate != null) {
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(candidate));
            }
        } catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}
