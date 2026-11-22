using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using LumaWall.Application.Steam;
using LumaWall.Contracts.Steam;

static partial class SteamAssetChecks
{
    private static async Task TestTextures(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string directory = Project(root, "Textures", "scene");
        byte[] rgba = [
            255,0,0,255, 0,255,0,128, 1,1,1,255, 1,1,1,255,
            0,0,255,0, 255,255,255,255, 1,1,1,255, 1,1,1,255,
            1,1,1,255, 1,1,1,255, 1,1,1,255, 1,1,1,255,
            1,1,1,255, 1,1,1,255, 1,1,1,255, 1,1,1,255 ];
        byte[] cropped = [255,0,0,255, 0,255,0,128, 0,0,255,0, 255,255,255,255];
        byte[] opaqueRed = [0, 248, 31, 0, 0, 0, 0, 0];
        byte[] transparent = [0, 0, 255, 255, 255, 255, 255, 255];
        byte[] dxt3 = [136,136,136,136,136,136,136,136, 224,7,0,0, 0,0,0,0];
        // DXT5: all alpha indices are 2, so interpolation yields 218 from 255 and 0.
        ulong alphaIndices = 0; for (int i = 0; i < 16; i++) alphaIndices |= 2UL << (i * 3);
        byte[] dxt5 = new byte[16]; dxt5[0] = 255;
        for (int i = 0; i < 6; i++) dxt5[2 + i] = (byte)(alphaIndices >> (i * 8));
        opaqueRed.CopyTo(dxt5, 8);
        byte[] literalLz4 = new byte[66]; literalLz4[0] = 240; literalLz4[1] = 49; rgba.CopyTo(literalLz4, 2);
        byte[] matchLz4 = [79, 3, 4, 5, 255, 4, 0, 41];
        byte[] version1 = Texture(rgba, version: 1, imageWidth: 2, imageHeight: 2);
        byte[] version4 = Texture(rgba, version: 4, imageWidth: 2, imageHeight: 2);
        File.WriteAllBytes(Path.Combine(directory, "scene.pkg"), Package(
            ("rgba.tex", Texture(rgba, imageWidth: 2, imageHeight: 2)),
            ("lz4.tex", Texture(literalLz4, compressed: true, decoded: 64, imageWidth: 2, imageHeight: 2)),
            ("lz4-match.tex", Texture(matchLz4, compressed: true, decoded: 64)),
            ("dxt1.tex", Texture(opaqueRed, format: 7, imageWidth: 3, imageHeight: 2)),
            ("dxt1-alpha.tex", Texture(transparent, format: 7)),
            ("dxt3.tex", Texture(dxt3, format: 6)),
            ("dxt5.tex", Texture(dxt5, format: 4)),
            ("r8.tex", Texture([0, 80, 160, 255], format: 9, width: 2, height: 2)),
            ("v1.tex", version1), ("v4.tex", version4)));
        var project = (await catalog.DiscoverAsync([directory])).Projects.Single();
        var found = await catalog.InspectAssetsAsync(project);
        check(found.Issues.Length == 0 && found.Assets.Length == 10 && found.Assets.All(a => a.CanImport && a.Kind == SteamAssetKind.Texture),
            "Static TEX v1/v2/v3/v4 containers and common RGBA/R8/DXT formats are selectable without extraction");
        string target = Path.Combine(root, "Decoded");
        var first = found.Assets.Single(a => a.PackageEntry == "rgba.tex");
        var prepared = await catalog.PrepareAssetAsync(first, target);
        var pixels = ReadPng(File.ReadAllBytes(prepared.Path));
        check(pixels.Width == 2 && pixels.Height == 2 && pixels.Pixels.SequenceEqual(cropped),
            "RGBA texture extraction preserves channel order and transparency while cropping padded texture bounds");
        var lz4 = await catalog.PrepareAssetAsync(found.Assets.Single(a => a.PackageEntry == "lz4.tex"), target);
        check(lz4.Reused && lz4.Path == prepared.Path, "LZ4 literal decoding yields the same content-addressed PNG as uncompressed texture pixels");
        var match = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "lz4-match.tex")) ?? []);
        check(match.Pixels.Chunk(4).All(p => p.SequenceEqual(new byte[] { 3, 4, 5, 255 })),
            "LZ4 overlapping match decoding reproduces repeated pixel sequences");
        var block = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "dxt1.tex")) ?? []);
        check(block.Width == 3 && block.Height == 2 && block.Pixels.Chunk(4).All(p => p.SequenceEqual(new byte[] { 255, 0, 0, 255 })),
            "DXT1 decoding handles non-block-aligned output and reconstructs red RGB565 pixels");
        var alpha = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "dxt1-alpha.tex")) ?? []);
        check(alpha.Pixels.All(b => b == 0), "DXT1 transparent selector retains transparent black pixels");
        var explicitAlpha = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "dxt3.tex")) ?? []);
        check(explicitAlpha.Pixels.Chunk(4).All(p => p.SequenceEqual(new byte[] { 0, 255, 0, 136 })), "DXT3 reconstructs green colors and explicit four-bit alpha");
        var interpolated = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "dxt5.tex")) ?? []);
        check(interpolated.Pixels.Chunk(4).All(p => p.SequenceEqual(new byte[] { 255, 0, 0, 218 })), "DXT5 reconstructs its interpolated alpha palette independently of the color block");
        var gray = ReadPng(await catalog.ReadPreviewAsync(found.Assets.Single(a => a.PackageEntry == "r8.tex")) ?? []);
        check(gray.Pixels.SequenceEqual(new byte[] { 0,0,0,255, 80,80,80,255, 160,160,160,255, 255,255,255,255 }),
            "R8 texture conversion emits opaque grayscale pixels");
        check((await catalog.PrepareAssetAsync(found.Assets.Single(a => a.PackageEntry == "v1.tex"), target)).Path == prepared.Path &&
            (await catalog.PrepareAssetAsync(found.Assets.Single(a => a.PackageEntry == "v4.tex"), target)).Path == prepared.Path,
            "Older and newer static texture container headers decode to identical image bytes");
        check(Directory.GetFiles(target).Length == 1,
            "Memory-only texture preview does not leave additional extracted files or thumbnail caches");

        File.WriteAllBytes(Path.Combine(directory, "loose.tex"), Texture(rgba, imageWidth: 2, imageHeight: 2));
        var loose = (await catalog.InspectAssetsAsync(project)).Assets.Single(a => a.RelativePath == "loose.tex");
        await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(loose), check,
            "Even an unpacked TEX requires conversion to a chosen destination rather than a non-playable reference");
        check((await catalog.PrepareAssetAsync(loose, target)).Path == prepared.Path,
            "Loose textures and packed textures reuse the same decoded output content");
        byte[] encodedPng = File.ReadAllBytes(prepared.Path);
        File.WriteAllBytes(Path.Combine(directory, "embedded.tex"), Texture(encodedPng, imageFormat: 13, width: 2, height: 2));
        var encoded = (await catalog.InspectAssetsAsync(project)).Assets.Single(a => a.RelativePath == "embedded.tex");
        check((await catalog.PrepareAssetAsync(encoded, target)).Path == prepared.Path &&
            (await catalog.ReadPreviewAsync(encoded))!.SequenceEqual(encodedPng),
            "Embedded PNG textures preserve their original encoded bytes without transcoding");
        File.WriteAllBytes(Path.Combine(directory, "large.tex"), Texture(new byte[640 * 320 * 4], width: 640, height: 320));
        var large = (await catalog.InspectAssetsAsync(project)).Assets.Single(a => a.RelativePath == "large.tex");
        var thumbnail = ReadPng(await catalog.ReadPreviewAsync(large) ?? []);
        check(thumbnail.Width == 320 && thumbnail.Height == 160 && Directory.GetFiles(target).Length == 1,
            "Raw texture thumbnail generation is bounded to 320 pixels and stays entirely in memory");

        await TestMalformedTextures(root, catalog, check);
        await TestEncodedHeaders(root, catalog, encodedPng, check);
    }

    private static async Task TestMalformedTextures(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string directory = Project(root, "Invalid textures", "scene");
        File.WriteAllBytes(Path.Combine(directory, "animation.tex"), Texture(new byte[64], flags: 4));
        File.WriteAllBytes(Path.Combine(directory, "video.tex"), Texture(new byte[64], flags: 32));
        File.WriteAllBytes(Path.Combine(directory, "unknown-format.tex"), Texture(new byte[64], format: 42));
        File.WriteAllBytes(Path.Combine(directory, "rg88.tex"), Texture(new byte[32], format: 8));
        File.WriteAllBytes(Path.Combine(directory, "multiframe.tex"), Texture(new byte[64], imageCount: 2));
        File.WriteAllBytes(Path.Combine(directory, "huge.tex"), Texture(new byte[64], width: 100000, height: 100000));
        File.WriteAllBytes(Path.Combine(directory, "short.tex"), Texture(new byte[64])[..^1]);
        File.WriteAllBytes(Path.Combine(directory, "crop.tex"), Texture(new byte[64], imageWidth: 5));
        byte[] badVersion = Texture(new byte[64]); badVersion[7] = (byte)'9';
        File.WriteAllBytes(Path.Combine(directory, "version.tex"), badVersion);
        byte[] badLength = Texture(new byte[64]); BinaryPrimitives.WriteInt32LittleEndian(badLength.AsSpan(83), int.MaxValue);
        File.WriteAllBytes(Path.Combine(directory, "length.tex"), badLength);
        File.WriteAllBytes(Path.Combine(directory, "bad-lz4.tex"), Texture([0, 0, 0], compressed: true, decoded: 64));
        File.WriteAllBytes(Path.Combine(directory, "bad-signature.tex"), Texture([1, 2, 3, 4], imageFormat: 13));
        File.WriteAllBytes(Path.Combine(directory, "scene.pkg"), Package(("cross-entry.tex", Texture(new byte[64])[..^4]), ("next.png", new byte[] { 1, 2, 3, 4 })));
        var project = (await catalog.DiscoverAsync([directory])).Projects.Single();
        var found = await catalog.InspectAssetsAsync(project);
        check(found.Assets.Count(a => a.Status == SteamProjectStatus.Unsupported) == 6 &&
            found.Assets.Count(a => a.Status == SteamProjectStatus.Invalid) == 6 &&
            found.Assets.Where(a => !a.CanImport).All(a => !string.IsNullOrWhiteSpace(a.Message)),
            "Animated/video/unknown textures show explicit limitations; huge, truncated, miscropped and cross-entry headers are rejected");
        check(found.Assets.Single(a => a.PackageEntry == "next.png").CanImport,
            "A malformed texture cannot read the neighboring package entry or prevent unrelated media selection");
        string target = Path.Combine(root, "No invalid output");
        await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(found.Assets.Single(a => a.RelativePath == "bad-lz4.tex"), target),
            check, "Malformed LZ4 with a zero back-reference is rejected before any output is created");
        await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(found.Assets.Single(a => a.RelativePath == "bad-signature.tex"), target),
            check, "Embedded image payload with mismatched signature is rejected before any output is created");
        check(!Directory.Exists(target), "Texture decode failures do not create a destination or leave partial output");
        await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(found.Assets.Single(a => a.RelativePath == "animation.tex"), target),
            check, "Unsupported texture records cannot bypass validation through the preparation API");
    }

    private static async Task TestEncodedHeaders(string root, SteamWallpaperCatalog catalog, byte[] png, Action<bool, string> check)
    {
        string directory = Project(root, "Encoded dimensions", "scene");
        byte[] hugePng = (byte[])png.Clone();
        BinaryPrimitives.WriteInt32BigEndian(hugePng.AsSpan(16), 32768);
        BinaryPrimitives.WriteInt32BigEndian(hugePng.AsSpan(20), 32768);
        File.WriteAllBytes(Path.Combine(directory, "huge-png.tex"), Texture(hugePng, imageFormat: 13));
        File.WriteAllBytes(Path.Combine(directory, "huge-jpeg.tex"), Texture(JpegHeader(65535, 65535), imageFormat: 2));
        File.WriteAllBytes(Path.Combine(directory, "huge-bmp.tex"), Texture(BmpHeader(32768, 32768), imageFormat: 0));
        File.WriteAllBytes(Path.Combine(directory, "mismatched.tex"), Texture(png, imageFormat: 13));
        File.WriteAllBytes(Path.Combine(directory, "cropped.tex"), Texture(png, imageFormat: 13, imageWidth: 2, imageHeight: 2));
        File.WriteAllBytes(Path.Combine(directory, "tiff.tex"), Texture([73,73,42,0,0,0,0,0], imageFormat: 18));
        File.WriteAllBytes(Path.Combine(directory, "truncated.tex"), Texture(png[..24], imageFormat: 13));
        File.WriteAllBytes(Path.Combine(directory, "valid-png.tex"), Texture(png, width: 2, height: 2, imageFormat: 13));
        File.WriteAllBytes(Path.Combine(directory, "valid-jpeg-header.tex"), Texture(JpegHeader(4, 4), imageFormat: 2));
        File.WriteAllBytes(Path.Combine(directory, "valid-bmp.tex"), Texture(BmpHeader(4, -4), imageFormat: 0));
        File.WriteAllBytes(Path.Combine(directory, "compressed-png.tex"),
            Texture(Lz4Literal(png), width: 2, height: 2, imageFormat: 13, compressed: true, decoded: png.Length));
        File.WriteAllBytes(Path.Combine(directory, "compressed-huge.tex"),
            Texture(Lz4Literal(hugePng), imageFormat: 13, compressed: true, decoded: hugePng.Length));
        var project = (await catalog.DiscoverAsync([directory])).Projects.Single();
        var found = await catalog.InspectAssetsAsync(project);
        check(new[] { "huge-png.tex", "huge-jpeg.tex", "huge-bmp.tex", "compressed-huge.tex" }.All(name =>
            found.Assets.Single(a => a.RelativePath == name).Status == SteamProjectStatus.Invalid),
            "Inspection rejects oversized inner PNG/JPEG/BMP dimensions even when the outer TEX header claims 4 by 4 pixels");
        check(found.Assets.Single(a => a.RelativePath == "mismatched.tex").Status == SteamProjectStatus.Invalid &&
            found.Assets.Single(a => a.RelativePath == "truncated.tex").Status == SteamProjectStatus.Invalid,
            "Embedded dimensions must match the displayed texture and truncated image headers are rejected at inspection");
        check(found.Assets.Single(a => a.RelativePath == "cropped.tex").Status == SteamProjectStatus.Unsupported &&
            found.Assets.Single(a => a.RelativePath == "tiff.tex").Status == SteamProjectStatus.Unsupported,
            "Encoded crops and TIFF are visibly unsupported instead of exporting a padded or unchecked image");
        check(new[] { "valid-png.tex", "valid-jpeg-header.tex", "valid-bmp.tex", "compressed-png.tex" }.All(name =>
            found.Assets.Single(a => a.RelativePath == name).CanImport),
            "Bounded PNG/JPEG/BMP headers and compressed PNG prefixes remain inspectable, including top-down BMP");
        var compressed = found.Assets.Single(a => a.RelativePath == "compressed-png.tex");
        check((await catalog.ReadPreviewAsync(compressed))!.SequenceEqual(png),
            "Header-only LZ4 inspection and complete preview decoding agree on the original PNG bytes");
        string target = Path.Combine(root, "No oversized encoded output");
        foreach (var asset in found.Assets.Where(a => !a.CanImport))
        {
            await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(asset, target), check,
                "Rejected encoded material cannot be extracted: " + asset.RelativePath);
            check(await catalog.ReadPreviewAsync(asset) == null, "Rejected encoded material is not passed to the thumbnail decoder: " + asset.RelativePath);
        }
        check(!Directory.Exists(target), "Oversized/mismatched encoded image validation never creates output");
    }

    private static byte[] Lz4Literal(byte[] bytes)
    {
        using var stream = new MemoryStream();
        stream.WriteByte((byte)(Math.Min(bytes.Length, 15) << 4));
        if (bytes.Length >= 15)
        {
            int remaining = bytes.Length - 15;
            while (remaining >= 255) { stream.WriteByte(255); remaining -= 255; }
            stream.WriteByte((byte)remaining);
        }
        stream.Write(bytes); return stream.ToArray();
    }
    private static byte[] JpegHeader(int width, int height)
    {
        byte[] result = [255,216, 255,192, 0,11, 8, 0,0, 0,0, 1, 1,17,0, 255,217];
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(7), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(9), (ushort)width);
        return result;
    }
    private static byte[] BmpHeader(int width, int height)
    {
        byte[] result = new byte[118]; result[0] = (byte)'B'; result[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), result.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(28), 32);
        return result;
    }

    private static byte[] Texture(byte[] data, int format = 0, int flags = 0, int width = 4, int height = 4,
        int imageWidth = 0, int imageHeight = 0, int version = 3, int imageFormat = -1, bool compressed = false, int decoded = 0, int imageCount = 1)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(Encoding.ASCII.GetBytes("TEXV0005\0TEXI0001\0"));
        writer.Write(format); writer.Write(flags); writer.Write(width); writer.Write(height);
        writer.Write(imageWidth == 0 ? width : imageWidth); writer.Write(imageHeight == 0 ? height : imageHeight); writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("TEXB" + version.ToString("D4") + "\0"));
        writer.Write(imageCount);
        if (version >= 3) writer.Write(imageFormat);
        if (version == 4) writer.Write(0);
        writer.Write(1);
        writer.Write(width); writer.Write(height);
        if (version >= 2) { writer.Write(compressed ? 1 : 0); writer.Write(decoded); }
        writer.Write(data.Length); writer.Write(data);
        return output.ToArray();
    }
    private static (int Width, int Height, byte[] Pixels) ReadPng(byte[] png)
    {
        if (!png.AsSpan().StartsWith(new byte[] { 137,80,78,71,13,10,26,10 })) throw new Exception("Invalid PNG signature");
        int width = 0, height = 0, position = 8;
        using var blocks = new MemoryStream();
        while (position < png.Length)
        {
            int count = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            string type = Encoding.ASCII.GetString(png, position + 4, 4);
            uint checksum = uint.MaxValue;
            foreach (byte item in png.AsSpan(position + 4, 4 + count))
            {
                checksum ^= item;
                for (int bit = 0; bit < 8; bit++) checksum = (checksum & 1) == 0 ? checksum >> 1 : (checksum >> 1) ^ 0xEDB88320;
            }
            if (~checksum != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + count))) throw new Exception("Invalid PNG CRC");
            if (type == "IHDR") { width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position + 8)); height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position + 12)); }
            if (type == "IDAT") blocks.Write(png, position + 8, count);
            position += count + 12;
        }
        blocks.Position = 0;
        using var zlib = new ZLibStream(blocks, CompressionMode.Decompress);
        byte[] rows = new byte[height * (1 + width * 4)];
        zlib.ReadExactly(rows);
        if (zlib.ReadByte() != -1) throw new Exception("Unexpected PNG scanline bytes");
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int offset = y * (1 + width * 4);
            if (rows[offset] != 0) throw new Exception("Unexpected PNG filter");
            rows.AsSpan(offset + 1, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
        }
        return (width, height, pixels);
    }
}
