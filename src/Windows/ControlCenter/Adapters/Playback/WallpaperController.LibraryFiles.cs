using LumaWall.Contracts;
using LumaWall.Application;

namespace LumaWall.ControlCenter;

public sealed partial class WallpaperController
{
    private readonly HashSet<string> blockedSourceFiles = new(StringComparer.OrdinalIgnoreCase);
    public IDisposable BlockSourceFile(string path)
    {
        string full = LibrarySourceFiles.Normalize(path);
        if (!blockedSourceFiles.Add(full)) throw new InvalidOperationException(UiLanguage.Text("此文件正在处理，请稍后重试。"));
        return new SourceFileBlock(() => blockedSourceFiles.Remove(full));
    }
    private sealed class SourceFileBlock(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
    private void CheckSourceFileAvailable(string path)
    {
        if (blockedSourceFiles.Contains(LibrarySourceFiles.Normalize(path)))
            throw new OperationCanceledException(UiLanguage.Text("此素材正在执行文件操作。"));
    }
    public async Task<string[]> StopSourceFileAsync(string path)
    {
        var ids = store.Assets().Where(a => LibrarySourceFiles.SamePath(a.Path, path)).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        string[] screens = StopSourceFile(path);
        // Cancellation of a decoder preparation completes asynchronously. Wait
        // until Apply's finally has disposed it before invoking the file shell.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (pendingEffects.Values.Any(p => ids.Contains(p.AssetId)))
        {
            if (DateTime.UtcNow >= deadline) throw new IOException(UiLanguage.Text("壁纸仍在释放文件，请稍后重试；源文件和素材记录均保留。"));
            await Task.Delay(25);
        }
        return screens;
    }
    /// <summary>Release file users without removing persisted bindings on failure.</summary>
    public string[] StopSourceFile(string path)
    {
        var ids = store.Assets().Where(a => LibrarySourceFiles.SamePath(a.Path, path)).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        string[] screens = running.Where(p => LibrarySourceFiles.SamePath(p.Value.Asset.Path, path)).Select(p => p.Key)
            .Concat(pendingEffects.Where(p => ids.Contains(p.Value.AssetId)).Select(p => p.Key))
            .Concat(store.Bindings().Where(b => ids.Contains(b.AssetId)).Select(b => b.ScreenId))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (Preview is { } preview && LibrarySourceFiles.SamePath(preview.Asset.Path, path) ||
            preparingPreview is { } preparation && ids.Contains(preparation.AssetId))
            EndPreview();
        foreach (string screen in screens) CancelWallpaper(screen, preserveBinding: true);
        return screens;
    }
}
