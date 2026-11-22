using System.Security.Cryptography;
using LumaWall.Contracts.Steam;

namespace LumaWall.Application.Steam;

public sealed partial class SteamWallpaperCatalog
{
    public Task<SteamPreparedImport> PrepareAssetAsync(SteamWallpaperAsset asset, string? copyDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Task.Run(() => PrepareAsset(asset, copyDirectory, cancellationToken), cancellationToken);
    }

    public Task<byte[]?> ReadPreviewAsync(SteamWallpaperAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!asset.CanImport || !asset.IsImage) return null;
            var current = CurrentAsset(asset, cancellationToken);
            using var source = OpenAssetSource(current);
            using var range = OpenAssetRange(current, source, cancellationToken);
            byte[] bytes;
            if (current.Kind == SteamAssetKind.Texture)
            {
                var texture = SteamTextureReader.Inspect(range, cancellationToken);
                if (texture.Encoded && texture.DecodedBytes > 16 * 1024 * 1024) return null;
                bytes = SteamTextureReader.ReadImage(range, texture, true, cancellationToken);
            }
            else
            {
                if (range.Length > 16 * 1024 * 1024) return null;
                bytes = new byte[checked((int)range.Length)];
                SteamTextureReader.ReadExactly(range, bytes, cancellationToken);
            }
            VerifyAssetStamp(current);
            return bytes;
        }, cancellationToken);
    }

    private static async Task<SteamPreparedImport> PrepareAsset(SteamWallpaperAsset selected, string? copyDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = CurrentAsset(selected, token);
        if (current.RequiresDestination && copyDirectory == null)
            throw new InvalidDataException("包内素材或纹理需要选择保存目录。");
        string sourcePath = current.PackagePath ?? current.MediaPath!;
        using var source = OpenAssetSource(current);
        using var range = OpenAssetRange(current, source, token);
        if (copyDirectory == null)
        {
            token.ThrowIfCancellationRequested();
            VerifyAssetStamp(current);
            return Result(sourcePath, false, false, null);
        }
        string target = SafePath(copyDirectory);
        CheckPath(target);
        string directory = current.Project.ProjectDirectory;
        if (Paths.Equals(target, directory) || target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            SteamSourceIdentity.IsManagedPath(Path.Combine(target, "media.mp4")))
            throw new IOException("请选择 Steam 和原项目之外的目录。");
        // Decode before creating output files; selection and thumbnails never extract to disk.
        using var decoded = current.Kind == SteamAssetKind.Texture ?
            new MemoryStream(SteamTextureReader.ReadImage(range, SteamTextureReader.Inspect(range, token), false, token), false) : null;
        Stream input = decoded ?? (Stream)range;
        long expectedBytes = input.Length;
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(target); CheckPath(target);
        string temporary = Path.Combine(target, ".zzzwallpaper-steam-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            string hash;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920]; long bytes = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    bytes += count;
                    if (bytes > expectedBytes) throw new IOException("素材在读取时变化。");
                    digest.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if (bytes != expectedBytes) throw new IOException("素材读取不完整。");
                await output.FlushAsync(token); output.Flush(true);
                hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            }
            token.ThrowIfCancellationRequested();
            CheckPath(target); VerifyAssetStamp(current);
            string destination = Path.Combine(target, hash + current.OutputExtension);
            bool reused = false;
            if (File.Exists(destination))
            {
                await VerifyCopy(destination, hash, expectedBytes, token); token.ThrowIfCancellationRequested(); reused = true;
            }
            else
            {
                try { File.Move(temporary, destination, false); }
                catch (IOException) when (File.Exists(destination))
                { await VerifyCopy(destination, hash, expectedBytes, token); token.ThrowIfCancellationRequested(); reused = true; }
            }
            return Result(destination, true, reused, hash);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        SteamPreparedImport Result(string path, bool copied, bool reused, string? hash) => new(path,
            new SteamWallpaperSource(current.SourceId, current.Project.ProjectDirectory, current.Project.WorkshopId, current.Project.Type,
                sourcePath, path, copied, hash)
            {
                Bytes = current.Bytes, ModifiedUtc = current.ModifiedUtc, ProjectSourceId = current.Project.SourceId,
                RelativePath = current.RelativePath, PackagePath = current.PackagePath
            }, copied, reused);
    }

    private static FileStream OpenAssetSource(SteamWallpaperAsset asset)
    {
        string path = asset.PackagePath ?? asset.MediaPath!;
        CheckPath(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (file.Length != asset.ContainerBytes) { file.Dispose(); throw new IOException("素材已变化，请重新扫描。"); }
        return file;
    }
    private static SteamAssetRangeStream OpenAssetRange(SteamWallpaperAsset asset, Stream source, CancellationToken token)
    {
        if (asset.PackagePath == null) return new(source, 0, source.Length);
        var entry = SteamPackageReader.Read(source, token).SingleOrDefault(e => Paths.Equals(e.Name, asset.PackageEntry))
            ?? throw new IOException("包内素材已变化，请重新扫描。");
        if (entry.Bytes != asset.Bytes) throw new IOException("包内素材已变化，请重新扫描。");
        return new(source, entry.Offset, entry.Bytes);
    }
    private static void VerifyAssetStamp(SteamWallpaperAsset asset)
    {
        string path = asset.PackagePath ?? asset.MediaPath!;
        CheckPath(path);
        var stamp = new FileInfo(path);
        if (!stamp.Exists || stamp.Length != asset.ContainerBytes || stamp.LastWriteTimeUtc != asset.ModifiedUtc)
            throw new IOException("素材已变化，请重新扫描。");
    }
}
