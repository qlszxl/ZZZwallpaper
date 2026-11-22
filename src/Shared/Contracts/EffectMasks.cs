using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record EffectMask
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Name {get;init;}="遮罩 1";
    // Rectangle, ellipse, rounded rectangle, diamond, star, image, legacy animated shapes.
    public int Shape {get;init;}
    public double X {get;init;}=.5;
    public double Y {get;init;}=.5;
    public double Width {get;init;}=.6;
    public double Height {get;init;}=.6;
    public double Rotation {get;init;}
    public double Feather {get;init;}=.05;
    public double Corner {get;init;}=.2;
    public bool Invert {get;init;}
    public double Speed {get;init;}
    public double Coverage {get;init;}=.45;
    public double Scale {get;init;}=2;
    // A compressed 512 x 512 luminance plane, independent of external file paths.
    public string Image {get;init;}="";
    public EffectMask Validate()
    {
        if(string.IsNullOrWhiteSpace(Id)||Id.Length>64||!Id.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-'))throw new ArgumentException("遮罩编号无效");
        if(string.IsNullOrWhiteSpace(Name)||Name.Length>40)throw new ArgumentException("遮罩名称须为 1–40 个字符");
        EffectRange.Check(Shape,0,8);EffectRange.Check(X,0,1);EffectRange.Check(Y,0,1);
        EffectRange.Check(Width,.01,2);EffectRange.Check(Height,.01,2);EffectRange.Check(Rotation,-180,180);
        EffectRange.Check(Feather,0,1);EffectRange.Check(Corner,0,1);EffectRange.Check(Speed,-2,2);EffectRange.Check(Coverage,0,1);EffectRange.Check(Scale,.5,8);
        if(Image is null||Image.Length>351000)throw new ArgumentException("遮罩图片无效");
        if(Image.Length>0)MaskPixels.Validate(Image);
        return this;
    }
}

public static class MaskPixels
{
    public const int Size=512,Bytes=Size*Size;
    // Keep validation/render commands cheap when previewing the same immutable plane.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> Cache=new(ReferenceEqualityComparer.Instance);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> Valid=new(ReferenceEqualityComparer.Instance);
    public static void Validate(string value) {if(Valid.ContainsKey(value))return;Decode(value);if(Valid.Count>=32)Valid.Clear();Valid[value]=0;}
    public static byte[] Decode(string value)
    {
        try {
            if(value.StartsWith("B1:",StringComparison.Ordinal)) {
                using var input=new MemoryStream(Convert.FromBase64String(value[3..]));using var stream=new BrotliStream(input,CompressionMode.Decompress);
                var pixels=new byte[Bytes];stream.ReadExactly(pixels);if(stream.ReadByte()!=-1)throw new ArgumentException("遮罩图片尺寸无效");return pixels;
            }
            var bytes=Convert.FromBase64String(value);if(bytes.Length!=Bytes)throw new ArgumentException("遮罩图片尺寸无效");return bytes;
        }catch(Exception ex) when(ex is FormatException or InvalidDataException or EndOfStreamException) {throw new ArgumentException("遮罩图片数据损坏",ex);}
    }
    public static string Encode(byte[] pixels)
    {
        if(pixels.Length!=Bytes)throw new ArgumentException("遮罩图片尺寸无效");
        using var output=new MemoryStream();using(var compressed=new BrotliStream(output,CompressionLevel.Optimal,true))compressed.Write(pixels);
        return "B1:"+Convert.ToBase64String(output.ToArray());
    }
    public static string Prepare(string value)
    {
        if(value.Length==0)return "-";
        if(Cache.TryGetValue(value,out var cached)&&File.Exists(Path.Combine(Path.GetTempPath(),"ZZZwallpaper-masks",cached+".mask"))) {OwnedFileRetention.Touch(Path.Combine(Path.GetTempPath(),"ZZZwallpaper-masks",cached+".mask"));return cached;}
        var bytes=Decode(value);string key=Convert.ToHexString(SHA256.HashData(bytes));
        string folder=Path.Combine(Path.GetTempPath(),"ZZZwallpaper-masks");Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,key+".mask");
        if(!File.Exists(path)) {
            string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try {File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        OwnedFileRetention.Pixels(folder,".mask",path);
        if(Cache.Count>=32)Cache.Clear();Cache[value]=key;return key;
    }
}

public sealed partial record VideoEffects
{
    public EffectMask[] Masks {get;init;}=[];
    private Dictionary<EffectKind,string> effectMasks=new();
    public Dictionary<EffectKind,string> EffectMasks {
        get=>effectMasks;
        init=>effectMasks=value==null?null!:value.Where(pair=>(pair.Key&EffectCollection.Retired)==0).ToDictionary(pair=>pair.Key,pair=>pair.Value);
    }
    [JsonIgnore] public string? MaskPreviewId {get;init;}
    private void ValidateMasks()
    {
        if(Masks is null||Masks.Length>16||EffectMasks is null)throw new ArgumentException("最多可添加 16 个遮罩");
        var ids=new HashSet<string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var mask in Masks) {
            if(mask is null)throw new ArgumentException("遮罩配置无效");mask.Validate();
            if(!ids.Add(mask.Id)||!names.Add(mask.Name.Trim()))throw new ArgumentException("遮罩编号和名称不能重复");
        }
        foreach(var pair in EffectMasks)if(!EffectOrder.Supports(pair.Key)||!ids.Contains(pair.Value))throw new ArgumentException("效果引用的遮罩无效");
    }
    private VideoEffects MigrateBlurMask()
    {
        if(!BlurMotion)return this;
        var id="legacy-blur";while(Masks.Any(m=>m.Id==id))id+="-1";
        var name="旧版模糊区域";while(Masks.Any(m=>m.Name==name))name+=" 1";
        var mask=new EffectMask {Id=id,Name=name,Shape=6+BlurMotionMode,Speed=BlurMotionSpeed,Coverage=BlurCoverage,Scale=BlurScale,Feather=BlurFeather,Rotation=BlurAngle};
        var links=new Dictionary<EffectKind,string>(EffectMasks) {[EffectKind.Blur]=id};
        return this with {BlurMotion=false,Masks=[..Masks,mask],EffectMasks=links,Collection=GetCollection() with {Added=GetCollection().Added|EffectKind.MaskLibrary}};
    }
    private string MasksWire()
    {
        var text=new System.Text.StringBuilder();text.Append(' ').Append(Masks.Length);
        foreach(var m in Masks)text.Append(FormattableString.Invariant($" {m.Shape} {m.X:F6} {m.Y:F6} {m.Width:F6} {m.Height:F6} {m.Rotation:F6} {m.Feather:F6} {m.Corner:F6} {(m.Invert?1:0)} {m.Speed:F6} {m.Coverage:F6} {m.Scale:F6} {(m.Shape==5?MaskPixels.Prepare(m.Image):"-")}"));
        for(int i=0;i<25;i++) {var kind=(EffectKind)(1<<i);text.Append(' ').Append(EffectMasks.TryGetValue(kind,out var id)?Array.FindIndex(Masks,m=>m.Id==id):-1);}
        text.Append(' ').Append(Array.FindIndex(Masks,m=>m.Id==MaskPreviewId));return text.ToString();
    }
}
