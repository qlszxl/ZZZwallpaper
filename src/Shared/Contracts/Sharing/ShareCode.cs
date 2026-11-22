using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaWall.Contracts.Sharing;

public sealed record ShareSection(string Id, int Version, JsonObject Data);
public sealed record ShareDocument(int Version, ShareSection[] Sections);
public sealed record SharedConfiguration(VideoEffects Effects, WidgetConfiguration Widgets, bool HasEffects, string[] Warnings)
{
    // Device names are local identifiers: all imported components go to the chosen display.
    public WidgetConfiguration OnScreen(string screen)
    {
        var groups=Widgets.LayoutGroups.ToDictionary(g=>g.Id,_=>Guid.NewGuid().ToString("N"));
        var shortcuts=Widgets.Shortcuts.ToDictionary(c=>c.Id,_=>Guid.NewGuid().ToString("N"));
        return (Widgets with {
            Widgets=Widgets.Widgets.Select(w=>w with {Id=Guid.NewGuid().ToString("N"),Screen=screen,
                LayoutGroupId=w.LayoutGroupId is { } group?groups[group]:null,
                ShortcutId=w.ShortcutId is { } card&&shortcuts.TryGetValue(card,out var id)?id:null}).ToArray(),
            LayoutGroups=Widgets.LayoutGroups.Select(g=>g with {Id=groups[g.Id],Screen=screen}).ToArray(),
            Shortcuts=Widgets.Shortcuts.Select(c=>c with {Id=shortcuts[c.Id]}).ToArray()
        }).Validate();
    }
}

// V1 is an offline, versioned snapshot. New transports need a new first byte;
// new independent features get a section ID/version, never positional fields.
public static class ShareCode
{
    public const string Prefix="ZZZwallpaper-";
    public const int MaxJsonBytes=64*1024*1024,MaxCompressedBytes=32*1024*1024;
    public static readonly int MaxCodeLength=Prefix.Length+((MaxCompressedBytes+26+6)/7)*17;
    private const int HeaderSize=10,ChecksumSize=16;
    private static readonly JsonSerializerOptions JsonOptions=new() {MaxDepth=64};

    public static string Encode(ShareDocument document)=>ToText(EncodePacket(document));

    // QR images carry the same checked packet without the decimal text expansion.
    public static byte[] EncodePacket(ShareDocument document)
    {
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(document,JsonOptions);
        if(json.Length>MaxJsonBytes)throw new ArgumentException("配置内容过大，请减少勾选的图片或组件。");
        using var compressed=new MemoryStream();
        using(var writer=new BrotliStream(compressed,CompressionLevel.SmallestSize,true))writer.Write(json);
        if(compressed.Length>MaxCompressedBytes)throw new ArgumentException("分享内容过大，请取消勾选部分贴纸或图片。");
        byte[] packet=new byte[HeaderSize+compressed.Length+ChecksumSize];
        packet[0]=1;packet[1]=1; // transport version, compression: Brotli
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(2),json.Length);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(6),(int)compressed.Length);
        compressed.GetBuffer().AsSpan(0,(int)compressed.Length).CopyTo(packet.AsSpan(HeaderSize));
        SHA256.HashData(packet.AsSpan(0,packet.Length-ChecksumSize)).AsSpan(0,ChecksumSize).CopyTo(packet.AsSpan(packet.Length-ChecksumSize));
        return packet;
    }

    public static string ToText(byte[] packet)
    {
        if(packet.Length<HeaderSize+ChecksumSize||packet.Length>MaxCompressedBytes+HeaderSize+ChecksumSize)
            throw new ArgumentException("分享数据大小无效。");
        var code=new StringBuilder(Prefix,(packet.Length+6)/7*17+Prefix.Length);
        // Seven bytes fit in an unsigned 56-bit integer and exactly 17 decimal digits.
        // Fixed-size blocks avoid a giant integer's quadratic conversion cost.
        for(int offset=0;offset<packet.Length;offset+=7) {
            ulong value=0;for(int i=0;i<7;i++)value=(value<<8)|(offset+i<packet.Length?packet[offset+i]:0u);
            code.Append(value.ToString("D17",CultureInfo.InvariantCulture));
        }
        return code.ToString();
    }

    public static SharedConfiguration Decode(string text)
    {
        if(text.Length>MaxCodeLength*2)throw new ArgumentException("分享码过长。");
        // Accept line wrapping from chat clients, but never silently remove punctuation.
        var normalized=new StringBuilder(Math.Min(text.Length,MaxCodeLength));
        foreach(char c in text) {
            if(char.IsWhiteSpace(c))continue;
            if(normalized.Length==MaxCodeLength)throw new ArgumentException("分享码过长。");
            normalized.Append(c);
        }
        string code=normalized.ToString();
        if(!code.StartsWith(Prefix,StringComparison.Ordinal))throw new ArgumentException("分享码必须以 ZZZwallpaper- 开头。");
        var digits=code.AsSpan(Prefix.Length);
        if(digits.Length<68||digits.Length%17!=0)throw new ArgumentException("分享码长度不完整，请重新复制。");
        byte[] packet=new byte[digits.Length/17*7];
        for(int offset=0;offset<digits.Length;offset+=17) {
            var block=digits.Slice(offset,17);
            if(block.IndexOfAnyExcept("0123456789")>=0||!ulong.TryParse(block,NumberStyles.None,CultureInfo.InvariantCulture,out ulong value)||value>0xFFFFFFFFFFFFFF)
                throw new ArgumentException("分享码数字无效，请检查是否漏字或改动。");
            for(int i=6;i>=0;i--) {packet[offset/17*7+i]=(byte)value;value>>=8;}
        }
        return DecodePacket(packet);
    }

    public static SharedConfiguration DecodePacket(byte[] packet)
    {
        if(packet.Length<HeaderSize+ChecksumSize||packet.Length>MaxCompressedBytes+HeaderSize+ChecksumSize+6)
            throw new ArgumentException("分享数据大小无效。");
        if(packet[0]!=1||packet[1]!=1)throw new ArgumentException("此分享码采用更新的格式，请升级 ZZZwallpaper 后导入。");
        int jsonLength=BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(2)),size=BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(6));
        if(jsonLength is <=0 or >MaxJsonBytes||size is <=0 or >MaxCompressedBytes)throw new ArgumentException("分享码大小无效或超出上限。");
        int length=HeaderSize+size+ChecksumSize;
        if(length>packet.Length||packet.Length-length>6||packet.AsSpan(length).IndexOfAnyExcept((byte)0)>=0)
            throw new ArgumentException("分享码不完整或包含额外内容。");
        byte[] digest=SHA256.HashData(packet.AsSpan(0,length-ChecksumSize));
        if(!CryptographicOperations.FixedTimeEquals(digest.AsSpan(0,ChecksumSize),packet.AsSpan(length-ChecksumSize,ChecksumSize)))
            throw new ArgumentException("分享码校验失败，请重新复制完整内容。");
        byte[] json=new byte[jsonLength];
        using var decoder=new BrotliDecoder();
        if(decoder.Decompress(packet.AsSpan(HeaderSize,size),json,out int consumed,out int written)!=OperationStatus.Done||consumed!=size||written!=jsonLength)
            throw new ArgumentException("分享码压缩内容损坏。");
        ShareDocument document;
        try {
            var reader=new Utf8JsonReader(json,new JsonReaderOptions {MaxDepth=64});int tokens=0;
            while(reader.Read())if(++tokens>200000)throw new ArgumentException("分享配置结构过于复杂。");
            document=JsonSerializer.Deserialize<ShareDocument>(json,JsonOptions)??throw new JsonException();
        }
        catch(JsonException) {throw new ArgumentException("分享码中的配置结构无效。");}
        if(document.Version!=1)throw new ArgumentException("此配置采用更新的版本，请升级软件后导入。");
        if(document.Sections==null||document.Sections.Length>32||document.Sections.Any(s=>s==null||string.IsNullOrWhiteSpace(s.Id)||s.Id.Length>64||s.Data==null)||document.Sections.Select(s=>s.Id).Distinct().Count()!=document.Sections.Length)
            throw new ArgumentException("分享码的配置分区无效或重复。");
        var effects=VideoEffects.Original;var widgets=new WidgetConfiguration();bool hasEffects=false;int known=0;
        var warnings=new List<string>();
        foreach(var section in document.Sections) {
            if(section.Id is not ("effects" or "widgets")) {warnings.Add("包含本版本不支持的扩展分区；需升级后完整还原。");continue;}
            if(section.Version!=1)throw new ArgumentException("效果或组件使用了更新的分享格式，请升级软件后导入。");
            if(section.Data["Version"] is not JsonValue version||!version.TryGetValue<int>(out int modelVersion)||modelVersion<1)
                throw new ArgumentException("效果或组件配置缺少有效版本号。");
            known++;
            try {
                if(section.Id=="effects") {effects=VideoEffects.FromJson(section.Data.ToJsonString());hasEffects=true;}
                else widgets=WidgetConfiguration.FromJson(section.Data.ToJsonString());
            } catch(Exception ex) when(ex is ArgumentException or JsonException or NullReferenceException or InvalidOperationException or FormatException or OverflowException) {
                throw new ArgumentException("配置参数无效或版本不兼容，请确认分享码来源或升级软件。",ex);
            }
        }
        if(document.Sections.Length>0&&known==0)throw new ArgumentException("此分享码只有本版本不支持的配置，请升级软件后导入。");
        return new(effects,widgets,hasEffects,warnings.Distinct().ToArray());
    }
}
