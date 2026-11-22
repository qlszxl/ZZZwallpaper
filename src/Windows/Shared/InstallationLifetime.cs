using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LumaWall.Lifecycle;

// Shared by the application, stdio bridge and uninstaller. A manual-reset event
// stays signalled for the entire uninstall, including attempts to restart MCP.
internal sealed class InstallationLifetime : IDisposable
{
    private readonly EventWaitHandle signal;
    private RegisteredWaitHandle? registration;
    internal static string SignalName(string directory)
    {
        var path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return @"Local\ZZZwallpaper.Stop." + WindowsIdentity.GetCurrent().User!.Value + "." + hash;
    }
    internal InstallationLifetime(string directory) =>
        signal = new EventWaitHandle(false, EventResetMode.ManualReset, SignalName(directory));
    internal bool IsStopping => signal.WaitOne(0);
    internal void Listen(Action stop) => registration = ThreadPool.RegisterWaitForSingleObject(
        signal, (_, _) => stop(), null, Timeout.Infinite, executeOnlyOnce: true);
    internal void RequestStop() => signal.Set();
    internal void Reset() => signal.Reset();
    public void Dispose() { registration?.Unregister(null); signal.Dispose(); }
}
