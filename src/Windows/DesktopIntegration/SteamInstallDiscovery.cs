using Microsoft.Win32;

namespace LumaWall.DesktopIntegration;

public static class SteamInstallDiscovery
{
    // Candidate paths only: no directory enumeration and no Steam process/API.
    // Callers may instead pass explicit test roots to the shared catalog.
    public static string[] CandidateRoots()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (Path.IsPathFullyQualified(path)) candidates.Add(Path.GetFullPath(path)); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var key = root.OpenSubKey(@"Software\Valve\Steam", false);
                    Add(key?.GetValue("SteamPath") as string); Add(key?.GetValue("InstallPath") as string);
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path)) Add(Path.Combine(path, "Steam"));
        }
        return candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
