using System.Text;
using LumaWall.Contracts;

if(args.Length!=1)throw new ArgumentException("Pass the isolated fixture output directory.");
string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
var fixtures=new List<string>();
void Add(string name,string source,VideoEffects effects)=>fixtures.Add(name+"\t"+source+"\t"+effects.ToWire());
void AddLegacy(string name,string source,VideoEffects effects)=>fixtures.Add(name+"\t"+source+"\t"+string.Join(" ",RenderWireFixture.Legacy25(effects.ToWire())));
VideoEffects Stack(VideoEffects fx,params EffectKind[] order) {
    var collection=fx.GetCollection();int level=-900;
    foreach(var kind in order) {collection=collection.WithLayer(kind,level);level+=10;}
    return fx with {Collection=collection};
}
VideoEffects Only(EffectKind kind,VideoEffects parameters)=>parameters with {Collection=new() {Added=kind}};

var neutral=VideoEffects.Original;
Add("original","color",neutral);
Add("neutral-stack","color",Stack(neutral,EffectKind.Color));
Add("black","black",neutral);
var particle=new ParticleOptions {Enabled=true,Count=100,Size=65,Speed=0,Sway=0,Rotation=0,Twinkle=0,Fade=false,
    SizeRandom=0,SpeedRandom=0,Spread=0,ColorEnabled=true,Color="#FF4010",Opacity=.9};
var particles=Only(EffectKind.Particles,new() {Particles=particle});
var color=Only(EffectKind.Color,new() {Brightness=.13,Contrast=1.1,Saturation=.25});
var blur=Only(EffectKind.Blur,new() {Blur=true,BlurRadius=28,BlurStrength=1});
var glow=Only(EffectKind.Glow,new() {Glow=true,GlowStrength=1.4,GlowThreshold=.1});
var singles=new Dictionary<string,(EffectKind Kind,VideoEffects Fx)> {
    ["color"]=(EffectKind.Color,color),["blur"]=(EffectKind.Blur,blur),["glow"]=(EffectKind.Glow,glow),
    ["particles"]=(EffectKind.Particles,particles),
    ["vignette"]=(EffectKind.Vignette,Only(EffectKind.Vignette,new() {Vignette=true,VignetteStrength=.75})),
    ["rgb"]=(EffectKind.RgbSplit,Only(EffectKind.RgbSplit,new() {RgbSplit=true,RgbOffset=22})),
    ["scan"]=(EffectKind.Scanlines,Only(EffectKind.Scanlines,new() {Scanlines=true,ScanlineStrength=.3})),
    ["noise"]=(EffectKind.Noise,Only(EffectKind.Noise,new() {Noise=true,NoiseStrength=.2,ColoredNoise=true})),
    ["water"]=(EffectKind.WaterWave,Only(EffectKind.WaterWave,new() {WaterWave=new() {Enabled=true,Amplitude=20,Strength=.8}})),
    ["pixel"]=(EffectKind.Pixelate,Only(EffectKind.Pixelate,new() {Pixelate=new() {Enabled=true,BlockSize=45}})),
    ["zoom"]=(EffectKind.ZoomTunnel,Only(EffectKind.ZoomTunnel,new() {ZoomTunnel=new() {Enabled=true,Strength=.8}})),
    ["edge"]=(EffectKind.EdgeOutline,Only(EffectKind.EdgeOutline,new() {EdgeOutline=new() {Enabled=true,Strength=.8,Color="#44DDFF"}})),
    ["sun"]=(EffectKind.SunPrism,Only(EffectKind.SunPrism,new() {SunPrism=new() {Enabled=true,Strength=.8}})),
    ["distortion"]=(EffectKind.Distortion,Only(EffectKind.Distortion,new() {Distortion=true,DistortionStrength=.7})),
    ["dots"]=(EffectKind.Dots,Only(EffectKind.Dots,new() {Dots=true,DotGlow=true,DotStrength=.7})),
    ["ripple"]=(EffectKind.DotRipple,Only(EffectKind.DotRipple,new() {DotRipple=true,RippleDotStrength=.8})),
    ["glitch"]=(EffectKind.Glitch,Only(EffectKind.Glitch,new() {Glitch=true,GlitchStrength=.8,GlitchCoverage=.8})),
    ["large"]=(EffectKind.LargeGlitch,Only(EffectKind.LargeGlitch,new() {LargeGlitch=true,LargeGlitchStrength=.8,LargeGlitchBlend=.8,LargeGlitchInterval=1,LargeGlitchDuration=.9})),
    ["rain"]=(EffectKind.Particles,Only(EffectKind.Particles,new() {Particles=new() {Enabled=true,Style=5},RainRipples=new() {Strength=.8,Outline=true}}))
};
foreach(var (name,item) in singles) {
    AddLegacy(name+"-legacy","color",item.Fx);
    Add(name+"-stack","color",Stack(item.Fx,item.Kind));
}
var pc=particles with {Collection=new() {Added=EffectKind.Particles|EffectKind.Color},Saturation=0};
Add("color-particles","black",Stack(pc,EffectKind.Color,EffectKind.Particles));
Add("particles-color","black",Stack(pc,EffectKind.Particles,EffectKind.Color));
var pb=particles with {Collection=new() {Added=EffectKind.Particles|EffectKind.Blur},Blur=true,BlurRadius=28,BlurStrength=1};
Add("blur-particles","black",Stack(pb,EffectKind.Blur,EffectKind.Particles));
Add("particles-blur","black",Stack(pb,EffectKind.Particles,EffectKind.Blur));
var pg=particles with {Collection=new() {Added=EffectKind.Particles|EffectKind.Glow},Glow=true,GlowStrength=1.4,GlowThreshold=.1};
Add("glow-particles","black",Stack(pg,EffectKind.Glow,EffectKind.Particles));
Add("particles-glow","black",Stack(pg,EffectKind.Particles,EffectKind.Glow));
Add("rotated-color-particles","rotated-black",Stack(pc,EffectKind.Color,EffectKind.Particles));
Add("rotated-particles-color","rotated-black",Stack(pc,EffectKind.Particles,EffectKind.Color));
Add("rotated-blur-particles","rotated-black",Stack(pb,EffectKind.Blur,EffectKind.Particles));
Add("rotated-particles-blur","rotated-black",Stack(pb,EffectKind.Particles,EffectKind.Blur));
Add("rotated-glow-particles","rotated-black",Stack(pg,EffectKind.Glow,EffectKind.Particles));
Add("rotated-particles-glow","rotated-black",Stack(pg,EffectKind.Particles,EffectKind.Glow));
var full=Stack(pb,EffectKind.Particles,EffectKind.Blur);
Add("intensity-full","color",full);
Add("intensity-half","color",full with {Intensity=.5});
Add("intensity-zero","color",full with {Intensity=0});
Add("disabled-particles","color",full with {Particles=particle with {Enabled=false},Blur=false});
var left=new EffectMask {Id="left",Name="Left",X=.25,Y=.5,Width=.5,Height=2,Feather=0};
Add("mask-library-only","color",new() {Masks=[left],Collection=new() {Added=EffectKind.MaskLibrary}});
Add("mask-library-preview","color",new() {Masks=[left],MaskPreviewId=left.Id,Intensity=0,Collection=new() {Added=EffectKind.MaskLibrary}});
Add("masked-color","color",Stack(Only(EffectKind.Color,new() {Saturation=0,Masks=[left],EffectMasks=new() {[EffectKind.Color]=left.Id}}),EffectKind.Color));
var masked=pc with {Masks=[left],EffectMasks=new() {[EffectKind.Color]=left.Id}};
Add("particles-masked-color","black",Stack(masked,EffectKind.Particles,EffectKind.Color));
Add("mask-preview","color",Stack(masked with {MaskPreviewId=left.Id},EffectKind.Particles,EffectKind.Color));
var fused=new VideoEffects {Collection=new() {Added=EffectKind.Color|EffectKind.Vignette|EffectKind.Scanlines|EffectKind.Noise},Brightness=.1,Saturation=.8,Vignette=true,VignetteStrength=.4,Scanlines=true,ScanlineStrength=.15,Noise=true,NoiseStrength=.05};
AddLegacy("pointwise-legacy","color",fused);
Add("pointwise-default","color",fused);
Add("pointwise-forward","color",Stack(fused,EffectKind.Color,EffectKind.Vignette,EffectKind.Scanlines,EffectKind.Noise));
Add("pointwise-reverse","color",Stack(fused,EffectKind.Noise,EffectKind.Scanlines,EffectKind.Vignette,EffectKind.Color));
Add("ordered-disabled","color",Stack(fused with {Collection=fused.GetCollection() with {Added=0}},EffectKind.Noise,EffectKind.Color));
var sub=particles with {Particles=particle with {Opacity=0,SubLayer=new() {Enabled=true,Opacity=.9,Count=100,Size=45,Speed=0,Sway=0,Twinkle=0,Fade=false,ColorEnabled=true,Color="#10FF30"}},Collection=new() {Added=EffectKind.Particles|EffectKind.Color},Saturation=0};
Add("subparticles-color","black",Stack(sub,EffectKind.Particles,EffectKind.Color));
// Non-square source and rotation exercise normalization before every layer.
Add("portrait-original","portrait",neutral);
Add("portrait-neutral","portrait",Stack(neutral,EffectKind.Color));
AddLegacy("portrait-color-legacy","portrait",color);
Add("portrait-color-stack","portrait",Stack(color,EffectKind.Color));
Add("rotated-original","rotated",neutral);
Add("rotated-neutral","rotated",Stack(neutral,EffectKind.Color));
AddLegacy("rotated-color-legacy","rotated",color);
Add("rotated-color-stack","rotated",Stack(color,EffectKind.Color));
Add("wide-original","wide",neutral);
Add("wide-neutral","wide",Stack(neutral,EffectKind.Color));
AddLegacy("wide-color-legacy","wide",color);
Add("wide-color-stack","wide",Stack(color,EffectKind.Color));
var audioZoom=Stack(Only(EffectKind.AudioReactive,new() {AudioReactive=true,AudioLegacy=true,AudioZoom=true,AudioZoomAmount=.35,AudioAmount=1}),EffectKind.Color);
Add("audiozoom-full","color",audioZoom);
Add("audiozoom-half","color",audioZoom with {Intensity=.5});
Add("audiozoom-zero","color",audioZoom with {Intensity=0});
File.WriteAllLines(Path.Combine(folder,"fixtures.tsv"),fixtures,new UTF8Encoding(false));
Console.WriteLine($"Wrote {fixtures.Count} rendering fixtures to {folder}");
