using System.Security.Cryptography;
using LumaWall.Contracts.Steam;

namespace LumaWall.Application.Steam;

public sealed partial class SteamWallpaperCatalog
{
    public Task<SteamPreparedImport> PrepareAsync(SteamWallpaperProject project, string? copyDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Task.Run(() => Prepare(project, copyDirectory, cancellationToken), cancellationToken);
    }

    private static async Task<SteamPreparedImport> Prepare(SteamWallpaperProject selected, string? copyDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = SafePath(selected.ProjectDirectory);
        var current = ReadProject(directory, token);
        if (current.Status != SteamProjectStatus.Supported || current.MediaPath == null)
            throw new InvalidDataException(current.Message);
        if (selected.Status != SteamProjectStatus.Supported || current.SourceId != selected.SourceId || current.Type != selected.Type ||
            !Paths.Equals(current.MediaPath, selected.MediaPath) || current.Bytes != selected.Bytes || current.ModifiedUtc != selected.ModifiedUtc)
            throw new IOException("项目文件已变化，请重新扫描后再导入。");
        string source = current.MediaPath;
        CheckPath(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (input.Length != current.Bytes) throw new IOException("项目主素材已变化，请重新扫描。");
        if (copyDirectory == null)
        {
            token.ThrowIfCancellationRequested();
            return Result(source, false, false, null);
        }
        string target = SafePath(copyDirectory);
        CheckPath(target);
        // A copy should survive removal or unsubscribe of the original project.
        if (Paths.Equals(target, directory) || target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            SteamSourceIdentity.IsManagedPath(Path.Combine(target, "media.mp4")))
            throw new IOException("复制目标应位于 Steam 和原项目目录之外。");
        Directory.CreateDirectory(target);
        CheckPath(target);
        string temporary = Path.Combine(target, ".zzzwallpaper-steam-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            string hash;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long bytes = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    bytes += count;
                    if (bytes > current.Bytes) throw new IOException("项目主素材在复制时变化。");
                    digest.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if (bytes != current.Bytes) throw new IOException("项目主素材在复制时变化。");
                await output.FlushAsync(token);
                output.Flush(true);
                hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            }
            token.ThrowIfCancellationRequested();
            CheckPath(target); CheckPath(source);
            if (new FileInfo(source).LastWriteTimeUtc != current.ModifiedUtc) throw new IOException("项目主素材在复制时变化，请重新扫描。");
            string destination = Path.Combine(target, hash + Path.GetExtension(source).ToLowerInvariant());
            bool reused = false;
            if (File.Exists(destination))
            {
                await VerifyCopy(destination, hash, current.Bytes, token);
                token.ThrowIfCancellationRequested();
                reused = true;
            }
            else
            {
                try { File.Move(temporary, destination, false); }
                catch (IOException) when (File.Exists(destination))
                {
                    await VerifyCopy(destination, hash, current.Bytes, token);
                    token.ThrowIfCancellationRequested();
                    reused = true;
                }
            }
            // Once the atomic move commits, cancellation must not report failure
            // for a successfully prepared file. No existing file is overwritten.
            return Result(destination, true, reused, hash);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        SteamPreparedImport Result(string path, bool copied, bool reused, string? hash) => new(path,
            new SteamWallpaperSource(current.SourceId, directory, current.WorkshopId, current.Type, source, path, copied, hash)
            { Bytes = current.Bytes, ModifiedUtc = current.ModifiedUtc }, copied, reused);
    }

    private static async Task VerifyCopy(string path, string hash, long size, CancellationToken token)
    {
        CheckPath(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (file.Length != size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("目标目录存在同名但内容不同的文件，未覆盖，请选择另一目录。");
    }
}
