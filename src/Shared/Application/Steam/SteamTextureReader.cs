using System.Text;

namespace LumaWall.Application.Steam;

// Format reference (author's parser, no dependency or bundled executable):
// https://github.com/notscuffed/repkg/tree/master/RePKG.Application/Texture
internal static partial class SteamTextureReader
{
    private const int MaxPixels = 16_777_216, MaxPayload = 96 * 1024 * 1024;
    internal sealed record Info(int Format, int Width, int Height, int StoredWidth, int StoredHeight,
        long Offset, int Bytes, int DecodedBytes, bool Lz4, string Extension, bool Encoded);

    internal static Info Inspect(Stream input, CancellationToken token)
    {
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        if (CString(reader, 16) != "TEXV0005" || CString(reader, 16) != "TEXI0001")
            throw new NotSupportedException("不支持此纹理版本。");
        int format = reader.ReadInt32(), flags = reader.ReadInt32();
        int textureWidth = reader.ReadInt32(), textureHeight = reader.ReadInt32();
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        reader.ReadUInt32();
        Dimensions(textureWidth, textureHeight); Dimensions(width, height);
        if ((flags & 4) != 0) throw new NotSupportedException("暂不支持动画纹理。");
        if ((flags & 32) != 0) throw new NotSupportedException("暂不支持视频纹理，可选择独立视频文件。");
        string magic = CString(reader, 16);
        int version = magic switch { "TEXB0001" => 1, "TEXB0002" => 2, "TEXB0003" => 3, "TEXB0004" => 4,
            _ => throw new NotSupportedException("不支持此纹理容器。") };
        int imageCount = reader.ReadInt32();
        if (imageCount != 1) throw new NotSupportedException("暂不支持多帧或立方体纹理。");
        int encodedFormat = version >= 3 ? reader.ReadInt32() : -1;
        if (version == 4 && reader.ReadInt32() != 0)
            throw new NotSupportedException("暂不支持视频纹理，可选择独立视频文件。");
        string extension = encodedFormat switch { -1 => ".png", 0 => ".bmp", 2 => ".jpg", 13 => ".png",
            _ => throw new NotSupportedException("不支持此纹理内嵌图片格式。") };
        if (encodedFormat == -1 && format is not (0 or 4 or 6 or 7 or 9))
            throw new NotSupportedException($"暂不支持纹理像素格式 {format}。");
        int mipmaps = reader.ReadInt32();
        if (mipmaps is < 1 or > 32) throw new InvalidDataException("纹理层级数量无效。");
        Info? first = null;
        for (int i = 0; i < mipmaps; i++)
        {
            token.ThrowIfCancellationRequested();
            int w = reader.ReadInt32(), h = reader.ReadInt32();
            Dimensions(w, h);
            int compression = version >= 2 ? reader.ReadInt32() : 0;
            if (compression is not (0 or 1)) throw new InvalidDataException("纹理压缩标记无效。");
            int decoded = version >= 2 ? reader.ReadInt32() : 0;
            int bytes = reader.ReadInt32();
            if (bytes is <= 0 or > MaxPayload || bytes > input.Length - input.Position ||
                decoded < 0 || decoded > MaxPayload || (compression == 1 && decoded == 0))
                throw new InvalidDataException("纹理数据长度无效或过大。");
            int actual = compression == 1 ? decoded : bytes;
            if (encodedFormat == -1 && actual != PixelBytes(format, w, h))
                throw new InvalidDataException("纹理像素长度与尺寸不符。");
            if (i == 0)
            {
                if (width > w || height > h) throw new InvalidDataException("纹理裁剪尺寸超出像素范围。");
                first = new(format, width, height, w, h, input.Position, bytes, actual, compression == 1, extension, encodedFormat != -1);
            }
            input.Seek(bytes, SeekOrigin.Current);
        }
        if (first!.Encoded)
        {
            if (first.Width != first.StoredWidth || first.Height != first.StoredHeight)
                throw new NotSupportedException("暂不支持需要裁剪的内嵌图片纹理。");
            ValidateEncodedHeader(input, first, token);
        }
        return first;
    }

    internal static byte[] ReadImage(Stream input, Info info, bool preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        input.Position = info.Offset;
        byte[] stored = new byte[info.Bytes];
        ReadExactly(input, stored, token);
        byte[] pixels = info.Lz4 ? DecodeLz4(stored, info.DecodedBytes, token) : stored;
        if (info.Encoded)
        {
            ValidateEncodedHeader(pixels, info);
            return pixels;
        }
        byte[] rgba = DecodePixels(pixels, info.Format, info.StoredWidth, info.StoredHeight, token);
        int outputWidth = info.Width, outputHeight = info.Height;
        if (preview && Math.Max(outputWidth, outputHeight) > 320)
        {
            double scale = 320d / Math.Max(outputWidth, outputHeight);
            outputWidth = Math.Max(1, (int)Math.Round(outputWidth * scale));
            outputHeight = Math.Max(1, (int)Math.Round(outputHeight * scale));
        }
        return EncodePng(rgba, info.StoredWidth, info.Width, info.Height, outputWidth, outputHeight, token);
    }

    private static int PixelBytes(int format, int width, int height) => format switch
    {
        0 => checked(width * height * 4), 9 => checked(width * height),
        7 => checked(((width + 3) / 4) * ((height + 3) / 4) * 8),
        4 or 6 => checked(((width + 3) / 4) * ((height + 3) / 4) * 16),
        _ => throw new NotSupportedException("不支持此纹理像素格式。")
    };
    private static void Dimensions(int width, int height)
    {
        if (width is < 1 or > 16384 || height is < 1 or > 16384 || (long)width * height > MaxPixels)
            throw new InvalidDataException("纹理尺寸无效或超过 1677 万像素。");
    }
    private static string CString(BinaryReader reader, int maximum)
    {
        var text = new StringBuilder();
        for (int i = 0; i <= maximum; i++)
        {
            byte value = reader.ReadByte();
            if (value == 0) return text.ToString();
            if (value is < 32 or > 126) throw new InvalidDataException("纹理标记无效。");
            text.Append((char)value);
        }
        throw new InvalidDataException("纹理标记过长。");
    }
    internal static void ReadExactly(Stream input, byte[] data, CancellationToken token)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            token.ThrowIfCancellationRequested();
            int read = input.Read(data, offset, Math.Min(81920, data.Length - offset));
            if (read == 0) throw new EndOfStreamException("素材文件不完整。");
            offset += read;
        }
    }

    private static byte[] DecodeLz4(byte[] input, int length, CancellationToken token)
    {
        var output = new byte[length];
        int source = 0, target = 0;
        while (source < input.Length)
        {
            token.ThrowIfCancellationRequested();
            byte control = input[source++];
            int literals = Extended(control >> 4);
            if (literals > input.Length - source || literals > length - target)
                throw new InvalidDataException("纹理 LZ4 字面量越界。");
            input.AsSpan(source, literals).CopyTo(output.AsSpan(target));
            source += literals; target += literals;
            if (source == input.Length) break;
            if (source + 2 > input.Length) throw new InvalidDataException("纹理 LZ4 偏移不完整。");
            int distance = input[source] | input[source + 1] << 8; source += 2;
            if (distance == 0 || distance > target) throw new InvalidDataException("纹理 LZ4 偏移无效。");
            int match = Extended(control & 15);
            if (match > length - target - 4) throw new InvalidDataException("纹理 LZ4 输出越界。");
            match += 4;
            for (int i = 0; i < match; i++)
            {
                if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
                output[target] = output[target - distance]; target++;
            }
        }
        if (target != length) throw new InvalidDataException("纹理 LZ4 输出长度不符。");
        return output;
        int Extended(int value)
        {
            if (value < 15) return value;
            byte more;
            do
            {
                token.ThrowIfCancellationRequested();
                if (source >= input.Length) throw new InvalidDataException("纹理 LZ4 长度不完整。");
                more = input[source++];
                if (value > MaxPayload - more) throw new InvalidDataException("纹理 LZ4 长度过大。");
                value += more;
            } while (more == 255);
            return value;
        }
    }
}
