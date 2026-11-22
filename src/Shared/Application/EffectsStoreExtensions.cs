using LumaWall.Contracts;

namespace LumaWall.Application;

// Source-compatible callers still enter Application. Storage never creates or
// depends on a use-case service; new callers should inject EffectsService.
public static class EffectsStoreExtensions
{
    public static void SaveTargetEffects(this IEffectsStore store,string screen,string? asset,WallpaperProfile draft,VideoEffects before)=>new EffectsService(store).SaveTargetEffects(screen,asset,draft,before);
    public static void ClearTargetEffects(this IEffectsStore store,string screen,string? asset)=>new EffectsService(store).ClearTargetEffects(screen,asset);
    public static void CopyScreenEffectsToAsset(this IEffectsStore store,string screen,string asset)=>new EffectsService(store).CopyScreenEffectsToAsset(screen,asset);
    public static void SetEffectScope(this IEffectsStore store,string screen,string asset,EffectKind kind,bool enabled)=>new EffectsService(store).SetEffectScope(screen,asset,kind,enabled);
}
