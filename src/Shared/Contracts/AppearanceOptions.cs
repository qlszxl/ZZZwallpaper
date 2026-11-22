using System.Text.Json;

namespace LumaWall.Contracts;

public sealed record SkinPalette(string Id,string Name,string Accent,string AccentEnd,string Background,string Stripe,string Surface)
{
    public static readonly SkinPalette[] All=[
        new("lime","荧光黄绿","#F4E600","#86EB16","#0C0E0F","#1B1D1E","#191919")
    ];
    public static SkinPalette Find(string? id)=>All.FirstOrDefault(s=>s.Id==id)??All[0];
}

public sealed record AppearanceOptions
{
    public string Skin {get;init;}="lime";
    public bool Pattern {get;init;}=true;
    public bool Motion {get;init;}=true;
    public AppearanceOptions Normalize()=>this with {Skin=SkinPalette.Find(Skin).Id};
    public static AppearanceOptions FromJson(string? json)
    {
        try {return (string.IsNullOrWhiteSpace(json)?new():JsonSerializer.Deserialize<AppearanceOptions>(json)??new()).Normalize();}
        catch(JsonException) {return new();}
    }
    public string ToJson()=>JsonSerializer.Serialize(Normalize());
}
