using System.IO;
using System.Diagnostics;

namespace ZZZwallpaper.InstallerPreview;

internal static class UninstallCompletion
{
    internal static bool ProgramRemoved(string directory)=>!new[] {"ZZZwallpaper.exe","ZZZwallpaper.dll","ZZZwallpaper.Launcher.exe","unins000.exe","unins000.dat"}.Any(name=>File.Exists(Path.Combine(directory,name)));
    internal static async Task<bool> WaitAsync(string directory,string doneFile,TimeSpan? timeout=null)
    {
        // The Inno entry process can hand removal off to its temporary copy.
        // Keep the stop guard alive until files actually disappear, not merely
        // until the entry process exits. Older engines may not write a marker.
        var clock=Stopwatch.StartNew();int absent=0;
        while(clock.Elapsed<(timeout??TimeSpan.FromMinutes(2))) {
            if(ProgramRemoved(directory)) {
                if(File.Exists(doneFile)||++absent>=5)return true;
            } else absent=0;
            await Task.Delay(200);
        }
        return false;
    }
}
