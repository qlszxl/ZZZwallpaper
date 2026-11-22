using LumaWall.Contracts;

namespace LumaWall.Application;

public sealed class EffectsService(IEffectsStore store)
{
    public WallpaperProfile? ScreenProfile(string screen)=>store.ScreenProfile(screen);
    public AssetEffects AssetProfile(string asset)=>store.AssetProfile(asset);
    public WallpaperProfile EffectiveProfile(string screen,string asset)=>store.EffectiveProfile(screen,asset);
    public WallpaperProfile EditingProfile(string screen,string? asset)
    {
        var baseline=store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        return asset==null?baseline:baseline with {Effects=store.AssetProfile(asset).OwnEffects(baseline.Effects)};
    }
    public void SaveScreenProfile(string screen,WallpaperProfile profile)=>store.SaveProfile(screen,"",profile with {Effects=profile.Effects.Validate()});
    public void SaveTargetEffects(string screen,string? asset,WallpaperProfile draft,VideoEffects before)
    {
        var prepared=Prepare(screen,asset,draft,before);
        if(asset==null)store.SaveProfile(screen,"",prepared.Baseline);
        else store.SaveEffectLayers(screen,asset,prepared.Baseline,prepared.Specific!);
    }
    public PreparedEffectWrite Prepare(string screen,string? asset,WallpaperProfile draft,VideoEffects before)
    {
        if(asset==null)return new(screen,null,draft with {Effects=draft.Effects.Validate()},null);
        var baseline=store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        var old=store.AssetProfile(asset);var changed=AssetEffects.ChangedKinds(before,draft.Effects,includeOrder:false);
        var effects=AssetEffects.Copy(draft.Effects,old.Effects,changed&old.Overrides,true);
        effects=AssetEffects.PrivateCopy(draft.Effects,effects,changed&~old.Overrides);
        // Keep the material's editable, unassigned masks as well as referenced masks.
        var referenced=effects.Masks.Where(m=>effects.EffectMasks.ContainsValue(m.Id)).ToList();
        foreach(var mask in draft.Effects.Masks.Where(m=>(!baseline.Effects.Masks.Any(b=>b.Id==m.Id)||
            !draft.Effects.EffectMasks.ContainsValue(m.Id)&&(old.Effects.Masks.Any(own=>own.Id==m.Id)||before.Masks.FirstOrDefault(b=>b.Id==m.Id)!=m))&&!referenced.Any(r=>r.Id==m.Id)))
            if(!before.EffectMasks.ContainsValue(mask.Id)||draft.Effects.EffectMasks.ContainsValue(mask.Id))referenced.Add(mask);
        effects=(effects with {PackageEffects=old.Effects.PackageEffects.Where(p=>!before.PackageEffects.Any(b=>b.Id==p.Id)||draft.Effects.PackageEffects.Any(d=>d.Id==p.Id)).Concat(draft.Effects.PackageEffects.Where(p=>!before.PackageEffects.Contains(p))).GroupBy(p=>p.Id).Select(g=>g.Last()).ToArray(),Masks=referenced.ToArray(),AudioRules=AudioParameters.ApplyDelta(old.Effects.AudioRules,before.AudioRules,draft.Effects.AudioRules),
            Collection=effects.GetCollection() with {Layers=EffectOrder.ApplyDelta(old.Effects.GetCollection(),before.GetCollection(),draft.Effects.GetCollection())}}).Validate();
        return new(screen,asset,baseline,new AssetEffects {Overrides=old.Overrides|changed,Effects=effects}.Validate());
    }
    public void ClearTargetEffects(string screen,string? asset)
    {
        var baseline=store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        if(asset==null)store.SaveProfile(screen,"",baseline with {Effects=VideoEffects.Original});
        else store.SaveEffectLayers(screen,asset,baseline,new() {Overrides=AssetEffects.Allowed,Effects=VideoEffects.Original});
    }
    public void CopyScreenEffectsToAsset(string screen,string asset)
    {
        var baseline=store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        store.SaveEffectLayers(screen,asset,baseline,AssetEffects.FromScreen(baseline.Effects));
    }
    public void SetEffectScope(string screen,string asset,EffectKind kind,bool enabled)
    {
        if((kind&AssetEffects.Allowed)!=kind||!Enum.IsDefined(kind))throw new ArgumentException("此项目始终跟随显示器。");
        var current=store.AssetProfile(asset);var effective=store.EffectiveProfile(screen,asset);
        if(enabled==current.Overrides.HasFlag(kind))return;
        var copied=enabled?AssetEffects.PrivateCopy(effective.Effects,current.Effects,kind):current.Effects;
        // Parameter scope and stack inheritance are independent settings.
        copied=copied with {AudioRules=current.Effects.AudioRules,Collection=copied.GetCollection() with {Layers=EffectOrder.Clone(current.Effects.GetCollection().Layers)}};
        var next=enabled?current with {Overrides=current.Overrides|kind,Effects=copied}:current with {Overrides=current.Overrides&~kind};
        store.SaveEffectLayers(screen,asset,store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original),next);
    }
    public void Commit(string screen,string? asset,WallpaperProfile baseline,AssetEffects specific)
    {
        if(asset==null)store.SaveProfile(screen,"",baseline);
        else store.SaveEffectLayers(screen,asset,baseline,specific);
    }
}
