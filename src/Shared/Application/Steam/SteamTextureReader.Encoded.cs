using System.Buffers.Binary;
using System.Text;

namespace LumaWall.Application.Steam;

internal static partial class SteamTextureReader
{
    private static void ValidateEncodedHeader(Stream input, Info info, CancellationToken token)
    {
        // PNG/BMP dimensions occur in their fixed prefix. JPEG may have preceding
        // metadata, which is limited to 1 MiB here rather than scanning entropy data.
        int maximum = info.Extension == ".jpg" ? 1024 * 1024 : 138;
        using var payload = new SteamAssetRangeStream(input, info.Offset, info.Bytes);
        byte[] header;
        if (info.Lz4) header = Lz4Header(payload, info.DecodedBytes, maximum, token);
        else
        {
            header = new byte[Math.Min(info.Bytes, maximum)];
            ReadExactly(payload, header, token);
        }
        ValidateEncodedHeader(header, info);
    }

    private static void ValidateEncodedHeader(ReadOnlySpan<byte> bytes, Info info)
    {
        (int Width, int Height) dimensions = info.Extension switch
        {
            ".png" => PngDimensions(bytes), ".jpg" => JpegDimensions(bytes), ".bmp" => BmpDimensions(bytes),
            _ => throw new NotSupportedException("不支持此纹理内嵌图片格式。")
        };
        Dimensions(dimensions.Width, dimensions.Height);
        if (dimensions.Width != info.Width || dimensions.Height != info.Height)
            throw new InvalidDataException("内嵌图片尺寸与纹理尺寸不符。");
    }

    private static (int, int) PngDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes.StartsWith(new byte[] { 137,80,78,71,13,10,26,10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("纹理内嵌 PNG 文件头无效。");
        return (BinaryPrimitives.ReadInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadInt32BigEndian(bytes[20..]));
    }
    private static (int, int) BmpDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 26 || !bytes.StartsWith("BM"u8)) throw new InvalidDataException("纹理内嵌 BMP 文件头无效。");
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]);
        if (header == 12)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]) != 1) throw new InvalidDataException("BMP 平面数量无效。");
            return (BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]));
        }
        if (header is not (40 or 52 or 56 or 108 or 124) || bytes.Length < 14 + header ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) != 1)
            throw new InvalidDataException("纹理内嵌 BMP 信息头无效。");
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]), height = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);
        if (height == int.MinValue) throw new InvalidDataException("BMP 尺寸无效。");
        // Embedded JPEG/PNG compression would add a second set of untrusted dimensions.
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes[30..]);
        if (compression is not (0 or 1 or 2 or 3 or 6)) throw new NotSupportedException("不支持此 BMP 压缩方式。");
        return (width, Math.Abs(height));
    }
    private static (int, int) JpegDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216)
            throw new InvalidDataException("纹理内嵌 JPEG 文件头无效。");
        int offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 255) throw new InvalidDataException("JPEG 标记无效。");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            byte marker = bytes[offset++];
            if (marker is 0 or 216 or 217 or 218) throw new InvalidDataException("JPEG 图像尺寸缺失。");
            if (marker == 1 || marker is >= 208 and <= 215) continue;
            if (offset > bytes.Length - 2) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2) throw new InvalidDataException("JPEG 标记长度无效。");
            if (length > bytes.Length - offset) break;
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
            {
                if (length < 11 || bytes[offset + 7] is < 1 or > 4 || length != 8 + 3 * bytes[offset + 7])
                    throw new InvalidDataException("JPEG 图像尺寸标记无效。");
                return (BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]));
            }
            offset += length;
        }
        throw new InvalidDataException("JPEG 文件头不完整或超过 1 MiB。");
    }

    private static byte[] Lz4Header(Stream input, int decodedBytes, int maximum, CancellationToken token)
    {
        // Decode only enough prefix bytes to inspect the image header. Complete
        // payload validation is repeated when a user requests preview/extraction.
        var output = new byte[Math.Min(decodedBytes, maximum)];
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        int target = 0;
        while (input.Position < input.Length && target < output.Length)
        {
            token.ThrowIfCancellationRequested();
            byte control = reader.ReadByte();
            int literals = Extended(control >> 4);
            if (literals > input.Length - input.Position || literals > decodedBytes - target)
                throw new InvalidDataException("纹理 LZ4 字面量越界。");
            int take = Math.Min(literals, output.Length - target);
            input.ReadExactly(output.AsSpan(target, take)); target += take;
            if (target == output.Length) return output;
            if (input.Position == input.Length) break;
            int distance = reader.ReadUInt16();
            if (distance == 0 || distance > target) throw new InvalidDataException("纹理 LZ4 偏移无效。");
            int match = Extended(control & 15);
            if (match > decodedBytes - target - 4) throw new InvalidDataException("纹理 LZ4 输出越界。");
            match = Math.Min(match + 4, output.Length - target);
            for (int i = 0; i < match; i++)
            {
                if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
                output[target] = output[target - distance]; target++;
            }
        }
        if (target != output.Length) throw new InvalidDataException("纹理 LZ4 文件头不完整。");
        return output;
        int Extended(int value)
        {
            if (value < 15) return value;
            byte more;
            do
            {
                token.ThrowIfCancellationRequested();
                more = reader.ReadByte();
                if (value > MaxPayload - more) throw new InvalidDataException("纹理 LZ4 长度过大。");
                value += more;
            } while (more == 255);
            return value;
        }
    }
}
