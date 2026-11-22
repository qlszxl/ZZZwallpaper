using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.ControlCenter;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class AudioRuleChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        void Reject(Action action,string name) {try {action();}catch(ArgumentException) {check(true,name);return;}throw new Exception(name);}
        var glow=new AudioRule {Parameter="GlowStrength",Weak=.1,Strong=1.2};
        var scan=new AudioRule {Parameter="ScanlineSpeed",Weak=-2,Strong=4,Band=1};
        var defaults=VideoEffects.Original;
        check(!defaults.AudioLegacy&&defaults.AudioRules.Length==0&&!defaults.AudioReactive,"New configurations start with audio inactive and no rules");
        check(!defaults.AudioScanlines&&!defaults.AudioGlitch&&!defaults.AudioDots&&!defaults.AudioBlur&&!defaults.AudioDistortion&&!defaults.AudioGlow&&!defaults.AudioNoise&&!defaults.AudioZoom,"Retired classic switches stay off for all effect families");
        check(!VideoEffects.FromJson(null).AudioLegacy&&!VideoEffects.FromJson(defaults.ToJson()).AudioLegacy,"Fresh and saved audio defaults agree");
        var display=new VideoEffects {Collection=new() {Added=EffectKind.Glow|EffectKind.Scanlines|EffectKind.AudioReactive},Glow=true,GlowStrength=0,Scanlines=true,AudioReactive=true,AudioLegacy=false,AudioAmount=1,AudioRules=[glow,scan]};
        check(!display.AudioLegacy&&display.Version==40&&display.ToWire().StartsWith("37 "),"Custom-only audio rules use JSON 40 and built-in wire 37");
        var restored=VideoEffects.FromJson(display.ToJson());
        check(!restored.AudioLegacy&&restored.AudioRules.SequenceEqual(display.AudioRules),"Existing custom-only configurations retain their mode and rules");
        var legacyDocument=JsonNode.Parse(display.ToJson())!;
        legacyDocument["AudioLegacy"]=true;
        var classicWithRules=VideoEffects.FromJson(legacyDocument.ToJsonString());
        check(!classicWithRules.AudioLegacy&&classicWithRules.AudioRules.SequenceEqual(display.AudioRules),"Reading the retired classic flag preserves explicit custom rules");
        check(VideoEffects.FromJson(JsonSerializer.Serialize(display with {Version=21,AudioRules=[]})).AudioRules.Length==0,"Reading a v21 configuration does not invent explicit rules");
        check(display.ForRendering().Glow&&display.ForRendering().AudioRules.Length==2,"Zero static strength can be driven by an enabled rule");
        check((display with {Glow=false}).ForRendering().AudioRules.All(r=>r.Parameter!="GlowStrength"),"Rules cannot implicitly enable a disabled effect");
        check((display with {Collection=display.GetCollection().WithStrength(EffectKind.Glow,0)}).ForRendering().AudioRules.All(r=>r.Parameter!="GlowStrength"),"Zero effect strength suppresses its rules");
        var scaled=display with {Collection=display.GetCollection().WithStrength(EffectKind.Glow,.5)};
        check(Math.Abs(scaled.ForRendering().AudioRules.Single(r=>r.Parameter==glow.Parameter).Strong-.6)<1e-9&&scaled.AudioRules.Single(r=>r.Parameter==glow.Parameter).Strong==1.2,"Rendering scales rule endpoints without overwriting saved values");
        (glow with {Weak=2,Strong=.1}).Validate();check(true,"Reverse endpoints are accepted");
        Reject(()=>(display with {AudioRules=[glow,glow]}).Validate(),"Duplicate parameter rules are rejected");
        Reject(()=>(glow with {Weak=double.NaN}).Validate(),"Nonfinite endpoints are rejected");
        Reject(()=>(glow with {Strong=3.1}).Validate(),"Parameter-specific endpoints are bounded");
        Reject(()=>(glow with {Parameter="command"}).Validate(),"Unknown parameter rejected");
        Reject(()=>(glow with {Band=4}).Validate(),"Invalid band rejected");
        Reject(()=>(glow with {Attack=0}).Validate(),"Invalid independent attack rejected");
        Reject(()=>EffectPatch.Apply(display,"AudioReactive",true,null,ToolCatalog.Json(new {AudioRules=new[] {new AudioRule {Parameter="RgbOffset",Weak=0,Strong=500}}}),1920,1080),"Bulk MCP rules enforce display-specific RGB bound");
        check(AudioParameters.All.Select(p=>p.Id).Distinct().Count()==AudioParameters.All.Length&&AudioParameters.All.Select(p=>p.Key).Distinct().Count()==AudioParameters.All.Length&&AudioParameters.Selectable.Count(p=>p.Trigger)==5,"Catalog keys and wire IDs are unique and expose five beat triggers");
        var all=VideoEffects.Original with {AudioLegacy=false};
        foreach(var group in EffectPatch.Groups.Keys)all=EffectPatch.Apply(all,group,true,null,ToolCatalog.Json(new {}),1920,1080);
        all=all with {AudioAmount=1,AudioRules=AudioParameters.Selectable.Select(p=>new AudioRule {Parameter=p.Key,Weak=p.Minimum,Strong=p.Maximum}).ToArray()};
        var renderedRules=all.ForRendering().AudioRules.ToDictionary(r=>r.Parameter);
        foreach(var p in AudioParameters.Selectable) {
            if(p.Trigger) {
                var rule=renderedRules[p.Key];
                check(rule.Response==1&&rule.Weak==0&&rule.Strong==1,"Normalized beat trigger: "+p.Key);
                continue;
            }
            check(renderedRules[p.Key].Weak==p.Minimum&&renderedRules[p.Key].Strong==p.Maximum,"Rendering at full strength preserves exact endpoints: "+p.Key);
            foreach(double edge in new[] {p.Minimum,p.Maximum}) {
                var node=JsonNode.Parse(all.ToJson())!;var path=p.Key.Split('.');JsonNode owner=node;
                foreach(string name in path[..^1])owner=owner[name]!;
                owner[path[^1]]=edge;node.Deserialize<VideoEffects>()!.Validate();
            }
            check(p.IsEnabled(all)&&double.IsFinite(p.Read(all)),"Catalog endpoints and target: "+p.Key);
        }
        File.WriteAllText(Path.Combine(root,"audio-all-wire.txt"),all.ToWire());
        File.WriteAllText(Path.Combine(root,"audio-glow-wire.txt"),display.ToWire());
        File.WriteAllText(Path.Combine(root,"audio-baseline-wire.txt"),(display with {AudioRules=[]}).ToWire());
        File.WriteAllText(Path.Combine(root,"audio-static-strong-wire.txt"),(display with {GlowStrength=1.2,AudioRules=[]}).ToWire());
        var merged=new AssetEffects {Effects=VideoEffects.Original with {AudioRules=[glow with {Enabled=false}]}}.Resolve(display);
        check(!merged.AudioRules.Single(r=>r.Parameter==glow.Parameter).Enabled&&merged.AudioRules.Contains(scan),"Disabled material rule suppresses only the matching display rule");
        check(AssetEffects.FromScreen(display).Effects.AudioRules.SequenceEqual(display.AudioRules),"Copy display includes rules and retains source");
        using(var store=new LibraryStore(Path.Combine(root,"audio-rules"))) {
            store.Upsert(new("asset",Path.Combine(root,"a.png"),"a",1,0,100,100,"png","SDR",""));
            store.SaveProfile("left","",new(FillMode.Cover,display));
            var before=store.EffectiveProfile("left","asset");
            store.SaveTargetEffects("left","asset",before with {Effects=before.Effects with {AudioRules=[glow with {Strong=.7},scan]}},before.Effects);
            check(store.AssetProfile("asset").Effects.AudioRules.Length==1&&store.AssetProfile("asset").Overrides==0,"UI rule edit stores one override without capturing global audio settings");
            before=store.EffectiveProfile("left","asset");
            store.SaveTargetEffects("left","asset",before with {Effects=before.Effects with {GlowStrength=.3}},before.Effects);
            check(store.AssetProfile("asset").Effects.AudioRules.Length==1&&store.EffectiveProfile("left","asset").Effects.AudioRules.Length==2,"Static material edits keep unrelated audio inheritance");
            var ui=new MainWindow(store);
            async Task<string> Revision(string scope="asset")=>(scope=="asset"?await ui.Call("effect_get",new {screenId="left",assetId="asset",scope}):await ui.Call("effect_get",new {screenId="left",scope})).Data.GetProperty("revision").GetString()!;
            var discovery=await ui.Call("effect_get",new {screenId="left",assetId="asset",scope="effective"});
            check(discovery.Data.GetProperty("audioRules").GetArrayLength()==2&&discovery.Data.GetProperty("ownAudioRules").GetArrayLength()==1&&discovery.Data.GetProperty("audioParameters").EnumerateArray().Select(p=>p.GetProperty("Key").GetString()).SequenceEqual(AudioParameters.Selectable.Select(p=>p.Key)),"MCP exposes effective rules, own rules and parameter metadata");
            string revision=await Revision();
            await ui.Call("audio_set_rule",new {screenId="left",assetId="asset",scope="asset",expectedRevision=revision,rule=scan with {Enabled=false,Attack=.2,Release=1.5}});
            check(!store.EffectiveProfile("left","asset").Effects.AudioRules.Single(r=>r.Parameter==scan.Parameter).Enabled&&store.ScreenProfile("left")!.Effects.AudioRules.Contains(scan),"MCP disabled material rule leaves display untouched");
            bool conflict=false;try {await ui.Call("audio_remove_rule",new {screenId="left",assetId="asset",scope="asset",expectedRevision=revision,parameter=scan.Parameter});}catch(LumaWall.Automation.OperationFault ex) when(ex.Code=="revision_conflict") {conflict=true;}
            check(conflict,"Audio rule operations reject stale revision");
            await ui.Call("effect_undo",new {screenId="left",assetId="asset",scope="asset",expectedRevision=await Revision()});
            check(store.AssetProfile("asset").Effects.AudioRules.Length==1&&store.EffectiveProfile("left","asset").Effects.AudioRules.Contains(scan),"MCP undo restores inheritance after rule change");
            await ui.Call("audio_remove_rule",new {screenId="left",assetId="asset",scope="asset",expectedRevision=await Revision(),parameter=glow.Parameter});
            check(store.AssetProfile("asset").Effects.AudioRules.Length==0&&store.EffectiveProfile("left","asset").Effects.AudioRules.Contains(glow),"MCP remove material rule restores current display value");
            await ui.Call("effect_set",new {screenId="left",assetId="asset",scope="asset",expectedRevision=await Revision(),effect="AudioReactive",enabled=true,parameters=new {AudioSensitivity=2}});
            check(store.AssetProfile("asset").Effects.AudioRules.Length==0,"Changing global audio settings does not capture inherited parameter rules");
            await ui.Call("audio_set_rule",new {screenId="left",scope="screen",expectedRevision=await Revision("screen"),rule=glow with {Strong=2}});
            check(store.EffectiveProfile("left","asset").Effects.AudioRules.Single(r=>r.Parameter==glow.Parameter).Strong==2,"New display rules flow through material global-audio override");
            await ui.Call("effect_clear",new {screenId="left",assetId="asset",scope="asset",expectedRevision=await Revision()});
            check(!store.EffectiveProfile("left","asset").Effects.AudioReactive&&store.EffectiveProfile("left","asset").Effects.ForRendering().AudioRules.Length==0,"Clear material suppresses all rendered rules and effects");
        }
        using(var store=new LibraryStore(Path.Combine(root,"audio-rules")))check(store.ScreenProfile("left")!.Effects.AudioRules.Single(r=>r.Parameter==glow.Parameter).Strong==2,"Rules persist across database reopen");
    }
}
