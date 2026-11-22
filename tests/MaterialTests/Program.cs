using LumaWall.Contracts;
using LumaWall.ControlCenter;
using LumaWall.Automation;
using Microsoft.Data.Sqlite;
using System.Text.Json;

if(args.Length!=1)throw new ArgumentException("Pass an isolated new data directory.");
var root=Path.GetFullPath(args[0]);if(Directory.Exists(root))throw new ArgumentException("Use a new test directory.");
int checks=0;
void Check(bool ok,string label) {if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
void Reject(Action action,string label) {try {action();}catch(ArgumentException) {Check(true,label);return;}throw new Exception(label);}
var baseline=new VideoEffects {Collection=new() {Added=EffectKind.Glow|EffectKind.Scanlines|EffectKind.AudioReactive},Glow=true,GlowStrength=.2,Scanlines=true,AudioReactive=true,AudioSource=0,AudioGlow=true};
var specific=new AssetEffects {Overrides=EffectKind.Glow,Effects=baseline with {GlowStrength=.5}};
Check(specific.Resolve(baseline).GlowStrength==.5,"Same effect overrides rather than adds strength");
Check(specific.Resolve(baseline).Scanlines,"Unrelated display effect is inherited");
Check((specific with {Effects=specific.Effects with {Glow=false}}).Resolve(baseline).Glow==false,"Explicit material off suppresses display glow");
Check(!(specific with {Effects=specific.Effects with {Collection=new()}}).Resolve(baseline).GetCollection().Added.HasFlag(EffectKind.Glow),"Removed material effect does not revive its display counterpart");
Check((specific with {Overrides=0}).Resolve(baseline).GlowStrength==.2,"Removing override restores display default");
Reject(()=>new AssetEffects {Overrides=EffectKind.HideTaskbar}.Validate(),"Taskbar remains display scoped");
Check(MediaFormats.IsImage("中文.JPG")&&MediaFormats.IsImage("still.webp")&&!MediaFormats.IsImage("clip.mp4"),"Image/video types and case-insensitive extensions");
Reject(()=>MediaFormats.LocalImportPath("https://example.com/photo.png"),"MCP import rejects URL");
Reject(()=>MediaFormats.LocalImportPath(@"\\server\share\photo.png"),"MCP import rejects network source");
Reject(()=>MediaFormats.LocalImportPath(@"C:\photo.png:script.exe"),"MCP import rejects alternate stream");
Reject(()=>MediaFormats.LocalImportPath(@"C:\script.ps1"),"MCP import rejects executable/script format");
ToolCatalog.Validate("media_import",ToolCatalog.Json(new {path=@"C:\图片 & $\a.png",expectedRevision="test"}));Check(true,"MCP import preserves literal path characters as data");
ToolCatalog.Validate("effect_set_scope",ToolCatalog.Json(new {screenId="left",assetId="a",effect="Glow",scope="asset",expectedRevision="test"}));Check(true,"MCP advertises material scope with revision");
var advanced=EffectPatch.Apply(VideoEffects.Original,"EdgeOutline",true,null,ToolCatalog.Json(new {EdgeOutline=new {Width=2.5,Color="#336699"}}),1920,1080);
Check(advanced.EdgeOutline.Enabled&&advanced.EdgeOutline.Width==2.5&&advanced.EdgeOutline.Color=="#336699","MCP advanced effect preserves structured parameters");
var audio=specific with {Overrides=EffectKind.AudioReactive,Effects=baseline with {AudioSource=1,AudioAmount=.3}};
Check(audio.Resolve(baseline).AudioSource==1&&audio.Resolve(baseline).AudioAmount==.3,"One material audio configuration replaces display audio configuration");
using(var store=new LibraryStore(root)) {
    foreach(var id in new[] {"a","b","image"})store.Upsert(new(id,Path.Combine(root,id+(id=="image"?".png":".mp4")),id,10,0,100,100,"test","SDR","Software"));
    store.SaveProfile("left","a",new(FillMode.Contain,baseline));store.SaveProfile("right","a",new(FillMode.Cover,baseline with {GlowStrength=.1}));
    Check(store.AssetProfile("a").Overrides==0&&store.EffectiveProfile("left","a").Effects.GlowStrength==.2,"Existing display configuration is retained with no automatic material overrides");
    store.SetEffectScope("left","a",EffectKind.Glow,true);
    store.SaveEffectDraft("left","a",new(FillMode.Contain,baseline with {GlowStrength=.7}),EffectKind.Glow);
    Check(store.ScreenProfile("left")!.Effects.GlowStrength==.2,"Editing material effect never overwrites display baseline");
    Check(store.EffectiveProfile("right","a").Effects.GlowStrength==.7,"Material effect follows the same asset across screens");
    Check(store.EffectiveProfile("left","b").Effects.GlowStrength==.2,"Switching to another asset restores display inheritance");
    Check(store.EffectiveProfile("right","a").Fill==FillMode.Cover&&store.EffectiveProfile("left","a").Fill==FillMode.Contain,"Fill remains independently owned by each display");
    var combined=store.EffectiveProfile("left","a").Effects;
    store.SaveBinding(new("left","a",FillMode.Contain,false,1) {Effects=combined},true);
    Check(store.ScreenProfile("left")!.Effects.GlowStrength==.2,"Applying an overridden wallpaper does not bake its effects into screen defaults");
    store.SetEffectScope("left","a",EffectKind.Glow,false);
    Check(store.EffectiveProfile("left","a").Effects.GlowStrength==.2,"UI/MCP restore-inheritance action does not write back private values");
    var pixels=new byte[MaskPixels.Bytes];new Random(13).NextBytes(pixels);
    var mask=new EffectMask {Id="shared-mask",Name="共享遮罩",Shape=5,Image=MaskPixels.Encode(pixels)};
    var masked=baseline with {Masks=[mask],EffectMasks=new() {[EffectKind.Glow]=mask.Id}};
    store.SaveProfile("right","a",new(FillMode.Cover,masked));
    store.SetEffectScope("right","a",EffectKind.Glow,true);
    var privateProfile=store.EffectiveProfile("right","a");
    string privateMask=privateProfile.Effects.EffectMasks[EffectKind.Glow];
    Check(privateMask!=mask.Id,"Enabling material scope isolates mask geometry while sharing its pixel resource");
    for(int step=0;step<20;step++) {
        var draft=store.EffectiveProfile("right","a");
        var moved=draft.Effects.Masks.Select(m=>m.Id==privateMask?m with {X=.2+step*.01}:m).ToArray();
        store.SaveEffectDraft("right","a",draft with {Effects=draft.Effects with {Masks=moved}},EffectKind.Glow);
    }
    Check(store.AssetProfile("a").Effects.Masks.Length==1&&store.AssetProfile("a").Effects.Masks[0].Id==privateMask,"Continuous private-mask edits keep one stable mask instead of accumulating copies");
    Check(store.ScreenProfile("right")!.Effects.Masks.Single().X==.5,"Material mask edits preserve display mask geometry and library");
    foreach(var id in new[] {"a","b","image"})store.SaveEffectLayers("left",id,new(FillMode.Contain,masked),new AssetEffects {Overrides=EffectKind.Glow,Effects=masked});
    Check(store.AssetProfile("image").Effects.Masks[0].Image==mask.Image,"Image wallpaper owns effects and resolves shared image-mask data");
    using var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(root,"library.db")}.ToString());db.Open();using var cmd=db.CreateCommand();
    cmd.CommandText="SELECT COUNT(*) FROM effect_resource";Check((long)cmd.ExecuteScalar()! ==1,"Identical image masks are stored once across display and material profiles");
    cmd.CommandText="SELECT MAX(length(effects)) FROM asset_effects";Check((long)cmd.ExecuteScalar()!<40000,"Per-material profile does not duplicate the embedded mask image");
    store.Remove("b");Check(store.AssetProfile("b").Overrides==0&&store.AssetProfile("a").Overrides==EffectKind.Glow,"Removing a library entry clears only its material overrides");
}
using(var store=new LibraryStore(root)) {
    Check(store.AssetProfile("a").Overrides==EffectKind.Glow&&store.EffectiveProfile("left","a").Effects.Masks.Any(m=>m.Image.Length>0),"Reopening restores overrides and shared mask pixels");
    Check(store.Assets().Single(a=>a.Id=="image").IsImage,"Image identity survives database reload");
}
var legacyDirectory=Path.Combine(root,"legacy");
var legacyMask=new EffectMask {Id="legacy-mask",Name="旧版图片遮罩",Shape=5,Image=MaskPixels.Encode(new byte[MaskPixels.Bytes])};
var legacyEffects=baseline with {Masks=[legacyMask],EffectMasks=new() {[EffectKind.Glow]=legacyMask.Id}};
using(var store=new LibraryStore(legacyDirectory))store.SaveProfile("old-screen","old-asset",new(FillMode.Contain,legacyEffects));
using(var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(legacyDirectory,"library.db")}.ToString())) {
    db.Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE screen_profile SET effects=$effects; DELETE FROM effect_resource; DELETE FROM settings WHERE key='effects.resources.v1'; PRAGMA user_version=4";
    cmd.Parameters.AddWithValue("$effects",legacyEffects.ToJson());cmd.ExecuteNonQuery();
}
using(var store=new LibraryStore(legacyDirectory)) {
    Check(store.ScreenProfile("old-screen")!.Effects.Masks[0].Image==legacyMask.Image,"Version 0.1.3 embedded image mask migrates without pixel loss");
    Check(store.ScreenProfile("old-screen")!.Fill==FillMode.Contain&&store.AssetProfile("old-asset").Overrides==0,"Upgrade preserves display layout and does not invent material overrides");
}
await TargetChecks.Run(root,Check);
await AudioRuleChecks.Run(root,Check);
BoundaryGrowthChecks.Run(root,Check);
await WidgetQueueChecks.Run(Check);
await BoundaryGrowthChecks.AutomationBounds(Path.Combine(root,"automation-bounds"),Check);
Console.WriteLine($"Material checks passed: {checks}");
File.WriteAllText(Path.Combine(root,"animated-effects.txt"),(VideoEffects.Original with {Collection=new() {Added=EffectKind.Scanlines},Scanlines=true,ScanlineStrength=.3,ScanlineMotion=true,ScanlineSpeed=1}).ToWire());
