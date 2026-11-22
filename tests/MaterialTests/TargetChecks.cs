using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.ControlCenter;
using Microsoft.Data.Sqlite;
using ZZZwallpaper.InstallerPreview;

internal static class TargetChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        using(var store=new LibraryStore(Path.Combine(root,"targets"))) {
            var display=new VideoEffects {Collection=new() {Added=EffectKind.Glow|EffectKind.Noise|EffectKind.Scanlines},Glow=true,GlowStrength=.2,Noise=true,NoiseStrength=.1,Scanlines=true};
            store.SaveTargetEffects("left",null,new(FillMode.Contain,display),VideoEffects.Original);
            check(store.Assets().Count==0&&store.ScreenProfile("left")!.Effects.Glow,"Display defaults save with an empty material library");
            store.Upsert(new("target",Path.Combine(root,"photo.png"),"photo",1,0,100,100,"png","SDR",""));
            store.Upsert(new("other",Path.Combine(root,"other.png"),"other",1,0,100,100,"png","SDR",""));
            var draft=store.EffectiveProfile("left","target");
            var added=EffectPatch.Apply(draft.Effects,"Distortion",true,.8,ToolCatalog.Json(new {}),1920,1080);
            store.SaveTargetEffects("left","target",draft with {Effects=added},draft.Effects);
            check(store.AssetProfile("target").Overrides==EffectKind.Distortion&&store.EffectiveProfile("left","target").Effects.Glow,"Different material effects combine without capturing unrelated display defaults");
            draft=store.EffectiveProfile("left","target");
            store.SaveTargetEffects("left","target",draft with {Effects=draft.Effects with {GlowStrength=.7}},draft.Effects);
            check(store.EffectiveProfile("left","target").Effects.GlowStrength==.7&&store.ScreenProfile("left")!.Effects.GlowStrength==.2&&store.EffectiveProfile("left","target").Effects.Noise,"Matching material group wins while remaining display effects inherit");
            draft=store.EffectiveProfile("left","target");
            store.SaveTargetEffects("left","target",draft with {Effects=draft.Effects with {Collection=draft.Effects.GetCollection().WithStrength(EffectKind.Scanlines,0)}},draft.Effects);
            check(store.AssetProfile("target").Overrides.HasFlag(EffectKind.Scanlines)&&store.EffectiveProfile("left","target").Effects.GetCollection().Scanlines==0,"Explicit zero strength blocks inherited display effect");
            store.CopyScreenEffectsToAsset("left","target");
            check(store.AssetProfile("target").Overrides==display.GetCollection().Added&&store.AssetProfile("target").Effects.GlowStrength==.2&&!store.AssetProfile("target").Effects.Distortion&&store.ScreenProfile("left")!.Effects.Noise,"Copy replaces target material effects and keeps source display effects");
            store.SaveTargetEffects("right",null,new(FillMode.Cover,new VideoEffects {Collection=new() {Added=EffectKind.Distortion},Distortion=true}),VideoEffects.Original);
            check(store.EffectiveProfile("right","target").Effects.Distortion&&store.EffectiveProfile("right","target").Effects.Glow,"Copied material effects still combine with unrelated effects on another display");
            store.ClearTargetEffects("left","target");
            check(store.EffectiveProfile("left","target").Effects.GetCollection().Added==0&&store.EffectiveProfile("left","other").Effects.Glow,"Material clear suppresses every inherited effect without changing other assets");
            store.CopyScreenEffectsToAsset("left","target");store.ClearTargetEffects("left",null);
            check(store.ScreenProfile("left")!.Effects.GetCollection().Added==0&&store.EffectiveProfile("left","target").Effects.Glow&&store.ScreenProfile("left")!.Fill==FillMode.Contain,"Display clear preserves material effects and screen fill");
            var mask=new EffectMask {Name="private unassigned"};draft=store.EffectiveProfile("left","target");
            store.SaveTargetEffects("left","target",draft with {Effects=draft.Effects with {Masks=[mask]}},draft.Effects);
            check(store.EffectiveProfile("left","target").Effects.Masks.Any(m=>m.Id==mask.Id)&&store.ScreenProfile("left")!.Effects.Masks.Length==0,"Unassigned material mask persists without leaking into display library");
            var sharedMask=new EffectMask {Name="screen mask"};
            var masked=display with {Masks=[sharedMask],EffectMasks=new() {[EffectKind.Glow]=sharedMask.Id}};
            store.SaveTargetEffects("masked",null,new(FillMode.Cover,masked),VideoEffects.Original);
            for(int i=0;i<20;i++) {
                var oldDraft=store.EffectiveProfile("masked","other");var maskId=oldDraft.Effects.EffectMasks[EffectKind.Glow];
                var nextMasks=oldDraft.Effects.Masks.Select(m=>m.Id==maskId?m with {X=.1+i*.01}:m).ToArray();
                store.SaveTargetEffects("masked","other",oldDraft with {Effects=oldDraft.Effects with {Masks=nextMasks}},oldDraft.Effects);
            }
            check(store.AssetProfile("other").Effects.Masks.Length==1&&store.ScreenProfile("masked")!.Effects.Masks.Single().X==.5&&store.AssetProfile("other").Overrides==EffectKind.Glow,"Repeated automatic material mask edits isolate geometry without accumulating copies or capturing unrelated effects");
            var unused=new EffectMask {Name="unused display mask"};
            store.SaveTargetEffects("unused",null,new(FillMode.Cover,VideoEffects.Original with {Masks=[unused]}),VideoEffects.Original);
            draft=store.EffectiveProfile("unused","target");
            store.SaveTargetEffects("unused","target",draft with {Effects=draft.Effects with {Masks=draft.Effects.Masks.Select(m=>m.Id==unused.Id?m with {X=.3}:m).ToArray()}},draft.Effects);
            check(store.EffectiveProfile("unused","target").Effects.Masks.Single(m=>m.Id==unused.Id).X==.3&&store.ScreenProfile("unused")!.Effects.Masks.Single().X==.5,"Editing an unassigned inherited mask saves to the material library only");
            var collision=new AssetEffects {Effects=VideoEffects.Original with {Masks=[new EffectMask {Name=unused.Name.ToUpperInvariant()}]}};
            check(collision.Resolve(VideoEffects.Original with {Masks=[unused]}).Masks.Select(m=>m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==2,"Material and display mask names resolve case-insensitive collisions");
            store.ClearTargetEffects("empty",null);
            var emptyUi=new MainWindow(store);
            var missing=await emptyUi.Call("effect_get",new {screenId="empty",scope="screen"});
            bool targetRejected=false;
            try {await emptyUi.Call("effect_clear",new {screenId="empty",scope="asset",expectedRevision=missing.Data.GetProperty("revision").GetString()});}catch(ArgumentException) {targetRejected=true;}
            check(targetRejected,"Material clear rejects a missing explicit target");
            var ui=new MainWindow(store);
            async Task<string> Revision(string scope,string? asset=null) {
                var reply=asset==null?await ui.Call("effect_get",new {screenId="left",scope}):await ui.Call("effect_get",new {screenId="left",scope,assetId=asset});
                return reply.Data.GetProperty("revision").GetString()!;
            }
            var revision=await Revision("screen");
            var changed=await ui.Call("effect_set",new {screenId="left",scope="screen",effect="Glow",enabled=true,expectedRevision=revision});
            check(changed.Data.GetProperty("saved").GetBoolean()&&changed.Data.GetProperty("renderConfirmed").ValueKind==System.Text.Json.JsonValueKind.Null,"Production MCP effect handler saves display effects with no playing asset");
            revision=await Revision("screen");await ui.Call("effect_clear",new {screenId="left",scope="screen",expectedRevision=revision});
            check(store.ScreenProfile("left")!.Effects.GetCollection().Added==0,"Production MCP clears display target without assetId");
            revision=await Revision("screen");await ui.Call("effect_undo",new {screenId="left",scope="screen",expectedRevision=revision});
            check(store.ScreenProfile("left")!.Effects.Glow,"Production MCP undo restores cleared display target");
            revision=await Revision("asset","target");await ui.Call("effect_copy_screen_to_asset",new {screenId="left",assetId="target",expectedRevision=revision});
            check(store.AssetProfile("target").Effects.Glow&&store.ScreenProfile("left")!.Effects.Glow,"Production MCP copies source display and retains source");
            revision=await Revision("asset","target");await ui.Call("effect_clear",new {screenId="left",assetId="target",scope="asset",expectedRevision=revision});
            check(!store.EffectiveProfile("left","target").Effects.GetCollection().Added.HasFlag(EffectKind.Glow)&&store.ScreenProfile("left")!.Effects.Glow,"Production MCP clear disables inherited material effects only");
            bool rejected=false;try {await ui.Call("effect_copy_screen_to_asset",new {screenId="left",assetId="target",expectedRevision=revision});}catch(LumaWall.Automation.OperationFault ex) when(ex.Code=="revision_conflict") {rejected=true;}
            check(rejected,"Stale revision rejects a material copy after another edit");
        }
        string local=Path.Combine(root,"cleanup","Local"),temporary=Path.Combine(root,"cleanup","Temp"),profile=Path.Combine(local,"LumaWall");
        using(var old=new LibraryStore(profile)) {
            old.Upsert(new("old","C:/original-image.png","old",1,0,100,100,"png","SDR",""));
            old.SaveProfile("left","old",new(FillMode.Contain,new VideoEffects {Glow=true}));old.SetSetting("widgets.v1",new WidgetConfiguration {Widgets=[new() {Id="cleanup-clock",Kind=WidgetKind.Clock}]}.ToJson());old.SetSetting("appearance.v1",new AppearanceOptions {Motion=false}.ToJson());old.SetSetting("automation.mcp.enabled","true");
            old.SetEffectScope("left","old",EffectKind.Glow,true);
        }
        SqliteConnection.ClearAllPools();
        var removed=PersonalDataCleanup.RemoveKnownData(local,temporary);
        check(removed.FailedItems==0&&!File.Exists(Path.Combine(profile,"library.db")),"Delete-personal-data removes the actual configuration database");
        using(var fresh=new LibraryStore(profile))
            check(fresh.Assets().Count==0&&fresh.ScreenProfile("left")==null&&fresh.AssetProfile("old").Overrides==0&&fresh.GetSetting("widgets.v1")==null&&fresh.GetSetting("appearance.v1")==null&&fresh.GetSetting("automation.mcp.enabled")==null,"Recreated installation starts without previous library, effects, widgets or settings");
    }
}
