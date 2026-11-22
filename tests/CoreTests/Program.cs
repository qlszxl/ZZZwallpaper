using LumaWall.Contracts;

int passed=0;
bool Same<T>(T a,T b)=>System.Text.Json.JsonSerializer.Serialize(a)==System.Text.Json.JsonSerializer.Serialize(b);
void Check(bool result,string name) {if(!result)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
if(args is ["--widget-locks"]) {WidgetLockChecks.Run(Check);Console.WriteLine($"{passed} independent widget lock checks passed.");return;}
WidgetLockChecks.Run(Check);
MenuPlacementChecks.Run(Check);
var appearance=new AppearanceOptions {Skin="lime",Pattern=false,Motion=false};
Check(AppearanceOptions.FromJson(appearance.ToJson())==appearance,"appearance round-trip preserves skin, pattern and motion");
Check(AppearanceOptions.FromJson("{broken")==new AppearanceOptions(),"malformed appearance safely uses defaults");
Check(AppearanceOptions.FromJson("{\"Skin\":\"missing\",\"Motion\":false}")==new AppearanceOptions {Motion=false},"unknown skin falls back without discarding other preferences");
Check(AppearanceOptions.FromJson(null)==new AppearanceOptions(),"first launch without appearance settings uses original palette");
Check(SkinPalette.All.Select(s=>s.Id).Distinct().Count()==SkinPalette.All.Length,"built-in skin identifiers are unique");

HitSnapshot Blank(long now=1000,long epoch=7) => new(now,100,100,true,"screen-2",epoch);

var click=new RightButtonSequence();
Check(click.Down(100,100,Blank(),1010,7,true).Suppress,"blank desktop captures down");
var up=click.Up(100,100,true);
Check(up.Suppress&&up.Menu?.ScreenId=="screen-2","matching release is captured and routes to hit monitor");
Check(!click.Up(100,100,true).Suppress,"stray release is passed through");

foreach(var scenario in new[]{
    ("stale snapshot",Blank(800),100,100,7L,true),
    ("future snapshot",Blank(1200),100,100,7L,true),
    ("cursor moved",Blank(),120,100,7L,true),
    ("window topology changed",Blank(),100,100,8L,true),
    ("menu service unavailable",Blank(),100,100,7L,false),
    ("desktop icon or another window",Blank() with {IsBlank=false},100,100,7L,true)
}) {
    var state=new RightButtonSequence();
    Check(!state.Down(scenario.Item3,scenario.Item4,scenario.Item2,1010,scenario.Item5,scenario.Item6).Suppress,scenario.Item1+" passes down");
    Check(!state.Up(scenario.Item3,scenario.Item4,true).Suppress,scenario.Item1+" passes matching up");
}
var drag=new RightButtonSequence();drag.Down(100,100,Blank(),1010,7,true);drag.Move(200,100,4,4);drag.Move(100,100,4,4);
var dragUp=drag.Up(100,100,true);Check(dragUp.Suppress&&dragUp.Menu==null,"cross-region drag returning to origin does not open menu or leak release");
var unavailable=new RightButtonSequence();unavailable.Down(100,100,Blank(),1010,7,true);
var unavailableUp=unavailable.Up(100,100,false);Check(unavailableUp.Suppress&&unavailableUp.Menu==null,"service loss after captured down still swallows matching release");
var held=new RightButtonSequence();held.Down(100,100,Blank(),1010,7,true);
Check(held.Down(100,100,null,1010,7,false).Suppress,"duplicate down cannot turn a captured sequence into partial passthrough");
var jitter=new RightButtonSequence();jitter.Down(101,101,Blank(),1010,7,true);jitter.Move(103,102,4,4);
Check(jitter.Up(103,102,true).Menu!=null,"small touchpad jitter remains a click");

var pause=new PauseState();pause.Set(PauseReason.User,true);pause.Set(PauseReason.Fullscreen,true);pause.Set(PauseReason.DisplayOff,true);
pause.Set(PauseReason.Fullscreen,false);pause.Set(PauseReason.DisplayOff,false);
Check(pause.IsPaused&&pause.Reasons==PauseReason.User,"exiting fullscreen and waking display do not clear user pause");
pause.Set(PauseReason.SessionLocked,true);pause.Set(PauseReason.User,false);
Check(pause.IsPaused,"manual resume cannot override session lock");pause.Set(PauseReason.SessionLocked,false);Check(!pause.IsPaused,"playback resumes after final pause reason clears");

var revisions=new RevisionGate();long first=revisions.Next();long second=revisions.Next();
Check(!revisions.IsCurrent(first)&&revisions.IsCurrent(second),"late completion cannot commit over a newer request");
var huge=new MediaAsset("x","local","large",20L*1024*1024*1024,36000,1920,1080,"h264","SDR","Software");
Check(huge.Bytes>uint.MaxValue,"media metadata preserves sizes beyond 4 GiB");
var scene=new DesktopSceneSnapshot(1000,7,new(-2560,0,3440,1440),[new("left",new(-2560,0,0,1440)),new("main",new(0,0,3440,1440))],[new(50,50,150,180)],[new(-1800,400,-600,1100),new(0,1400,3440,1440)]);
Check(scene.Hit(-2500,500).IsBlank&&scene.Hit(-2500,500).ScreenId=="left","negative-coordinate monitor blanks identified");
Check(!scene.Hit(70,160).IsBlank,"icon label bounds pass through");
Check(!scene.Hit(-1200,500).IsBlank,"application occluding desktop passes through");
Check(!scene.Hit(300,1410).IsBlank,"taskbar passes through");
Check(!scene.Hit(4000,500).IsBlank,"outside physical display passes through");
var instant=new RightButtonSequence();Check(instant.Down(-2300,200,scene.Hit(-2300,200),1010,7,true).Suppress,"instant click after large cursor movement uses geometry, without waiting for cursor polling");
foreach(uint evt in new uint[]{0x8005,0x8006,0x8007,0x800A,0x800C})
    Check(!DesktopEventPolicy.ChangesGeometry(evt,-4,1,true,false),$"desktop focus/selection/name event {evt:X} preserves valid geometry");
foreach(uint evt in new uint[]{0x8000,0x8001,0x8002,0x8003,0x8004,0x800B})
    Check(DesktopEventPolicy.ChangesGeometry(evt,-4,1,true,false),$"icon geometry event {evt:X} invalidates cache");
Check(DesktopEventPolicy.ChangesGeometry(3,0,0,false,true),"foreground window reorder invalidates occlusions");
Check(DesktopEventPolicy.ChangesGeometry(0x800B,0,0,false,true),"moving application invalidates occlusions");
Check(!DesktopEventPolicy.ChangesGeometry(0x800B,-4,1,false,true),"moving control inside another application preserves desktop geometry");
Check(VideoEffects.Original.IsNeutral,"new media defaults to neutral effects");
var effects=new VideoEffects {Collection=new() {Added=EffectKind.Color|EffectKind.Scanlines|EffectKind.RgbSplit|EffectKind.Vignette|EffectKind.Glow|EffectKind.Noise},Scanlines=true,RgbSplit=true,Softening=true,Contrast=1.05,Saturation=.95,Vignette=true,Glow=true,Brightness=.1,Noise=true,NoiseStrength=.005};
Check(VideoEffects.FromJson(effects.ToJson()).ToJson()==effects.ToJson(),"versioned effect parameters round-trip");
Check(VideoEffects.FromJson("{}").IsNeutral,"old empty effects object upgrades to neutral");
Check((VideoEffects.Original with {Scanlines=true,ScanlineStrength=0,RgbSplit=true,RgbOffset=0,Softening=true,SoftRadius=0,Glow=true,GlowStrength=0,Vignette=true,VignetteStrength=0,Noise=true,NoiseStrength=0}).IsNeutral,"zero strength skips all effect passes");
Check((effects with {Intensity=0}).IsNeutral,"master intensity zero bypasses every effect");
var legacy=VideoEffects.FromJson("{\"Version\":1,\"ScreenTexture\":true,\"TextureStrength\":0.8,\"Brightness\":0.1,\"Glow\":true,\"GlowStrength\":0.6}");
Check(legacy.Version==VideoEffects.Original.Version&&legacy.Scanlines&&legacy.RgbSplit&&!legacy.Softening&&!legacy.GetCollection().Added.HasFlag(EffectKind.Softening)&&Math.Abs(legacy.RgbOffset-1.2)<1e-9&&legacy.GlowStrength==.3&&legacy.Brightness==.1&&!legacy.Noise,"v1 grouped effects migrate to current settings and retire softening");
Check(VideoEffects.FromJson(legacy.ToJson()).ToJson()==legacy.ToJson(),"migrated effects remain stable after saving and reopening");
foreach(var invalid in new[]{effects with {Brightness=double.NaN},effects with {Contrast=double.PositiveInfinity},effects with {Saturation=2.01},effects with {GlowStrength=-.1},effects with {Version=7},effects with {Intensity=1.01},effects with {ScanlineStrength=.36},effects with {ScanlineDensity=0},effects with {ScanlineDensity=261},effects with {RgbOffset=16384.01},effects with {BlurRadius=100.01},effects with {GlowThreshold=1.01},effects with {NoiseStrength=.301}}) {
    bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"invalid or unsupported effect parameters rejected");
}
var culture=System.Globalization.CultureInfo.CurrentCulture;
try {System.Globalization.CultureInfo.CurrentCulture=new("fr-FR");Check(effects.ToWire().Contains("0.100000")&&!effects.ToWire().Contains(','),"effect protocol uses invariant decimal format");}
finally {System.Globalization.CultureInfo.CurrentCulture=culture;}
var half=effects with {Collection=effects.Collection! with {Scanlines=.5}};
Check(half.ForRendering().ScanlineStrength==effects.ScanlineStrength*.5&&half.ForRendering().GlowStrength==effects.GlowStrength,"one effect strength does not scale its neighbors");
Check(half.ScanlineStrength==effects.ScanlineStrength&&half.ScanlineDensity==effects.ScanlineDensity,"intensity projection preserves saved detailed parameters");
var paused=half with {Scanlines=false};
Check((VideoEffects.FromJson(paused.ToJson()).Collection!.Added&EffectKind.Scanlines)!=0&&!paused.ForRendering().Scanlines,"paused effects remain in the collection");
var removed=half with {Collection=half.Collection! with {Added=half.Collection!.Added&~EffectKind.Scanlines}};
Check(!removed.ForRendering().Scanlines&&removed.ScanlineDensity==effects.ScanlineDensity,"removed membership gates rendering while preserving parameters");
var previous=System.Text.Json.JsonSerializer.Serialize(effects with {Version=11,Collection=null});
var upgraded=VideoEffects.FromJson(previous);
Check(upgraded.Version==VideoEffects.Original.Version&&upgraded.ToWire()==effects.ToWire(),"v11 enabled effects migrate without changing native protocol values");
var satellites=new VideoEffects {LargeGlitch=true,GlitchColorShift=true,GlitchHueShift=70,Collection=new() {Added=EffectKind.LargeGlitch,Glitch=0}};
Check(satellites.ForRendering().GlitchHueShift==70&&satellites.ForRendering().LargeGlitchBlend==satellites.LargeGlitchBlend,"digital glitch strength preserves large glitch satellite colors");
var everything=new VideoEffects {Collection=new() {Added=EffectCollection.All},Brightness=.2,Contrast=1.2,Saturation=.8,RgbSplit=true,Softening=true,Glow=true,Vignette=true,Noise=true,NoiseStrength=.1,Scanlines=true,Glitch=true,LargeGlitch=true,Dots=true,DotRipple=true,Blur=true,Distortion=true,AudioReactive=true,ZoomTunnel=new() {Enabled=true},Particles=new() {Enabled=true},WaterWave=new() {Enabled=true},EdgeOutline=new() {Enabled=true},Pixelate=new() {Enabled=true},SunPrism=new() {Enabled=true},LightFlare=new() {Enabled=true},Transform=new() {Enabled=true,Scale=1.5}};
Check(everything.GetCollection().WithStrength(EffectKind.MaskLibrary,0)==everything.GetCollection(),"mask library is a resource container without a strength stage");
foreach(var kind in Enum.GetValues<EffectKind>().Where(k=>EffectCollection.All.HasFlag(k)&&k!=EffectKind.MaskLibrary)) {
    var zero=(everything with {Collection=everything.Collection!.WithStrength(kind,0)}).ForRendering();
    bool bypass=kind switch {
        EffectKind.Color=>zero.Brightness==0&&zero.Contrast==1&&zero.Saturation==1,
        EffectKind.RgbSplit=>!zero.RgbSplit,EffectKind.Glow=>!zero.Glow,EffectKind.Vignette=>!zero.Vignette,
        EffectKind.Noise=>!zero.Noise,EffectKind.Scanlines=>!zero.Scanlines,EffectKind.Glitch=>!zero.Glitch,EffectKind.LargeGlitch=>!zero.LargeGlitch,
        EffectKind.Dots=>!zero.Dots,EffectKind.DotRipple=>!zero.DotRipple,EffectKind.Blur=>!zero.Blur,EffectKind.Distortion=>!zero.Distortion,EffectKind.AudioReactive=>!zero.AudioReactive,EffectKind.HideTaskbar=>!zero.HidesTaskbar,EffectKind.ZoomTunnel=>!zero.ZoomTunnel.Enabled,EffectKind.Particles=>!zero.Particles.Enabled,EffectKind.WaterWave=>!zero.WaterWave.Enabled,EffectKind.EdgeOutline=>!zero.EdgeOutline.Enabled,EffectKind.Pixelate=>!zero.Pixelate.Enabled,EffectKind.SunPrism=>!zero.SunPrism.Enabled,
        EffectKind.LightFlare=>!zero.LightFlare.Enabled,EffectKind.Transform=>!zero.Transform.Enabled,_=>false
    };
    Check(bypass,$"zero {kind} strength bypasses only its rendering stage");
}
foreach(var invalid in new[]{new EffectCollection {Added=(EffectKind)(1<<30)},new EffectCollection {Color=double.NaN},new EffectCollection {Blur=-.01},new EffectCollection {AudioReactive=1.01}}) {
    bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"invalid effect collections rejected");
}
var savedWidget=new WidgetInstance {Kind=WidgetKind.Performance,Locked=true,Metrics=PerformanceMetrics.Gpu|PerformanceMetrics.NetworkReceive,LayoutGroupId="group",LayoutOrder=2,X=.25,Y=.75};
var oldWidgets=new WidgetConfiguration {Version=5,Widgets=[savedWidget],LayoutGroups=[new() {Id="group",X=.3,Y=.4,Locked=true}]};
var migratedWidgets=WidgetConfiguration.FromJson(System.Text.Json.JsonSerializer.Serialize(oldWidgets));
Check(migratedWidgets.Version==18&&Same(migratedWidgets.Widgets[0],savedWidget)&&migratedWidgets.LayoutGroups[0]==oldWidgets.LayoutGroups[0],"v5 migration keeps widget groups, locks, positions and metrics");
var withSpectrum=migratedWidgets with {Widgets=[..migratedWidgets.Widgets,new() {Kind=WidgetKind.Spectrum,Spectrum=new() {Bands=64,Width=800,Height=240,Sensitivity=4,SmoothingMs=800,Mirror=true,Background=false}}]};
Check(Same(WidgetConfiguration.FromJson(withSpectrum.ToJson()).Widgets.Last().Spectrum,withSpectrum.Widgets.Last().Spectrum),"spectrum options persist through configuration roundtrip");
foreach(var invalid in new[]{new SpectrumOptions {Width=23},new SpectrumOptions {Height=16385},new SpectrumOptions {Bands=257},new SpectrumOptions {Sensitivity=double.NaN},new SpectrumOptions {SmoothingMs=49}}) {
    bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"invalid spectrum options rejected");
}
var excludedClick=new RightButtonSequence();
Check(!excludedClick.Down(100,100,Blank(),1010,7,false).Suppress&&!excludedClick.Up(100,100,false).Suppress,"disabled screen policy passes both right-button events to Windows");

var zoomOnly=new VideoEffects {AudioReactive=true,Transform=new() {Enabled=true},AudioRules=[new() {Parameter="AudioZoomAmount",Strong=.15}],Collection=new() {Added=EffectKind.AudioReactive|EffectKind.Transform}};
Check(!zoomOnly.IsNeutral&&zoomOnly.ForRendering().AudioRules.Single().Strong==.15,"audio zoom rule drives an otherwise neutral transform");
Check(VideoEffects.FromJson(zoomOnly.ToJson()).ToJson()==zoomOnly.ToJson(),"audio zoom roundtrips without parameter loss");
Check((zoomOnly with {Collection=zoomOnly.Collection! with {AudioReactive=0}}).IsNeutral,"zero mapping strength disables zoom");
var hiding=new VideoEffects {Collection=new() {Added=EffectKind.HideTaskbar}};
Check(hiding.HidesTaskbar&&hiding.IsNeutral,"taskbar visibility is independent of pixel rendering");
Check(!(hiding with {Collection=hiding.Collection! with {HideTaskbarEnabled=false}}).HidesTaskbar,"taskbar switch restores visibility");
Check(VideoEffects.FromJson(System.Text.Json.JsonSerializer.Serialize(effects with {Version=12})).ToJson()==effects.ToJson(),"v12 migration retains membership and strengths");
var arbitrary=new SpectrumOptions {Bands=117,Width=3440,Height=1440,Gap=.42,Gradient=true,Peaks=true,Direction=3};arbitrary.Validate();
var visualState=new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Text,Visual=new() {Text="第一行\n第二行",FontSize=32,ClickThrough=true}},new() {Kind=WidgetKind.Sticker,Visual=new() {StickerShape=3}},new() {Kind=WidgetKind.Mask,Visual=new() {FillOpacity=.3,Gradient=2}},new() {Kind=WidgetKind.Spectrum,Spectrum=arbitrary}]};
Check(Same(WidgetConfiguration.FromJson(visualState.ToJson()).Widgets,visualState.Widgets),"new components and custom spectrum roundtrip");
var legacyWidgets=System.Text.Json.JsonSerializer.Serialize(visualState with {Version=6,Widgets=[savedWidget],LayoutGroups=oldWidgets.LayoutGroups});
Check(Same(WidgetConfiguration.FromJson(legacyWidgets).Widgets[0],savedWidget),"v6 migration keeps the saved instance");
foreach(var invalid in new[]{new VisualWidgetOptions {Width=0},new VisualWidgetOptions {TextColor="#oops"},new VisualWidgetOptions {Image="not png"},new VisualWidgetOptions {FontSize=double.NaN}}) {
    bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}catch(FormatException) {rejected=true;}Check(rejected,"invalid custom visual settings rejected");
}


var wideRgb=effects with {RgbOffset=258,Collection=effects.Collection! with {RgbSplit=.5}};
Check(VideoEffects.FromJson(wideRgb.ToJson()).ToJson()==wideRgb.ToJson()&&wideRgb.ForRendering().RgbOffset==129,"large RGB offsets persist and retain per-effect intensity");
Check(wideRgb.ToWire().StartsWith("37 ")&&wideRgb.ToWire().Contains("129.000000"),"expanded RGB preserves its scaled offset in the native protocol");
Check(VideoEffects.FromJson(System.Text.Json.JsonSerializer.Serialize(effects with {Version=13})).ToJson()==effects.ToJson(),"v13 RGB migrates without altering the saved reference offset");
var layerState=new WidgetConfiguration {LayoutGroups=[new() {Id="stack"}],Widgets=[
    new() {Id="first"},new() {Id="second"},new() {Id="high",Layer=3},new() {Id="low",Layer=-2},
    new() {Id="group-second",LayoutGroupId="stack",LayoutOrder=2},new() {Id="group-first",LayoutGroupId="stack",LayoutOrder=1},new() {Id="hidden",Layer=99,Enabled=false}]};
Check(WidgetStacking.FrontToBack(layerState).Select(w=>w.Id).SequenceEqual(new[]{"high","group-first","group-second","first","second","low"}),"higher layers cover lower; ties use visible group/member/list order; hidden widgets excluded");
Check(Same(WidgetConfiguration.FromJson(layerState.ToJson()).Widgets,layerState.Widgets),"layer edits survive persistence without moving widgets");
var oldLayerJson=System.Text.Json.JsonSerializer.Serialize(layerState with {Version=7,Widgets=[new() {Id="legacy",Locked=true}]});
Check(WidgetConfiguration.FromJson(oldLayerJson).Widgets[0] is {Layer:0,Locked:true},"v7 migration defaults to layer zero and retains locks");
var lockedMember=layerState.Widgets[4] with {Locked=true};
var memberLockedState=layerState with {Widgets=layerState.Widgets.Select(w=>w.Id==lockedMember.Id?lockedMember:w).ToArray()};
Check(memberLockedState.IsEditingLocked(lockedMember)&&!memberLockedState.IsEditingLocked(layerState.Widgets[5])&&memberLockedState.IsDragLocked(layerState.Widgets[5]),"member lock blocks only its own editing and all group movement");
var groupLockedState=layerState with {LayoutGroups=[layerState.LayoutGroups[0] with {Locked=true}]};
Check(groupLockedState.IsEditingLocked(layerState.Widgets[4])&&groupLockedState.IsEditingLocked(layerState.Widgets[5])&&!groupLockedState.IsEditingLocked(layerState.Widgets[0]),"group lock blocks all member editing without affecting independent widgets");
foreach(int invalidLayer in new[]{-1000,1000}) {
    bool rejected=false;try {(layerState with {Widgets=[new() {Layer=invalidLayer}]}).Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"out-of-range widget layers rejected");
}

var randomized=everything with {LargeGlitchRandomize=true,LargeGlitchVariation=.4};
Check(VideoEffects.FromJson(randomized.ToJson()).ToJson()==randomized.ToJson(),"large glitch variation persists without rewriting base parameters");
Check(randomized.ForRendering().LargeGlitchVariation==.4&&randomized.LargeGlitchArea==everything.LargeGlitchArea,"randomization keeps configured base values and per-effect strength");
Check(string.Join(" ",randomized.ToWire().Split(' ').Skip(149).Take(6))=="1 0.400000 0 0.000000 0 0.000000","native v31 retains randomization and dot fields at their compatible prefix positions");
Check(!VideoEffects.FromJson(System.Text.Json.JsonSerializer.Serialize(effects with {Version=14})).LargeGlitchRandomize,"v14 profiles migrate with randomization off");
foreach(double invalidRange in new[]{-.01,1.01,double.NaN}) {
    bool rejected=false;try {(randomized with {LargeGlitchVariation=invalidRange}).Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"random range must be finite and within 0 to 100 percent");
}
var legacySearchJson="{\"Version\":8,\"Widgets\":[{\"Id\":\"search\",\"Kind\":0,\"Locked\":true,\"Layer\":12}]}";
var migratedSearch=WidgetConfiguration.FromJson(legacySearchJson);
Check(migratedSearch.Version==18&&migratedSearch.Widgets.Single() is {Locked:true,Layer:12,Search:{Width:360,Height:44,Radius:22}},"v8 migration keeps locks and layers and defaults search to its previous size");
var resizedSearch=migratedSearch with {Widgets=[migratedSearch.Widgets[0] with {Search=new() {Width=720,Height=80,Radius=14}}]};
Check(WidgetConfiguration.FromJson(resizedSearch.ToJson()).Widgets[0].Search==resizedSearch.Widgets[0].Search,"search dimensions and radius persist");
Check(new SearchWidgetOptions {Radius=200}.RadiusFor(120,44)==22&&new SearchWidgetOptions {Radius=0}.RadiusFor(720,80)==0,"search corners clamp to the current footprint and allow square corners");
foreach(var invalid in new[]{new SearchWidgetOptions {Width=119},new SearchWidgetOptions {Width=16385},new SearchWidgetOptions {Height=43},new SearchWidgetOptions {Height=16385},new SearchWidgetOptions {Radius=-1},new SearchWidgetOptions {Radius=double.NaN},new SearchWidgetOptions {Radius=8193}}) {
    bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"invalid search dimensions and radius rejected");
}
SpectrumChecks.Run(Check);
DotShapeChecks.Run(Check);
EffectOrderChecks.Run(Check);
Console.WriteLine($"{passed} checks passed.");
