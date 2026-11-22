using System.Numerics;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

internal static class EffectOrderChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var expected=Enumerable.Range(0,13).Where(i=>i!=2).Concat(Enumerable.Range(15,6)).Concat(new[]{23,24}).ToArray();
        int Index(EffectKind kind)=>BitOperations.TrailingZeroCount((uint)kind);
        string[] StackFields(VideoEffects effects) {var fields=effects.ToWire().Split(' ');int order=RenderWireFixture.OrderOffset(fields);return fields[..(order+1+int.Parse(fields[order]))];}
        void Reject(Action action,string label) {bool rejected=false;try {action();}catch(ArgumentException) {rejected=true;}check(rejected,label);}
        check(EffectOrder.Default.Count==20&&EffectOrder.Default.Select(Index).Order().SequenceEqual(expected),"Visual stack contains each of the 20 renderer stages exactly once");
        check(EffectOrder.Default.Last()==EffectKind.LightFlare&&EffectOrder.Default[^2]==EffectKind.Particles&&!EffectOrder.Supports(EffectKind.AudioReactive)&&!EffectOrder.Supports(EffectKind.HideTaskbar)&&!EffectOrder.Supports(EffectKind.MaskLibrary),"Light flare follows particles and nonvisual controls cannot become render stages");

        var mask=new EffectMask {Id="stack-mask",Name="Stack mask"};
        var original=new VideoEffects {Glow=true,GlowStrength=.4,Brightness=.1,
            Collection=new() {Added=EffectKind.Color|EffectKind.Glow,Glow=.3},Masks=[mask],EffectMasks=new() {[EffectKind.Glow]=mask.Id}};
        string defaultWire=original.ToWire();
        var collection=original.GetCollection().WithLayer(EffectKind.Glow,-20);
        var custom=original with {Collection=collection};
        var wire=StackFields(custom);var defaults=StackFields(original);
        check(defaults[0]=="37"&&wire[0]=="37"&&wire.SkipLast(23).SequenceEqual(defaults.SkipLast(23)),"Reordering changes only the rendering wire sequence without changing parameter strengths or masks");
        check(wire.Length==defaults.Length&&wire[^23]=="22"&&wire.TakeLast(22).Take(20).Select(int.Parse).SequenceEqual(collection.OrderedKinds().Select(Index)),"Wire order block contains count and sorted bit indices rather than flag integers");
        check(!original.GetCollection().HasCustomOrder&&defaults[^23]=="22"&&defaults.TakeLast(22).Take(20).Select(int.Parse).SequenceEqual(original.GetCollection().OrderedKinds().Select(Index)),"A profile without saved layer overrides sends its displayed default order to the renderer");
        var implicitProfile=VideoEffects.Original with {Brightness=.1};string implicitJson=implicitProfile.ToJson();
        var implicitWire=StackFields(implicitProfile);
        check(implicitProfile.Collection==null&&implicitProfile.ToJson()==implicitJson&&implicitWire[0]=="37"&&implicitWire.TakeLast(22).Take(20).Select(int.Parse).SequenceEqual(EffectOrder.Default.Select(Index)),"Rendering an implicit collection sends the central sequence without adding stored layer metadata");
        check((original with {Collection=original.GetCollection() with {Layers=new()}}).ToWire()==defaultWire,"An explicit empty layer map and no layer overrides render the same sequence");
        check(collection.HasCustomOrder&&collection.OrderedKinds()[0]==EffectKind.Glow&&collection.Layers!.Count==20,"First order adjustment records a complete reordered visual sequence");
        check((custom with {Collection=collection.ResetOrder()}).ToWire()==defaultWire,"Resetting display ordering restores the central sequence on the rendering wire");
        check(custom.GlowStrength==original.GlowStrength&&custom.GetCollection().Glow==.3&&custom.Masks.Single()==mask&&custom.EffectMasks[EffectKind.Glow]==mask.Id,"Ordering preserves unscaled parameters, per-effect strength and mask links");

        var tied=collection with {Layers=EffectOrder.Default.Reverse().ToDictionary(kind=>kind,_=>5)};
        check(tied.OrderedKinds().SequenceEqual(EffectOrder.Default),"Equal numeric layers use the central deterministic default order");
        var moved=new EffectCollection().Move(EffectKind.Particles,0);
        check(moved.OrderedKinds()[0]==EffectKind.Particles&&moved.Move(EffectKind.Particles,18).OrderedKinds().SequenceEqual(EffectOrder.Default),"Move targets the final 0-based index in the entire 20-stage stack");
        check(new EffectCollection().Move(EffectKind.WaterWave,EffectKind.Glow).OrderedKinds().ToList().IndexOf(EffectKind.WaterWave)+1==new EffectCollection().Move(EffectKind.WaterWave,EffectKind.Glow).OrderedKinds().ToList().IndexOf(EffectKind.Glow),"Move-before handles removing a source before its target");
        foreach(var kind in new[] {EffectKind.AudioReactive,EffectKind.HideTaskbar,EffectKind.MaskLibrary,EffectKind.Glow|EffectKind.Color})
            Reject(()=>collection.WithLayer(kind,0),"Nonvisual and combined kinds reject layer adjustment");
        foreach(int layer in new[] {-1000,1000})Reject(()=>collection.WithLayer(EffectKind.Glow,layer),"Numeric layers outside the bounded range reject");
        foreach(int index in new[] {-1,20})Reject(()=>collection.Move(EffectKind.Glow,index),"Move rejects indices outside the full stack");
        Reject(()=> (original with {Collection=collection with {Layers=new() {[EffectKind.AudioReactive]=1}}}).Validate(),"Persisted nonvisual stage keys reject");
        Reject(()=> (original with {Collection=collection with {Layers=new() {[EffectKind.Glow]=1000}}}).Validate(),"Persisted out-of-range layers reject");

        var reversed=custom with {Collection=collection with {Layers=collection.Layers!.Reverse().ToDictionary(pair=>pair.Key,pair=>pair.Value)}};
        check(EffectOrder.Equivalent(collection.Layers,reversed.Collection!.Layers)&&custom.ToJson()==reversed.ToJson()&&AssetEffects.ChangedKinds(custom,reversed)==0,"Equivalent layer maps have canonical persistence and no false change from insertion order");
        var roundtrip=VideoEffects.FromJson(custom.ToJson());
        check(roundtrip.Version==VideoEffects.Original.Version&&roundtrip.ToJson()==custom.ToJson()&&roundtrip.ToWire()==custom.ToWire(),"JSON roundtrip retains layers and native stage order");
        var migrated=VideoEffects.FromJson(JsonSerializer.Serialize(original with {Version=28}));
        check(migrated.Version==VideoEffects.Original.Version&&!migrated.GetCollection().HasCustomOrder&&migrated.ToWire()==defaultWire,"JSON 28 migration retains parameters and uses the central visual sequence");
        check(!VideoEffects.FromJson(JsonSerializer.Serialize(custom with {Version=28})).GetCollection().HasCustomOrder,"An old-version document cannot introduce unsupported layer metadata");
        check(AssetEffects.ChangedKinds(original,custom,includeOrder:false)==0&&(AssetEffects.ChangedKinds(original,custom)&EffectKind.Glow)!=0,"Order-only changes are detectable without promoting material parameter scopes");
        check(EffectProjection.Isolate(custom,EffectKind.Glow).GetCollection().OrderedKinds().SequenceEqual(collection.OrderedKinds()),"Preview projection keeps ordering while isolating effect membership");

        var copied=AssetEffects.Copy(custom,VideoEffects.Original,EffectKind.Glow);
        check(copied.GetCollection().Layers is {Count:1}&&copied.GetCollection().GetLayer(EffectKind.Glow)==-20&&copied.GetCollection().Strength(EffectKind.Glow)==.3,"Copying one effect preserves its layer and strength without capturing unrelated layer overrides");
        check(EffectOrder.Equivalent(AssetEffects.FromScreen(custom).Effects.GetCollection().Layers,collection.Layers),"Copy-display-to-material preserves complete ordering, including dormant stages");
        var document=ShareProjection.Create(custom,new WidgetConfiguration(),new ShareSelection {Effects=EffectKind.Glow});
        var shared=ShareCode.Decode(ShareCode.Encode(document)).Effects;
        check(shared.GetCollection().Layers is {Count:1}&&shared.GetCollection().GetLayer(EffectKind.Glow)==-20&&shared.GetCollection().Added.HasFlag(EffectKind.Glow),"Selective share code roundtrip retains only selected visual layer overrides");
        check(shared.GetCollection().Strength(EffectKind.Glow)==.3&&shared.EffectMasks.ContainsKey(EffectKind.Glow),"Sharing ordering retains existing effect strength and selected mask association");
        var defaultDocument=ShareProjection.Create(original,new WidgetConfiguration(),new ShareSelection {Effects=EffectKind.Glow});
        var sharedDefault=ShareCode.Decode(ShareCode.Encode(defaultDocument)).Effects;
        check(!sharedDefault.GetCollection().HasCustomOrder&&sharedDefault.ToWire().StartsWith("37 "),"Sharing a profile without layer overrides preserves default ordering on the rendering wire");
    }
}
