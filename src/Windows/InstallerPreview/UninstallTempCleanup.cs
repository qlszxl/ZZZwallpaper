using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZZZwallpaper.InstallerPreview;

internal sealed record UninstallTempOwner(string AppId, string Token, string Sha256, int ProcessId,
    long StartTicks, string InstallDirectory, bool ProgramRemoved = false);

// A hidden system PowerShell process outlives the mapped uninstaller EXE. It
// deletes only the named copy and marker, then attempts non-recursive rmdir.
internal static class UninstallTempCleanup
{
    internal const string AppId = "D82EA01A-D414-4393-BE4D-7AD3371671C0";
    internal const string Prefix = "ZZZwallpaper-Uninstall-";
    internal const string Marker = "cleanup-owner.json";
    internal const string Executable = "ZZZwallpaper.Uninstall.exe";

    internal static void Prepare(string directory, string installDirectory)
    {
        using var process = Process.GetCurrentProcess();
        using var file = File.OpenRead(Path.Combine(directory, Executable));
        Write(directory, new(AppId, Guid.NewGuid().ToString("N"), Convert.ToHexString(SHA256.HashData(file)),
            process.Id, process.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(installDirectory)));
    }

    private static void Write(string directory, UninstallTempOwner owner) =>
        File.WriteAllText(Path.Combine(directory, Marker), JsonSerializer.Serialize(owner));

    internal static UninstallTempOwner? Read(string directory, string temporaryRoot)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot)), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith(Prefix, StringComparison.Ordinal)) return null;
            RequireNoLinks(full);
            RequireNoLinks(Path.Combine(full, Marker));
            var owner = JsonSerializer.Deserialize<UninstallTempOwner>(File.ReadAllText(Path.Combine(full, Marker)));
            return owner is { AppId: AppId, Token.Length: 32, Sha256.Length: 64 } ? owner : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    private static void RequireNoLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked cleanup paths are preserved.");
    }

    internal static bool OwnerRunning(UninstallTempOwner owner)
    {
        if(owner.ProcessId<=0)return false;
        try { using var process = Process.GetProcessById(owner.ProcessId); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == owner.StartTicks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    internal static void TrackCurrentProcess()
    {
        try {
        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var owner = Read(directory, Path.GetTempPath());
        if (owner == null) return;
        using var process = Process.GetCurrentProcess();
        owner = owner with { ProcessId = process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks };
        Write(directory, owner);
        Schedule(directory, Path.GetTempPath(), owner);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex); }
    }

    internal static void MarkProgramRemoved(string installDirectory)
    {
        try {
        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var owner = Read(directory, Path.GetTempPath());
        if (owner != null && string.Equals(owner.InstallDirectory, Path.GetFullPath(installDirectory), StringComparison.OrdinalIgnoreCase))
            Write(directory, owner with { ProgramRemoved = true });
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }

    internal static void Sweep(string? temporaryRoot = null)
    {
        temporaryRoot ??= Path.GetTempPath();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(temporaryRoot, Prefix + "*", SearchOption.TopDirectoryOnly))
                if (Read(directory, temporaryRoot) is { } owner && !OwnerRunning(owner)) Schedule(directory, temporaryRoot, owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }

    internal static void Abandon(string directory, string? temporaryRoot=null)
    {
        // Called only if copying/starting this newly allocated directory failed.
        // No process has successfully started, so even an incomplete copy with
        // no marker can be reclaimed without a background helper.
        try {
            var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if(!string.Equals(Path.GetDirectoryName(full),Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot??Path.GetTempPath())),StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith(Prefix,StringComparison.Ordinal))return;
            RequireNoLinks(full);
            foreach(var name in new[]{Executable,Marker}) {var file=Path.Combine(full,name);RequireNoLinks(file);File.Delete(file);}
            if(!Directory.EnumerateFileSystemEntries(full).Any())Directory.Delete(full,false);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }

    internal static ProcessStartInfo CreateStart(string directory, string temporaryRoot, UninstallTempOwner owner)
    {
        using var stream = typeof(UninstallTempCleanup).Assembly.GetManifestResourceStream("UninstallTempCleanup.ps1")!;
        using var reader = new StreamReader(stream);
        // Encode data separately; no file path or user text is interpolated as code.
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { directory, temporaryRoot, owner.Token, owner.Sha256, owner.InstallDirectory })));
        var script = "$cleanupData='" + data + "'\n" + reader.ReadToEnd();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = temporaryRoot };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        return start;
    }

    private static void Schedule(string directory, string temporaryRoot, UninstallTempOwner owner)
    {
        try { using var process = Process.Start(CreateStart(directory, temporaryRoot, owner)); }
        // Restricted system scripting must not block uninstall; the marker is
        // retained for a later installer/uninstaller startup to retry.
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine(ex); }
    }
}
