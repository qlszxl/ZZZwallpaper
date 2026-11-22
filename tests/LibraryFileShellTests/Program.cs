using System.Runtime.InteropServices;
using System.Text.Json;
using LumaWall.Application;
using LumaWall.ControlCenter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // This fixture never enumerates the recycle bin. Only its freshly created
        // unique file is passed to the production adapter.
        string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string evidence = Path.Combine(workspace, "artifacts", "release-0.1.11.38", "shell-recycle");
        Directory.CreateDirectory(evidence);
        string file = Path.Combine(evidence, "ZZZwallpaper-recycle-fixture-" + Guid.NewGuid().ToString("N") + ".txt");
        int checks = 0; nint foreground = GetForegroundWindow();
        try
        {
            if (args.Length != 1 || args[0] != "--isolated-recycle") throw new InvalidOperationException("Explicit isolated-test flag required.");
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write("ZZZwallpaper isolated recycle verification; no personal file content."u8);
            var source = LibrarySourceFiles.Inspect(file);
            var adapter = new WindowsLibrarySourceFileOperations();
            adapter.Delete(source, permanently: false);
            Check(!File.Exists(file), "Only the generated fixture source is no longer present");
            Check(adapter.RecycleVerified, "Production sink confirmed a non-null recycled-item pointer");
            Check(GetForegroundWindow() == foreground, "The silent recycle operation did not change the foreground window");
            File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new {
                passed = true, checks, source = source.Path, bytes = source.Bytes,
                recycleItemPointerVerified = adapter.RecycleVerified, sourceAbsent = !File.Exists(file),
                foregroundUnchanged = GetForegroundWindow() == foreground, recycleBinEnumerated = false,
                personalFilesTouched = false, desktopInteractionTested = false
            }, new JsonSerializerOptions {WriteIndented=true}));
            Console.WriteLine($"{checks} shell recycle checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidence, "failure.txt"), error.ToString());
            Console.Error.WriteLine(error); return 1;
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++; Console.WriteLine("PASS: " + name);
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
