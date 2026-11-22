using System.IO.Compression;
using System.Security.Cryptography;

namespace LumaWall.Contracts;

public static class ParticlePixels
{
    public const int Size=512,Bytes=Size*Size*4;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> Cache=new(ReferenceEqualityComparer.Instance);
    // Keep every image in a live configuration validated without pinning old
    // encoded images. A 16-entry clear-all cache thrashes for 17 scan-card slices.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string,object> Valid=new();
    public static void Validate(string value) {if(Valid.TryGetValue(value,out _))return;Decode(value);Valid.GetValue(value,_=>new());}
    public static byte[] Decode(string value) {
        try {
            if(value.Length>1_400_000||!value.StartsWith("P1:",StringComparison.Ordinal))throw new ArgumentException("粒子图片格式无效");
            using var input=new MemoryStream(Convert.FromBase64String(value[3..]));using var stream=new BrotliStream(input,CompressionMode.Decompress);
            var pixels=new byte[Bytes];stream.ReadExactly(pixels);if(stream.ReadByte()!=-1)throw new ArgumentException("粒子图片尺寸无效");return pixels;
        } catch(Exception ex) when(ex is FormatException or InvalidDataException or EndOfStreamException) {throw new ArgumentException("粒子图片数据损坏",ex);}
    }
    public static string Encode(byte[] pixels) {
        if(pixels.Length!=Bytes)throw new ArgumentException("粒子图片尺寸无效");
        using var output=new MemoryStream();using(var compressed=new BrotliStream(output,CompressionLevel.Optimal,true))compressed.Write(pixels);
        return "P1:"+Convert.ToBase64String(output.ToArray());
    }
    public static string Prepare(string value) {
        if(value.Length==0)return "-";
        string folder=Path.Combine(Path.GetTempPath(),"ZZZwallpaper-particles");
        if(Cache.TryGetValue(value,out var cached)&&File.Exists(Path.Combine(folder,cached+".rgba"))) {OwnedFileRetention.Touch(Path.Combine(folder,cached+".rgba"));return cached;}
        var bytes=Decode(value);string key=Convert.ToHexString(SHA256.HashData(bytes));Directory.CreateDirectory(folder);string path=Path.Combine(folder,key+".rgba");
        if(!File.Exists(path)) {
            string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try {File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        OwnedFileRetention.Pixels(folder,".rgba",path);
        if(Cache.Count>=16)Cache.Clear();Cache[value]=key;return key;
    }
}
