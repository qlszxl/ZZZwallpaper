namespace LumaWall.Contracts;

// Shared by the desktop and image preview. Projection never changes stored state
// or enables an effect that the user has removed from the collection.
public static class EffectProjection
{
    public static VideoEffects Isolate(VideoEffects effects,EffectKind kind)
    {
        var keep=kind;
        if(kind==EffectKind.AudioReactive) {
            foreach(var rule in effects.AudioRules)
                if(rule.Enabled)keep|=AudioParameters.Find(rule.Parameter).Effect;
        }
        return effects with {PackageEffects=[],Collection=effects.GetCollection() with {Added=effects.GetCollection().Added&keep}};
    }
}

public sealed record EffectChange(string[] Screens,string[] Assets)
{
    public static EffectChange Screen(string screen)=>new([screen],[]);
    public static EffectChange Asset(string asset)=>new([],[asset]);
    public bool Affects(string screen,string asset)=>Screens.Contains(screen)||Assets.Contains(asset);
}
