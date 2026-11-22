using System.IO.Compression;
using System.Security.Cryptography;

namespace LumaWall.Contracts;

public sealed record ParticleAppearance(int Id,string Name,string? File=null);

public static class ParticleArtwork
{
    public const int Size=200,FirstStyle=8,Count=7,LastStyle=FirstStyle+Count-1;
    private static readonly ParticleAppearance[] Standard=[
        new(0,"圆点"),new(1,"花瓣"),new(2,"光点"),new(3,"柔光"),new(4,"菱形"),new(5,"雨滴涟漪"),new(6,"星形"),new(7,"自定义图片")];
    public static IReadOnlyList<ParticleAppearance> All => [..Standard,..FeaturePacks.ParticleStyles];
    public static string? ResourceDirectory=>FeaturePacks.ResourceDirectory(new("effect","Particles","particles.artwork"));
    public static string ParticlePath(string file)=>Path.Combine(ResourceDirectory??throw new FileNotFoundException("尚未安装粒子图片包"),file);
    private static readonly object Gate=new();
    private static string? preparedDirectory,preparedKey;
    private static string CachePath(string key)=>Path.Combine(Path.GetTempPath(),"ZZZwallpaper-particles",key+".rgba");
    internal static void Release(string directory){
        lock(Gate){
            if(directory==preparedDirectory){preparedDirectory=null;preparedKey=null;}
            var receipt=Path.Combine(directory,"particle-cache.key");
            if(!File.Exists(receipt))return;
            var key=File.ReadAllText(receipt);
            if(key.Length==64&&key.All(char.IsAsciiHexDigit))File.Delete(CachePath(key));
        }
    }
    public static string Prepare(){
        var directory=ResourceDirectory??throw new FileNotFoundException("尚未安装粒子图片包");
        lock(Gate){
            if(directory==preparedDirectory&&preparedKey!=null&&File.Exists(CachePath(preparedKey)))return preparedKey;
            using var stream=File.OpenRead(Path.Combine(directory,"builtins.rgba.br"));
            using var compressed=new BrotliStream(stream,CompressionMode.Decompress);
            var pixels=new byte[Size*Size*4*Count];compressed.ReadExactly(pixels);
            if(compressed.ReadByte()!=-1)throw new InvalidDataException("粒子图片尺寸无效");
            var key=Convert.ToHexString(SHA256.HashData(pixels));var path=CachePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(!File.Exists(path)||new FileInfo(path).Length!=pixels.Length){
                var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(temp,pixels);File.Move(temp,path,true);}finally{File.Delete(temp);}
            }
            File.WriteAllText(Path.Combine(directory,"particle-cache.key"),key);
            OwnedFileRetention.Touch(path);preparedDirectory=directory;return preparedKey=key;
        }
    }
}
