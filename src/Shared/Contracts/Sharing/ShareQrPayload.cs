using System.Globalization;
using System.Security.Cryptography;

namespace LumaWall.Contracts.Sharing;

// A QR transport over the existing packet. Each image contains one complete set.
public static class ShareQrPayload
{
    public const string Prefix="ZZZwallpaper-QR1|";
    public const int ChunkBytes=1024,MaxParts=16,MaxPacketBytes=ChunkBytes*MaxParts;

    public static string[] Split(byte[] packet)
    {
        if(packet.Length==0||packet.Length>MaxPacketBytes)
            throw new ArgumentException("配置超过二维码图片容量（16 KiB），请减少勾选的组件或图片，或使用下方的文本文件分享。");
        string id=Convert.ToHexString(SHA256.HashData(packet).AsSpan(0,16));
        int count=(packet.Length+ChunkBytes-1)/ChunkBytes;
        return Enumerable.Range(0,count).Select(i=>Prefix+id+"|"+(i+1).ToString(CultureInfo.InvariantCulture)+"|"+count.ToString(CultureInfo.InvariantCulture)+"|"+
            Convert.ToBase64String(packet,i*ChunkBytes,Math.Min(ChunkBytes,packet.Length-i*ChunkBytes))).ToArray();
    }

    public static SharedConfiguration Read(IEnumerable<string> values)
    {
        var texts=values.Where(s=>s.StartsWith(ShareCode.Prefix,StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Take(MaxParts+1).ToArray();
        if(texts.Length==0)throw new ArgumentException("图片中没有找到 ZZZwallpaper 配置二维码，请粘贴完整的分享原图。");
        if(texts.Length>MaxParts)throw new ArgumentException("图片中有过多二维码，请一次只导入一份分享配置。");
        // Also accept an older decimal share code placed in a QR by another tool.
        if(texts.Length==1&&!texts[0].StartsWith("ZZZwallpaper-QR",StringComparison.Ordinal))return ShareCode.Decode(texts[0]);
        string? expectedId=null;int total=0;var parts=new SortedDictionary<int,byte[]>();
        foreach(string text in texts) {
            if(text.Length>1500||!text.StartsWith(Prefix,StringComparison.Ordinal))
                throw new ArgumentException("二维码格式不兼容或混有其他配置，请使用完整原图；新格式可能需要升级软件。");
            var fields=text[Prefix.Length..].Split('|');
            if(fields.Length!=4||fields[0].Length!=32||fields[0].Any(c=>!char.IsAsciiHexDigit(c))||
                !int.TryParse(fields[1],NumberStyles.None,CultureInfo.InvariantCulture,out int index)||
                !int.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out int count)||count is <1 or >MaxParts||index<1||index>count)
                throw new ArgumentException("二维码分片信息无效。");
            if(expectedId!=null&&(expectedId!=fields[0]||total!=count))throw new ArgumentException("图片混有不同的分享配置，请一次只导入一份。");
            expectedId=fields[0];total=count;
            byte[] data;
            try {data=Convert.FromBase64String(fields[3]);}catch(FormatException) {throw new ArgumentException("二维码数据损坏，请使用分享原图。");}
            if(data.Length is <1 or >ChunkBytes||(index<count&&data.Length!=ChunkBytes)||!parts.TryAdd(index,data))
                throw new ArgumentException("二维码分片大小无效或存在冲突。");
        }
        if(parts.Count!=total)throw new ArgumentException($"只识别到 {parts.Count} / {total} 个二维码。请使用完整原图，避免裁剪或压缩后重新粘贴。");
        byte[] packet=parts.Values.SelectMany(p=>p).ToArray();
        if(!string.Equals(expectedId,Convert.ToHexString(SHA256.HashData(packet).AsSpan(0,16)),StringComparison.Ordinal))
            throw new ArgumentException("二维码整份配置校验失败，请重新获取原图。");
        return ShareCode.DecodePacket(packet);
    }
}
