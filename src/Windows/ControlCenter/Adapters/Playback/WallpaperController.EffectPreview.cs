using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed partial class WallpaperController
{
    private readonly TaskbarVisibilityClient taskbars=new();
    public int? TaskbarProcessId=>taskbars.ProcessId;
    private string? soloScreen,soloAsset;
    public EffectKind? SoloKind {get;private set;}
    // Drag previews update the live players only. Persistence is a separate
    // operation at an idle/gesture boundary, preserving screen/asset inheritance.
    internal void PreviewEffectDraft(PreparedEffectWrite draft)
    {
        if(disposed)return;
        bool Affects(string screen,string asset)=>draft.Asset==null?screen==draft.Screen:asset==draft.Asset;
        WallpaperProfile Resolve(string screen,string asset) {
            var baseline=screen==draft.Screen?draft.Baseline:store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
            var specific=draft.Asset==asset?draft.Specific!:store.AssetProfile(asset);
            return baseline with {Effects=specific.Resolve(baseline.Effects)};
        }
        foreach(var pair in pendingEffects)if(Affects(pair.Key,pair.Value.AssetId)) {
            var profile=Resolve(pair.Key,pair.Value.AssetId);pair.Value.Fill=profile.Fill;pair.Value.Effects=profile.Effects;
        }
        foreach(var pair in running)if(Affects(pair.Key,pair.Value.Asset.Id)) {
            var profile=Resolve(pair.Key,pair.Value.Asset.Id);var current=pair.Value;
            current.Binding=current.Binding with {Fill=profile.Fill,Effects=profile.Effects};
            current.Player.ConfigureEffects(profile.Fill,ProjectedEffects(pair.Key,current.Asset.Id,profile.Effects),false);
        }
        if(Preview is { } preview&&Affects(preview.ScreenId,preview.Asset.Id)) {
            var profile=Resolve(preview.ScreenId,preview.Asset.Id);preview.Player.ConfigureEffects(profile.Fill,ProjectedEffects(preview.ScreenId,preview.Asset.Id,profile.Effects),false);
        }
        SyncTaskbars();
    }
    private VideoEffects ProjectedEffects(string screen,string asset,VideoEffects effects)
    {
        if(!RuntimeSettingsReader.EffectsEnabled(store,screen))return VideoEffects.Original;
        if(SoloKind is not { } kind||screen!=soloScreen||asset!=soloAsset)return effects;
        return EffectProjection.Isolate(effects,kind);
    }
    public void RefreshScreenEffectsState(string screen)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(pendingEffects.TryGetValue(screen,out var preparing))preparing.ProjectionRevision++;
        bool failed=false;
        if(running.TryGetValue(screen,out var current))
            failed=!current.Player.ConfigureEffects(current.Binding.Fill,ProjectedEffects(screen,current.Asset.Id,current.Binding.Effects),false);
        if(Preview is { } preview&&preview.ScreenId==screen) {
            var profile=store.EffectiveProfile(screen,preview.Asset.Id);
            failed|=!preview.Player.ConfigureEffects(profile.Fill,ProjectedEffects(screen,preview.Asset.Id,profile.Effects),false);
        }
        SyncTaskbars();Changed?.Invoke();
        if(failed)throw new IOException(UiLanguage.Text("总开关已保存，部分壁纸未确认更新，请重新应用壁纸。"));
    }
    public void SetEffectSolo(string screen,string asset,EffectKind? kind)
    {
        ClearEffectSolo();if(kind==null||disposed)return;
        soloScreen=screen;soloAsset=asset;SoloKind=kind;
        if(running.TryGetValue(screen,out var current)&&current.Asset.Id==asset)
            current.Player.ConfigureEffects(current.Binding.Fill,ProjectedEffects(screen,asset,current.Binding.Effects),false);
        SyncTaskbars();
    }
    public void ClearEffectSolo()
    {
        var screen=soloScreen;var asset=soloAsset;soloScreen=soloAsset=null;SoloKind=null;
        if(screen!=null&&running.TryGetValue(screen,out var current)&&current.Asset.Id==asset&&!disposed)
            current.Player.ConfigureEffects(current.Binding.Fill,ProjectedEffects(screen,current.Asset.Id,current.Binding.Effects),false);
        SyncTaskbars();
    }
    private void SyncTaskbars()
    {
        if(disposed)return;
        var targets=running.Where(p=>!p.Value.Player.HasExited)
            .Select(p=>(Screen:p.Key,Effects:ProjectedEffects(p.Key,p.Value.Asset.Id,p.Value.Binding.Effects)))
            .Where(p=>p.Effects.TaskbarAppearance.HasValue)
            .Select(p=>(p.Screen,Style:p.Effects.TaskbarAppearance!.Value,Visual:p.Effects.GetCollection().TaskbarVisual));
        taskbars.Update(targets);
    }
}
