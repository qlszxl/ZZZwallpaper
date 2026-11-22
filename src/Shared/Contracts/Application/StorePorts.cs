namespace LumaWall.Contracts.Application;

/// <summary>Storage operations required by the effect editing use cases.</summary>
public interface IEffectsStore
{
    WallpaperProfile? ScreenProfile(string screen);
    AssetEffects AssetProfile(string asset);
    WallpaperProfile EffectiveProfile(string screen,string asset);
    void SaveProfile(string screen,string asset,WallpaperProfile profile);
    void SaveEffectLayers(string screen,string asset,WallpaperProfile baseline,AssetEffects specific);
}

/// <summary>Atomic configuration persistence; implementations own transaction and undo capture.</summary>
public interface IConfigurationStore
{
    string? GetSetting(string key);
    string ConfigurationRevision();
    WallpaperProfile? ScreenProfile(string screen);
    void SaveSharedConfiguration(WidgetConfiguration widgets,string? screen,string? asset,WallpaperProfile? profile,string? undo);
}

public interface IHistoryStore
{
    long HistoryRevision {get;}
    HistoryReplayResult ReplayUndo(bool redo);
}

/// <summary>A validated edit prepared by Application and committed by Storage or previewed by a host.</summary>
public sealed record PreparedEffectWrite(string Screen,string? Asset,WallpaperProfile Baseline,AssetEffects? Specific)
{
    public WallpaperProfile Editing=>Specific==null?Baseline:Baseline with {Effects=Specific.OwnEffects(Baseline.Effects)};
}

public sealed record HistoryReplayResult(string Label,string[] Tables,string[] Screens,string[] Settings,EffectChange Effects);
