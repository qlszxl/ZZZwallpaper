using System.Text.Json;

namespace LumaWall.Contracts;
public sealed record WallpaperPosition(int X=0,int Y=0,int Scale=100)
{
    public WallpaperPosition Validate()
    {
        if(X is < -100 or >100||Y is < -100 or >100)throw new ArgumentException("壁纸位置须为 -100–100%");
        if(Scale is <10 or >400)throw new ArgumentException("壁纸缩放须为 10–400%");
        return this;
    }
    public string ToJson()=>JsonSerializer.Serialize(Validate());
    public static WallpaperPosition FromJson(string? json)=>string.IsNullOrEmpty(json)?new():(JsonSerializer.Deserialize<WallpaperPosition>(json)??new()).Validate();
}
