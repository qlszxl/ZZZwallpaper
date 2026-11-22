using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public enum DotShape { Circle, Square, Diamond, Triangle, Hexagon, Star, Cross, Ring }

public sealed partial record VideoEffects
{
    public int Version {get;init;}=40;
    public EffectCollection? Collection {get;init;}
    public double Intensity {get;init;}=1;
    public bool Scanlines {get;init;}
    public double ScanlineStrength {get;init;}=.12;
    public double ScanlineDensity {get;init;}=170;
    public bool RgbSplit {get;init;}
    public double RgbOffset {get;init;}=1.2;
    // Historical fields only; ignored on input and omitted from new JSON.
    [JsonIgnore] public bool Softening {get=>false;init {}}
    [JsonIgnore] public double SoftRadius {get=>0;init {}}
    public double Brightness {get;init;}
    public double Contrast {get;init;}=1;
    public double Saturation {get;init;}=1;
    public bool Vignette {get;init;}
    public double VignetteStrength {get;init;}=.1;
    public bool Glow {get;init;}
    public double GlowStrength {get;init;}=.15;
    public double GlowThreshold {get;init;}=.75;
    public bool Noise {get;init;}
    public double NoiseStrength {get;init;}=0;
    public bool ScanlineMotion {get;init;}
    public double ScanlineSpeed {get;init;}=.8;
    public bool Glitch {get;init;}
    public double GlitchStrength {get;init;}=.3;
    public double GlitchRate {get;init;}=6;
    public double GlitchCoverage {get;init;}=.2;
    public bool LargeGlitch {get;init;}
    public bool LargeGlitchRandomize {get;init;}
    public double LargeGlitchVariation {get;init;}=.2;
    public int LargeGlitchMode {get;init;}=3;
    public double LargeGlitchArea {get;init;}=.35;
    public double LargeGlitchStrength {get;init;}=.45;
    public double LargeGlitchRate {get;init;}=14;
    public double LargeGlitchBlend {get;init;}=.7;
    public double LargeGlitchRgbOffset {get;init;}=28;
    public double LargeGlitchInterval {get;init;}=4;
    public double LargeGlitchDuration {get;init;}=.25;
    public int LargeGlitchSmallCount {get;init;}=6;
    public double LargeGlitchSmallStrength {get;init;}=.55;
    public bool GlitchColorShift {get;init;}
    public double GlitchHueShift {get;init;}=45;
    public bool GlitchRandomHue {get;init;}
    public bool LargeGlitchColorShift {get;init;}
    public double LargeGlitchHueShift {get;init;}=45;
    public bool LargeGlitchRandomHue {get;init;}
    public DotShape DotShape {get;init;}
    public double DotRotation {get;init;}
    public DotShape RippleDotShape {get;init;}
    public double RippleDotRotation {get;init;}
    public bool Dots {get;init;}
    public double DotStrength {get;init;}=1;
    public double DotSpacing {get;init;}=14;
    public double DotSize {get;init;}=.8;
    public bool DotGlow {get;init;}
    public double DotGlowStrength {get;init;}=.4;
    public bool DotMotion {get;init;}
    public double DotSpeed {get;init;}=.3;
    public double DotPulse {get;init;}=.35;
    public bool GlitchRgbSplit {get;init;}=true;
    public double GlitchRgbOffset {get;init;}=24;
    public bool GlitchCluster {get;init;}
    public GlitchLayer Layer1 {get;init;}=new() {Mode=0,Strength=.35,Rate=3,Coverage=.8,Size=14,RgbOffset=14};
    public GlitchLayer Layer2 {get;init;}=new() {Mode=1,Strength=.22,Rate=5,Coverage=.8,Size=6,RgbOffset=8};
    public GlitchLayer Layer3 {get;init;}=new() {Mode=2,Strength=.16,Rate=11,Coverage=.6,Size=3,RgbOffset=5};
    public GlitchLayer Layer4 {get;init;}=new() {Mode=3,Strength=.25,Rate=2,Coverage=.75,Size=10,RgbOffset=12};
    public GlitchLayer Layer5 {get;init;}=new() {Mode=1,Strength=.18,Rate=7,Coverage=.65,Size=8,RgbOffset=7};
    public GlitchLayer Layer6 {get;init;}=new() {Mode=0,Strength=.2,Rate=9,Coverage=.7,Size=5,RgbOffset=8};
    [JsonIgnore] public GlitchLayer[] Layers => [Layer1,Layer2,Layer3,Layer4,Layer5,Layer6];
    public bool DotRipple {get;init;}
    public double DotRippleStrength {get;init;}=1;
    public double DotRippleSpeed {get;init;}=.35;
    public double DotRippleWidth {get;init;}=.12;
    public int DotRippleCount {get;init;}=2;
    public double RippleDotStrength {get;init;}=.85;
    public double RippleDotSpacing {get;init;}=14;
    public double RippleDotSize {get;init;}=.8;
    public bool RippleDotGlow {get;init;}=true;
    public double ClusterBlend {get;init;}=.35;
    public double ClusterSize {get;init;}=.24;
    public int ClusterSmallCount {get;init;}=3;
    public bool ScanlineBends {get;init;}
    public double ScanBendAmplitude {get;init;}=18;
    public double ScanBendSpan {get;init;}=240;
    public double ScanBendDensity {get;init;}=.4;
    public bool Blur {get;init;}
    public double BlurRadius {get;init;}=8;
    public double BlurStrength {get;init;}=1;
    public bool BlurMotion {get;init;}
    public int BlurMotionMode {get;init;}
    public double BlurMotionSpeed {get;init;}=.08;
    public double BlurCoverage {get;init;}=.45;
    public double BlurScale {get;init;}=2;
    public double BlurFeather {get;init;}=.45;
    public double BlurAngle {get;init;}=26;
    public bool Distortion {get;init;}
    public int DistortionMode {get;init;}
    public double DistortionStrength {get;init;}=.2;
    public double DistortionScale {get;init;}=4;
    public bool DistortionMotion {get;init;}
    public double DistortionSpeed {get;init;}=.25;
    public double DistortionCenterX {get;init;}=.5;
    public double DistortionCenterY {get;init;}=.5;
    public double ScanlineWidth {get;init;}=3.18;
    public double ScanlineSoftness {get;init;}=.6;
    public double ScanlineAngle {get;init;}
    public bool AudioReactive {get;init;}
    public int AudioSource {get;init;}
    public int AudioBand {get;init;}
    public double AudioSensitivity {get;init;}=1.5;
    public double AudioSmoothing {get;init;}=.18;
    public double AudioBeatThreshold {get;init;}=.35;
    public double AudioAmount {get;init;}=.75;
    [JsonIgnore] public bool AudioScanlines {get=>false;init {}}
    [JsonIgnore] public bool AudioGlitch {get=>false;init {}}
    [JsonIgnore] public bool AudioDots {get=>false;init {}}
    [JsonIgnore] public bool AudioBlur {get=>false;init {}}
    [JsonIgnore] public bool AudioDistortion {get=>false;init {}}
    [JsonIgnore] public bool AudioGlow {get=>false;init {}}
    [JsonIgnore] public bool AudioNoise {get=>false;init {}}
    public static VideoEffects Original => new();
    [JsonIgnore] public bool AudioZoom {get=>false;init {}}
    public double AudioZoomAmount {get;init;}=.08;
    [JsonIgnore] public TaskbarStyle? TaskbarAppearance => (GetCollection().Added&EffectKind.HideTaskbar)!=0&&GetCollection().HideTaskbarEnabled&&GetCollection().HideTaskbar>0?GetCollection().TaskbarStyle:null;
    [JsonIgnore] public bool HidesTaskbar => TaskbarAppearance==TaskbarStyle.Hidden;
    [JsonIgnore] public bool IsNeutral => ForRendering().IsRenderNeutral;
    [JsonIgnore] private bool IsRenderNeutral => Intensity==0||(!PackageEffects.Any(p=>p.Enabled&&p.Strength>0)&&!(AudioReactive&&AudioAmount>0&&AudioRules.Any(r=>r.Enabled))&&((!Scanlines||ScanlineStrength==0)&&(!RgbSplit||RgbOffset==0)&&(!Softening||SoftRadius==0)&&Brightness==0&&Contrast==1&&Saturation==1&&(!Vignette||VignetteStrength==0)&&(!Glow||GlowStrength==0)&&(!Noise||NoiseStrength==0)&&(!Glitch||(GlitchCluster?(ClusterBlend==0||!Layers.Take(ClusterSmallCount+1).Any(layer=>layer is {Active:true})):(GlitchStrength==0||GlitchCoverage==0)))&&(!LargeGlitch||LargeGlitchStrength==0||LargeGlitchBlend==0)&&(!Dots||DotStrength==0)&&(!DotRipple||RippleDotStrength==0)&&(!Blur||BlurRadius==0||BlurStrength==0||(BlurMotion&&BlurCoverage==0))&&(!Distortion||DistortionStrength==0)&&(!AudioReactive||!AudioLegacy||!AudioZoom||AudioZoomAmount==0||AudioAmount==0)&&!ExtendedActive));
    public VideoEffects Validate()
    {
        ValidatePackageEffects();
        if(Version!=40)throw new ArgumentException("不支持的效果参数版本");
        static void Range(double value,double min,double max) {if(!double.IsFinite(value)||value<min||value>max)throw new ArgumentException("效果参数超出范围");}

        if(Particles==null)throw new ArgumentException("缺少粒子设置");Particles.Validate();
        Collection?.Validate();ValidateExtended();ValidateAdvanced();ValidateAudioRules();
        if(!Enum.IsDefined(DotShape)||!Enum.IsDefined(RippleDotShape))throw new ArgumentException("波点形状无效");
        Range(DotRotation,0,360);Range(RippleDotRotation,0,360);
        Range(Intensity,0,1);Range(ScanlineStrength,0,.35);Range(ScanlineDensity,1,260);Range(RgbOffset,0,16384);Range(SoftRadius,0,100);
        Range(Brightness,-.5,.5);Range(Contrast,.5,1.5);Range(Saturation,0,2);Range(VignetteStrength,0,1);Range(GlowStrength,0,3);Range(GlowThreshold,0,1);Range(NoiseStrength,0,.3);
        Range(ScanlineSpeed,-60,60);Range(GlitchStrength,0,1);Range(GlitchRate,0,20);Range(GlitchCoverage,0,1);
        Range(LargeGlitchVariation,0,1);Range(LargeGlitchMode,0,3);Range(LargeGlitchArea,.2,.6);Range(LargeGlitchStrength,0,1);Range(LargeGlitchRate,0,20);Range(LargeGlitchBlend,0,1);Range(LargeGlitchRgbOffset,0,80);
        Range(LargeGlitchInterval,1,20);Range(LargeGlitchDuration,.05,.9);Range(LargeGlitchSmallCount,0,10);Range(LargeGlitchSmallStrength,0,1);Range(GlitchHueShift,-180,180);Range(LargeGlitchHueShift,-180,180);
        Range(DotStrength,0,1);Range(DotSpacing,4,40);Range(DotSize,.1,1);Range(DotGlowStrength,0,1);Range(DotSpeed,-2,2);Range(DotPulse,0,1);
        Range(GlitchRgbOffset,0,80);Range(DotRippleStrength,0,2);Range(DotRippleSpeed,0,4);Range(DotRippleWidth,.02,.5);Range(DotRippleCount,1,6);
        Range(RippleDotStrength,0,1);Range(RippleDotSpacing,4,40);Range(RippleDotSize,.1,1);
        Range(ClusterBlend,0,1);Range(ClusterSize,.08,.4);Range(ClusterSmallCount,0,5);
        Range(ScanBendAmplitude,0,80);Range(ScanBendSpan,40,800);Range(ScanBendDensity,0,1);
        Range(BlurRadius,0,100);Range(BlurStrength,0,1);Range(DistortionMode,0,2);Range(DistortionStrength,-1,1);Range(DistortionScale,1,20);
        Range(BlurMotionMode,0,2);Range(BlurMotionSpeed,-2,2);Range(BlurCoverage,0,1);Range(BlurScale,.5,8);Range(BlurFeather,0,1);Range(BlurAngle,-180,180);
        Range(DistortionSpeed,-4,4);Range(DistortionCenterX,0,1);Range(DistortionCenterY,0,1);Range(ScanlineWidth,.25,12);Range(ScanlineSoftness,0,1);Range(ScanlineAngle,-90,90);
        Range(AudioSource,0,1);Range(AudioBand,0,3);Range(AudioSensitivity,.25,5);Range(AudioSmoothing,.02,2);Range(AudioBeatThreshold,.05,.95);Range(AudioAmount,0,1);Range(AudioZoomAmount,0,.5);
        foreach(var layer in Layers) {if(layer is null)throw new ArgumentException("缺少故障集群参数");layer.Validate();}
        return this;
    }
    public EffectCollection GetCollection()
    {
        if(Collection!=null)return Collection;
        EffectKind added=0;
        if(Brightness!=0||Contrast!=1||Saturation!=1)added|=EffectKind.Color;
        if(RgbSplit)added|=EffectKind.RgbSplit;
        if(Softening)added|=EffectKind.Softening;
        if(Glow)added|=EffectKind.Glow;
        if(Vignette)added|=EffectKind.Vignette;
        if(Noise)added|=EffectKind.Noise;
        if(Scanlines)added|=EffectKind.Scanlines;
        if(Glitch)added|=EffectKind.Glitch;
        if(LargeGlitch)added|=EffectKind.LargeGlitch;
        if(Dots)added|=EffectKind.Dots;
        if(DotRipple)added|=EffectKind.DotRipple;
        if(Blur)added|=EffectKind.Blur;
        if(Distortion)added|=EffectKind.Distortion;
        if(AudioReactive)added|=EffectKind.AudioReactive;
        if(Masks.Length>0)added|=EffectKind.MaskLibrary;
        if(ZoomTunnel.Enabled)added|=EffectKind.ZoomTunnel;
        if(Particles.Enabled)added|=EffectKind.Particles;
        if(WaterWave.Enabled)added|=EffectKind.WaterWave;
        if(EdgeOutline.Enabled)added|=EffectKind.EdgeOutline;
        if(Pixelate.Enabled)added|=EffectKind.Pixelate;
        if(SunPrism.Enabled)added|=EffectKind.SunPrism;
        if(LightFlare.Enabled)added|=EffectKind.LightFlare;
        if(Transform.Enabled)added|=EffectKind.Transform;

        return new() {Added=added};
    }
    // Persist the unscaled parameter values; lower only the rendering copy into
    // the versioned host protocol. Raising a strength restores details.
    public VideoEffects ForRendering()
    {
        Validate();var collection=GetCollection();
        double Amount(EffectKind kind)=>(collection.Added&kind)!=0?collection.Strength(kind):0;
        double color=collection.ColorEnabled?Amount(EffectKind.Color):0;
        double rgb=Amount(EffectKind.RgbSplit),glow=Amount(EffectKind.Glow),vignette=Amount(EffectKind.Vignette),noise=Amount(EffectKind.Noise);
        double scan=Amount(EffectKind.Scanlines),glitch=Amount(EffectKind.Glitch),large=Amount(EffectKind.LargeGlitch),dots=Amount(EffectKind.Dots),ripple=Amount(EffectKind.DotRipple);
        double blur=Amount(EffectKind.Blur),distortion=Amount(EffectKind.Distortion),audio=Amount(EffectKind.AudioReactive);
        return this with {
            ZoomTunnel=ZoomTunnel with {Enabled=ZoomTunnel.Enabled&&Amount(EffectKind.ZoomTunnel)>0,Strength=ZoomTunnel.Strength*Amount(EffectKind.ZoomTunnel)},
            Particles=Particles with {Enabled=Particles.Enabled&&Amount(EffectKind.Particles)>0,Opacity=Particles.Opacity*Amount(EffectKind.Particles),SubLayer=Particles.SubLayer with {Opacity=Particles.SubLayer.Opacity*Amount(EffectKind.Particles)}},
            RainRipples=RainRipples with {Enabled=Particles.Enabled&&Particles.IsRain&&Amount(EffectKind.Particles)>0,Strength=RainRipples.Strength*Amount(EffectKind.Particles),OutlineStrength=RainRipples.OutlineStrength*Amount(EffectKind.Particles)},
            WaterWave=WaterWave with {Enabled=WaterWave.Enabled&&Amount(EffectKind.WaterWave)>0,Strength=WaterWave.Strength*Amount(EffectKind.WaterWave)},
            EdgeOutline=EdgeOutline with {Enabled=EdgeOutline.Enabled&&Amount(EffectKind.EdgeOutline)>0,Strength=EdgeOutline.Strength*Amount(EffectKind.EdgeOutline)},
            Pixelate=Pixelate with {Enabled=Pixelate.Enabled&&Amount(EffectKind.Pixelate)>0,Strength=Pixelate.Strength*Amount(EffectKind.Pixelate)},
            SunPrism=SunPrism with {Enabled=SunPrism.Enabled&&Amount(EffectKind.SunPrism)>0,Strength=SunPrism.Strength*Amount(EffectKind.SunPrism)},

            LightFlare=LightFlare with {Enabled=LightFlare.Enabled&&Amount(EffectKind.LightFlare)>0,Strength=LightFlare.Strength*Amount(EffectKind.LightFlare)},
            Transform=Transform with {Enabled=Transform.Enabled&&Amount(EffectKind.Transform)>0,Strength=Transform.Strength*Amount(EffectKind.Transform)},
            Collection=null,Brightness=Brightness*color,Contrast=1+(Contrast-1)*color,Saturation=1+(Saturation-1)*color,
            RgbSplit=RgbSplit&&rgb>0,RgbOffset=RgbOffset*rgb,Softening=false,SoftRadius=0,
            Glow=Glow&&glow>0,GlowStrength=GlowStrength*glow,Vignette=Vignette&&vignette>0,VignetteStrength=VignetteStrength*vignette,Noise=Noise&&noise>0,NoiseStrength=NoiseStrength*noise,
            Scanlines=Scanlines&&scan>0,ScanlineStrength=ScanlineStrength*scan,
            Glitch=Glitch&&glitch>0,GlitchStrength=GlitchStrength*glitch,ClusterBlend=ClusterBlend*glitch,
            LargeGlitch=LargeGlitch&&large>0,LargeGlitchBlend=LargeGlitchBlend*large,
            Dots=Dots&&dots>0,DotStrength=DotStrength*dots,DotRipple=DotRipple&&ripple>0,RippleDotStrength=RippleDotStrength*ripple,
            Blur=Blur&&blur>0,BlurStrength=BlurStrength*blur,Distortion=Distortion&&distortion>0,DistortionStrength=DistortionStrength*distortion,
            AudioReactive=AudioReactive&&audio>0,AudioAmount=AudioAmount*audio,AudioRules=AudioParameters.ForRendering(this)
        };
    }
    public string ToWire()
    {
        var collection=GetCollection();var rendering=ForRendering();var legacy=rendering.ToRenderWire();
        // Wire 37 appends audio rule behavior; earlier effect slots are unchanged.
        var package=rendering.PackageWire();
        return (package.Length==0?"37":"38")+legacy[2..]+" "+(EffectOrder.Default.Count+2).ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+
            string.Join(" ",collection.OrderedKinds().Select(kind=>System.Numerics.BitOperations.TrailingZeroCount((uint)kind).ToString(System.Globalization.CultureInfo.InvariantCulture)))+" 2 22"+RetiredScanCardsWire()+" "+BlurSamples.ToString(System.Globalization.CultureInfo.InvariantCulture)+rendering.LightFlareWire()+rendering.TransformWire()+rendering.AudioRuleBehaviorWire()+package;
    }
    private string ToRenderWire()
    {
        Validate();
        return FormattableString.Invariant($"25 {Intensity:F6} {(Scanlines?1:0)} {ScanlineStrength:F6} {ScanlineDensity:F6} {(RgbSplit?1:0)} {RgbOffset:F6} {(Softening?1:0)} {SoftRadius:F6} {Brightness:F6} {Contrast:F6} {Saturation:F6} {(Vignette?1:0)} {VignetteStrength:F6} {(Glow?1:0)} {GlowStrength:F6} {GlowThreshold:F6} {(Noise?1:0)} {NoiseStrength:F6} {(ScanlineMotion?1:0)} {ScanlineSpeed:F6} {(Glitch?1:0)} {GlitchStrength:F6} {GlitchRate:F6} {GlitchCoverage:F6} {(Dots?1:0)} {DotStrength:F6} {DotSpacing:F6} {DotSize:F6} {(DotGlow?1:0)} {DotGlowStrength:F6} {(DotMotion?1:0)} {DotSpeed:F6} {DotPulse:F6} {(GlitchRgbSplit?1:0)} {GlitchRgbOffset:F6} {(GlitchCluster?1:0)} {(DotRipple?1:0)} {DotRippleStrength:F6} {DotRippleSpeed:F6} {DotRippleWidth:F6} {DotRippleCount}")+string.Concat(Layers.Select(layer=>layer.ToWire()))+FormattableString.Invariant($" {RippleDotStrength:F6} {RippleDotSpacing:F6} {RippleDotSize:F6} {(RippleDotGlow?1:0)} {ClusterBlend:F6} {ClusterSize:F6} {ClusterSmallCount} {(ScanlineBends?1:0)} {ScanBendAmplitude:F6} {ScanBendSpan:F6} {ScanBendDensity:F6} {(Blur?1:0)} {BlurRadius:F6} {BlurStrength:F6} {(Distortion?1:0)} {DistortionMode} {DistortionStrength:F6} {DistortionScale:F6} {(DistortionMotion?1:0)} {DistortionSpeed:F6} {DistortionCenterX:F6} {DistortionCenterY:F6} {ScanlineWidth:F6} {ScanlineSoftness:F6} {ScanlineAngle:F6} {(BlurMotion?1:0)} {BlurMotionMode} {BlurMotionSpeed:F6} {BlurCoverage:F6} {BlurScale:F6} {BlurFeather:F6} {BlurAngle:F6} {(LargeGlitch?1:0)} {LargeGlitchMode} {LargeGlitchArea:F6} {LargeGlitchStrength:F6} {LargeGlitchRate:F6} {LargeGlitchBlend:F6} {LargeGlitchRgbOffset:F6} {(GlitchColorShift?1:0)} {GlitchHueShift:F6} {LargeGlitchInterval:F6} {LargeGlitchDuration:F6} {LargeGlitchSmallCount} {LargeGlitchSmallStrength:F6} {(GlitchRandomHue?1:0)} {(LargeGlitchColorShift?1:0)} {LargeGlitchHueShift:F6} {(LargeGlitchRandomHue?1:0)} {(AudioReactive?1:0)} {AudioSource} {AudioBand} {AudioSensitivity:F6} {AudioSmoothing:F6} {AudioBeatThreshold:F6} {AudioAmount:F6} {(AudioScanlines?1:0)} {(AudioGlitch?1:0)} {(AudioDots?1:0)} {(AudioBlur?1:0)} {(AudioDistortion?1:0)} {(AudioGlow?1:0)} {(AudioNoise?1:0)} {(AudioZoom?1:0)} {AudioZoomAmount:F6} {(LargeGlitchRandomize?1:0)} {LargeGlitchVariation:F6} {(int)DotShape} {DotRotation:F6} {(int)RippleDotShape} {RippleDotRotation:F6}")+ExtendedWire()+AdvancedWire()+SunAnimationWire()+SunConeWire()+" "+EffectColors.Wire(EdgeOutline.ResolvedColor)+AudioRulesWire()+ParticlesWire();
    }
    public string ToJson()
    {
        Validate();
        // Stable key order prevents identical layer maps producing false dirty
        // revisions or distinct undo snapshots solely from insertion order.
        return JsonSerializer.Serialize(Collection==null?this:this with {Collection=Collection with {Layers=EffectOrder.Clone(Collection.Layers)}});
    }
    public static VideoEffects FromJson(string? json)
    {
        bool legacy=false,migrateZoom=false;
        if(!string.IsNullOrWhiteSpace(json)) {
            var node=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            int version=node["Version"]?.GetValue<int>()??1;legacy=version<=22;migrateZoom=version<=38;
            if(legacy) {
                if(node["Collection"] is System.Text.Json.Nodes.JsonObject collection&&collection["RainRipples"] is { } amount) {collection["Particles"]=amount.DeepClone();collection.Remove("RainRipples");}
                if(node["EffectMasks"] is System.Text.Json.Nodes.JsonObject masks&&masks["RainRipples"] is { } mask) {masks["Particles"]=mask.DeepClone();masks.Remove("RainRipples");}
                json=node.ToJsonString();
            }
            // Discard the retired visual effect from every saved version.
            {
                node.Remove("ScanCards");node.Remove("Softening");node.Remove("SoftRadius");
                if(node["Collection"] is System.Text.Json.Nodes.JsonObject oldScanCollection) {
                    oldScanCollection.Remove("ScanCards");oldScanCollection.Remove("Softening");
                    if(oldScanCollection["Added"] is { } membership)oldScanCollection["Added"]=(int)((EffectKind)membership.GetValue<int>()&~EffectCollection.Retired);
                    if(oldScanCollection["Layers"] is System.Text.Json.Nodes.JsonObject layers) {layers.Remove("ScanCards");layers.Remove("4194304");layers.Remove("Softening");layers.Remove("4");}
                }
                if(node["EffectMasks"] is System.Text.Json.Nodes.JsonObject links) {links.Remove("ScanCards");links.Remove("4194304");links.Remove("Softening");links.Remove("4");}
                json=node.ToJsonString();
            }
            if(version<=28) {
                if(node["Collection"] is System.Text.Json.Nodes.JsonObject oldCollection)oldCollection.Remove("Layers");
                json=node.ToJsonString();
            }
        }
        var state=ReadVersion(json);
        if(legacy)state=state with {Particles=new ParticleOptions {Enabled=state.RainRipples.Enabled,Style=state.RainRipples.Enabled||(state.GetCollection().Added&EffectKind.Particles)!=0?5:0}};
        if(migrateZoom)state=state.MigrateAudioZoom();
        state=state.MigrateBlurMask();
        return (state with {Collection=state.GetCollection()}).Validate();
    }
    private static VideoEffects ReadVersion(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return Original;
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        int version=root.TryGetProperty("Version",out var v)?v.GetInt32():1;
        if(version is >=30 and <=40)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40}).Validate();
        if(version is 22 or 23 or 24 or 25 or 26 or 27 or 28 or 29)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40}).Validate();
        if(version is >=11 and <=21)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioLegacy=true}).Validate();
        if(version==10)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false}).Validate();
        if(version==9) {
            var old=JsonSerializer.Deserialize<VideoEffects>(json)??Original;
            if(root.TryGetProperty("LargeGlitchMode",out var oldMode)&&oldMode.GetInt32() is <0 or >2)throw new ArgumentException("旧版大故障形态超出范围");
            return (old with {Version=40,AudioReactive=false,LargeGlitchMode=3,LargeGlitchColorShift=old.GlitchColorShift,
                LargeGlitchHueShift=old.GlitchHueShift,GlitchRandomHue=false,LargeGlitchRandomHue=false}).Validate();
        }
        if(version==8) {
            // Inversion is retired. Keep the user's enabled state with a half-turn
            // in hue; unlike inversion, this preserves saturation and value.
            bool oldInvert=root.TryGetProperty("GlitchInvert",out var invert)&&invert.GetBoolean();
            var old=JsonSerializer.Deserialize<VideoEffects>(json)??Original;
            if(root.TryGetProperty("LargeGlitchMode",out var oldMode)&&oldMode.GetInt32() is <0 or >2)throw new ArgumentException("旧版大故障形态超出范围");
            return (old with {
                Version=40,AudioReactive=false,LargeGlitchMode=3,GlitchColorShift=oldInvert,GlitchHueShift=oldInvert?180:45,
                LargeGlitchColorShift=oldInvert,LargeGlitchHueShift=oldInvert?180:45,GlitchRandomHue=false,LargeGlitchRandomHue=false,
                LargeGlitchInterval=4,LargeGlitchDuration=.25,LargeGlitchSmallCount=6,LargeGlitchSmallStrength=.55
            }).Validate();
        }
        if(version==7)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false}).Validate();
        if(version==6)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false,BlurMotion=false}).Validate();
        if(version==5)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false,BlurMotion=false,Blur=false,Distortion=false}).Validate();
        if(version==4) {
            var old=JsonSerializer.Deserialize<VideoEffects>(json)??Original;
            return (old with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false,BlurMotion=false,Dots=old.Dots&&!old.DotRipple,DotRipple=old.Dots&&old.DotRipple,
                RippleDotStrength=old.DotStrength,RippleDotSpacing=old.DotSpacing,RippleDotSize=old.DotSize}).Validate();
        }
        if(version==3)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false,BlurMotion=false,GlitchCluster=false,DotRipple=false}).Validate();
        if(version==2)return ((JsonSerializer.Deserialize<VideoEffects>(json)??Original) with {Version=40,AudioReactive=false,LargeGlitch=false,GlitchColorShift=false,BlurMotion=false,ScanlineMotion=false,Glitch=false,Dots=false,DotGlow=false,DotMotion=false,GlitchCluster=false,DotRipple=false}).Validate();
        if(version!=1)throw new ArgumentException("不支持的效果参数版本");
        double Number(string key,double fallback) => root.TryGetProperty(key,out var p)?p.GetDouble():fallback;
        bool Flag(string key) => root.TryGetProperty(key,out var p)&&p.GetBoolean();
        double strength=Number("TextureStrength",.5);
        if(!double.IsFinite(strength)||strength<0||strength>1)throw new ArgumentException("旧版效果参数超出范围");
        double oldGlow=Number("GlowStrength",.3);if(!double.IsFinite(oldGlow)||oldGlow<0||oldGlow>1)throw new ArgumentException("旧版辉光参数超出范围");
        return new VideoEffects {
            Scanlines=Flag("ScreenTexture"),ScanlineStrength=.22*strength,ScanlineDensity=260,
            RgbSplit=Flag("ScreenTexture"),RgbOffset=1.5*strength,Softening=Flag("ScreenTexture"),SoftRadius=.65*strength,
            Brightness=Number("Brightness",0),Contrast=Number("Contrast",1),Saturation=Number("Saturation",1),
            Vignette=Flag("Vignette"),VignetteStrength=Number("VignetteStrength",.35),Glow=Flag("Glow"),GlowStrength=oldGlow*.5
        }.Validate();
    }
}

public sealed record WallpaperProfile(FillMode Fill,VideoEffects Effects);
