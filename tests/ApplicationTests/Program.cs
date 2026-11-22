using System.Text.Json;
using LumaWall.Application;
using LumaWall.Contracts;
using LumaWall.Contracts.Application;

int checks=0;
void Check(bool condition,string label)
{
    if(!condition)throw new Exception(label);
    checks++;Console.WriteLine("PASS: "+label);
}

// These tests intentionally reference only Application. They cannot reach SQLite,
// a UI dispatcher, a native host, or MCP transport through the production assembly.
var configuration=new MemoryConfigurationStore();
var imports=new ConfigurationImportService(configuration);
var original=new WidgetConfiguration();
configuration.Settings["widgets.v1"]=original.ToJson();
var before=new WallpaperProfile(FillMode.Contain,VideoEffects.Original with {Brightness=.1});
configuration.Profile=before;
var changed=new WallpaperProfile(FillMode.Cover,VideoEffects.Original with {Brightness=.2});
bool conflict=false;
try {imports.Apply(original,"screen",changed,"stale");}
catch(ConfigurationConflictException error)
{
    conflict=error.ExpectedRevision=="stale"&&error.CurrentRevision=="current"&&error.ReadContext=="app_get_state";
}
Check(conflict&&configuration.Commits==0,"A stale import is rejected before writing any configuration");
imports.Apply(original,"screen",changed,"current");
Check(configuration.Commits==1&&configuration.Profile==changed,"An import uses the store's atomic configuration commit");
var backup=imports.ReadBackup()!;
Check(backup.Profile!.Fill==before.Fill&&backup.Profile.Effects.ToJson()==before.Effects.ToJson()&&backup.Widgets.ToJson()==original.ToJson(),"Import backup preserves the prior profile and widgets");
imports.Restore("current");
Check(configuration.Commits==2&&configuration.Profile!.Fill==before.Fill&&configuration.Profile.Effects.ToJson()==before.Effects.ToJson()&&!configuration.Settings.ContainsKey("sharing.undo.v1"),"Restore commits the backup once and clears it");
imports.Apply(original,"screen",null,"current");
Check(imports.ReadBackup()!.Profile==null&&configuration.Profile!.Effects.ToJson()==before.Effects.ToJson(),"A widget-only import does not capture or replace screen effects");
configuration.Settings["sharing.undo.v1"]=JsonSerializer.Serialize(new ConfigurationImportService.Backup(99,original,null,null,null));
bool invalidBackup=false;try {imports.ReadBackup();}catch(IOException) {invalidBackup=true;}
Check(invalidBackup,"Unsupported backup versions are rejected before replay");
configuration.Settings["sharing.undo.v1"]=JsonSerializer.Serialize(new ConfigurationImportService.Backup(1,original,"screen",null,before with {Effects=before.Effects with {Version=28}}));
backup=imports.ReadBackup()!;
Check(backup.Profile!.Effects.Version==VideoEffects.Original.Version&&backup.Profile.Effects.ToWire()==before.Effects.ToWire(),"An older import backup migrates with its parameters and default visual sequence intact");
var ordered=before with {Effects=before.Effects with {Collection=before.Effects.GetCollection().WithLayer(EffectKind.Color,-15)}};
configuration.Settings["sharing.undo.v1"]=JsonSerializer.Serialize(new ConfigurationImportService.Backup(1,original,"screen",null,ordered));
Check(imports.ReadBackup()!.Profile!.Effects.ToJson()==ordered.Effects.ToJson(),"A current backup retains explicit visual layers without rebuilding its collection");

var historyStore=new MemoryHistoryStore();
var history=new HistoryService(historyStore);
conflict=false;try {history.Restore(true,"1");}
catch(ConfigurationConflictException error) {conflict=error.CurrentRevision=="2"&&error.ReadContext=="app_get_state.history";}
Check(conflict&&historyStore.Replays==0,"Stale history revisions do not call replay");
var replay=history.Restore(true,"2");
Check(historyStore.Replays==1&&historyStore.LastRedo&&ReferenceEquals(replay,historyStore.Result),"History delegates replay and returns the neutral result unchanged");

var effectStore=new MemoryEffectsStore();
var effects=new EffectsService(effectStore);
var draft=new WallpaperProfile(FillMode.Contain,VideoEffects.Original with {Brightness=.3});
var prepared=effects.Prepare("screen",null,draft,VideoEffects.Original);
Check(effectStore.Writes==0&&prepared.Editing==draft,"Preparing an effect draft is side-effect free");
effects.SaveTargetEffects("screen",null,draft,VideoEffects.Original);
Check(effectStore.Writes==1&&effectStore.Screen==draft,"A screen edit writes its validated profile once");
bool invalidEffects=false;
try {effects.SaveTargetEffects("screen",null,draft with {Effects=draft.Effects with {Brightness=double.NaN}},draft.Effects);}
catch(ArgumentException) {invalidEffects=true;}
Check(invalidEffects&&effectStore.Writes==1,"Invalid effects are rejected before the persistence port is called");
effects.ClearTargetEffects("screen",null);
Check(effectStore.Screen!.Fill==FillMode.Contain&&effectStore.Screen.Effects.IsNeutral,"Clearing effects preserves the screen's fill mode");
CatalogSettingsChecks.Run(Check);
await SteamCatalogChecks.Run(Check);
await SteamAssetChecks.Run(Check);
Console.WriteLine($"Application checks passed: {checks}");

sealed class MemoryConfigurationStore : IConfigurationStore
{
    public Dictionary<string,string> Settings {get;}=new();
    public WallpaperProfile? Profile {get;set;}
    public int Commits {get;private set;}
    public string? GetSetting(string key)=>Settings.GetValueOrDefault(key);
    public string ConfigurationRevision()=>"current";
    public WallpaperProfile? ScreenProfile(string screen)=>Profile;
    public void SaveSharedConfiguration(WidgetConfiguration widgets,string? screen,string? asset,WallpaperProfile? profile,string? undo)
    {
        Commits++;Settings["widgets.v1"]=widgets.ToJson();
        if(profile!=null)Profile=profile;
        if(undo==null)Settings.Remove("sharing.undo.v1");else Settings["sharing.undo.v1"]=undo;
    }
}

sealed class MemoryHistoryStore : IHistoryStore
{
    public long HistoryRevision=>2;
    public int Replays {get;private set;}
    public bool LastRedo {get;private set;}
    public HistoryReplayResult Result {get;}=new("edit",["screen_profile"],["screen"],[],EffectChange.Screen("screen"));
    public HistoryReplayResult ReplayUndo(bool redo) {Replays++;LastRedo=redo;return Result;}
}

sealed class MemoryEffectsStore : IEffectsStore
{
    public WallpaperProfile? Screen {get;private set;}
    public int Writes {get;private set;}
    public WallpaperProfile? ScreenProfile(string screen)=>Screen;
    public AssetEffects AssetProfile(string asset)=>new();
    public WallpaperProfile EffectiveProfile(string screen,string asset)=>Screen??new(FillMode.Cover,VideoEffects.Original);
    public void SaveProfile(string screen,string asset,WallpaperProfile profile) {Writes++;Screen=profile;}
    public void SaveEffectLayers(string screen,string asset,WallpaperProfile baseline,AssetEffects specific) {Writes++;Screen=baseline;}
}

