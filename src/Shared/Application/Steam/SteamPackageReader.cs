using System.Text;

namespace LumaWall.Application.Steam;

// PKGV directory layout: https://github.com/redpfire/we/blob/master/main.cpp
// Referenced by Wallpaper Engine's official "Editing Downloaded Wallpapers" FAQ.
// This reader only indexes bounded media ranges; it never materializes an archive tree.
internal static class SteamPackageReader
{
    internal sealed record Entry(string Name, long Offset, long Bytes);
    private const int MaxEntries = 20_000, MaxHeaderBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static Entry[] Read(Stream input, CancellationToken token)
    {
        if (!input.CanSeek || input.Length > 64L * 1024 * 1024 * 1024)
            throw new InvalidDataException("素材包过大或不可读取。");
        using var reader = new BinaryReader(input, Utf8, true);
        string magic = ReadString(reader, 16);
        if (magic.Length != 8 || !magic.StartsWith("PKGV", StringComparison.Ordinal) ||
            !magic.AsSpan(4).ToArray().All(char.IsAsciiDigit) || magic == "PKGV0000")
            throw new InvalidDataException("不支持此素材包格式。");
        uint count = reader.ReadUInt32();
        if (count > MaxEntries) throw new InvalidDataException("素材包条目超过 20000 个。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>((int)count);
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            string name = ReadString(reader, 1024).Replace('\\', '/');
            ValidateName(name);
            if (!names.Add(name)) throw new InvalidDataException("素材包包含重复路径。");
            entries.Add(new(name, reader.ReadUInt32(), reader.ReadUInt32()));
            if (input.Position > MaxHeaderBytes) throw new InvalidDataException("素材包目录过大。");
        }
        long start = input.Position;
        var ordered = entries.OrderBy(e => e.Offset).ThenBy(e => e.Bytes).ToArray();
        long end = 0;
        foreach (var entry in ordered)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Offset > input.Length - start || entry.Bytes > input.Length - start - entry.Offset)
                throw new InvalidDataException("素材包条目超出文件范围。");
            if (entry.Bytes > 0 && entry.Offset < end) throw new InvalidDataException("素材包条目范围重叠。");
            end = Math.Max(end, entry.Offset + entry.Bytes);
        }
        return entries.Select(e => e with { Offset = start + e.Offset }).ToArray();
    }

    private static string ReadString(BinaryReader reader, int maximum)
    {
        uint size = reader.ReadUInt32();
        if (size is 0 || size > maximum || size > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("素材包路径长度无效。");
        byte[] bytes = reader.ReadBytes((int)size);
        if (bytes.Length != size) throw new EndOfStreamException("素材包目录不完整。");
        return Utf8.GetString(bytes);
    }

    private static void ValidateName(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(':') || name.Any(char.IsControl) ||
            name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("素材包包含无效或越界路径。");
    }
}

// A range stream prevents malformed TEX headers from reading a neighboring PKG entry.
internal sealed class SteamAssetRangeStream : Stream
{
    private readonly Stream source;
    private readonly long start, size;
    private long position;
    internal SteamAssetRangeStream(Stream source, long start, long size)
    {
        if (!source.CanSeek || start < 0 || size < 0 || start > source.Length || size > source.Length - start)
            throw new InvalidDataException("素材范围无效。");
        this.source = source; this.start = start; this.size = size;
    }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => size;
    public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        source.Position = start + position;
        int read = source.Read(buffer[..(int)Math.Min(buffer.Length, size - position)]);
        position += read; return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        source.Position = start + position;
        int read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, size - position)], token);
        position += read; return read;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        long next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(position + offset),
            SeekOrigin.End => checked(size + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        if (next < 0 || next > size) throw new InvalidDataException("素材读取超出条目范围。");
        return position = next;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
