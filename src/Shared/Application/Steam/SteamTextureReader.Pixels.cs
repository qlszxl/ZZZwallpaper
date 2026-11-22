using System.Buffers.Binary;
using System.IO.Compression;

namespace LumaWall.Application.Steam;

internal static partial class SteamTextureReader
{
    private static byte[] DecodePixels(byte[] data, int format, int width, int height, CancellationToken token)
    {
        if (format == 0) return data;
        var rgba = new byte[checked(width * height * 4)];
        if (format == 9)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = data[i];
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }
        int offset = 0, blockBytes = format == 7 ? 8 : 16;
        Span<byte> colors = stackalloc byte[16];
        Span<byte> alpha = stackalloc byte[8];
        for (int by = 0; by < height; by += 4)
        {
            token.ThrowIfCancellationRequested();
            for (int bx = 0; bx < width; bx += 4)
            {
                var block = data.AsSpan(offset, blockBytes); offset += blockBytes;
                int colorOffset = format == 7 ? 0 : 8;
                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block[colorOffset..]);
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[(colorOffset + 2)..]);
                Color565(c0, colors); Color565(c1, colors[4..]);
                colors[3] = colors[7] = 255;
                bool opaque = format != 7 || c0 > c1;
                for (int c = 0; c < 3; c++)
                {
                    colors[8 + c] = opaque ? (byte)((2 * colors[c] + colors[4 + c]) / 3) : (byte)((colors[c] + colors[4 + c]) / 2);
                    colors[12 + c] = opaque ? (byte)((colors[c] + 2 * colors[4 + c]) / 3) : (byte)0;
                }
                colors[11] = 255; colors[15] = opaque ? (byte)255 : (byte)0;
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[(colorOffset + 4)..]);
                ulong alphaBits = 0;
                if (format == 4)
                {
                    alpha[0] = block[0]; alpha[1] = block[1];
                    if (alpha[0] > alpha[1])
                        for (int i = 2; i < 8; i++) alpha[i] = (byte)(((8 - i) * alpha[0] + (i - 1) * alpha[1]) / 7);
                    else
                    {
                        for (int i = 2; i < 6; i++) alpha[i] = (byte)(((6 - i) * alpha[0] + (i - 1) * alpha[1]) / 5);
                        alpha[6] = 0; alpha[7] = 255;
                    }
                    for (int i = 0; i < 6; i++) alphaBits |= (ulong)block[2 + i] << (8 * i);
                }
                for (int i = 0; i < 16; i++)
                {
                    int x = bx + i % 4, y = by + i / 4;
                    if (x >= width || y >= height) continue;
                    int output = (y * width + x) * 4, color = (int)((indices >> (i * 2)) & 3) * 4;
                    colors.Slice(color, 4).CopyTo(rgba.AsSpan(output, 4));
                    if (format == 6) rgba[output + 3] = (byte)(((block[i / 2] >> ((i % 2) * 4)) & 15) * 17);
                    if (format == 4) rgba[output + 3] = alpha[(int)((alphaBits >> (i * 3)) & 7)];
                }
            }
        }
        return rgba;
    }
    private static void Color565(ushort color, Span<byte> rgba)
    {
        int r = color >> 11, g = color >> 5 & 63, b = color & 31;
        rgba[0] = (byte)((r << 3) | (r >> 2)); rgba[1] = (byte)((g << 2) | (g >> 4)); rgba[2] = (byte)((b << 3) | (b >> 2));
    }

    private static byte[] EncodePng(byte[] rgba, int stride, int sourceWidth, int sourceHeight, int width, int height, CancellationToken token)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            byte[] row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                int sy = (int)((long)y * sourceHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int sx = (int)((long)x * sourceWidth / width);
                    rgba.AsSpan((sy * stride + sx) * 4, 4).CopyTo(row.AsSpan(1 + x * 4));
                }
                zlib.Write(row);
            }
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR"u8, header);
        token.ThrowIfCancellationRequested();
        // Small chunks bound CRC cancellation latency and avoid a second compressed-data copy.
        var compressedBytes = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        for (int position = 0; position < compressedBytes.Length; position += 64 * 1024)
        {
            token.ThrowIfCancellationRequested();
            Chunk(png, "IDAT"u8, compressedBytes.Slice(position, Math.Min(64 * 1024, compressedBytes.Length - position)));
        }
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }
    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number); output.Write(type); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte item in type) crc = CrcTable[(crc ^ item) & 255] ^ crc >> 8;
        foreach (byte item in data) crc = CrcTable[(crc ^ item) & 255] ^ crc >> 8;
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ value >> 1 : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
