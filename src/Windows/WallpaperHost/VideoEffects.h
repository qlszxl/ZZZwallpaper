#pragma once
#include <cmath>
#include <locale>
#include <sstream>
#include <stdexcept>
#include "ExtendedEffects.h"
#include "Particles.h"
#include "AdvancedEffects.h"
#include "AudioMappingTypes.h"
#include "EffectOrder.h"
#include "LightFlare.h"
#include "TransformEffect.h"
#include "ScanCardsOptions.h" // Legacy wire payload reader only; rendering belongs to the widget.

struct GlitchLayer {
    bool enabled=true;int mode=0;
    float strength=.4f,rate=7,coverage=.2f,size=24,rgbOffset=24;
    int dotShape=0,rippleDotShape=0;float dotRotation=0,rippleDotRotation=0;
    bool Active() const {return enabled&&strength>0&&coverage>0&&(mode!=3||rgbOffset>0);}
};
struct PackageShader {
    std::string generation,file;
    float strength=1;
    int animated=0;
};
struct VideoEffects : AdvancedEffects {
    std::vector<PackageShader> packageShaders;
    // Empty for wire 2..25: preserve their original fixed composition exactly.
    std::vector<int> effectOrder;
    ZoomTunnelOptions zoomTunnel;
    RainRipplesOptions rainRipples;
    ParticleOptions particles,subParticles;
    WaterWaveOptions waterWave;
    EdgeOutlineOptions edgeOutline;
    PixelateOptions pixelate;
    SunPrismOptions sunPrism;
    LightFlareOptions lightFlare;
    TransformOptions transform;
    bool transformProtocol=false;

    bool scanlines=false,rgbSplit=false,softening=false,vignette=false,glow=false,noise=false;
    bool largeGlitchRandomize=false;
    float largeGlitchVariation=.2f;
    float intensity=1,scanlineStrength=.12f,scanlineDensity=170,rgbOffset=1.2f,softRadius=.6f;
    float brightness=0,contrast=1,saturation=1,vignetteStrength=.1f,glowStrength=.15f,glowThreshold=.75f,noiseStrength=0;
    bool scanlineMotion=false,glitch=false,dots=false,dotGlow=false,dotMotion=false;
    float scanlineSpeed=.8f,glitchStrength=.3f,glitchRate=6,glitchCoverage=.2f;
    float dotStrength=1,dotSpacing=12,dotSize=.8f,dotGlowStrength=.4f,dotSpeed=.3f,dotPulse=.35f;
    bool glitchRgbSplit=true,glitchCluster=false,dotRipple=false;
    float glitchRgbOffset=24,dotRippleStrength=1,dotRippleSpeed=.35f,dotRippleWidth=.12f;int dotRippleCount=2;
    float rippleDotStrength=.85f,rippleDotSpacing=12,rippleDotSize=.8f,clusterBlend=.35f,clusterSize=.24f;
    bool rippleDotGlow=true,scanlineBends=false;int clusterSmallCount=3;
    float scanBendAmplitude=16,scanBendSpan=240,scanBendDensity=.4f;
    bool blur=false,distortion=false,distortionMotion=false;int distortionMode=0;
    float blurRadius=8,blurStrength=1,distortionStrength=.2f,distortionScale=4,distortionSpeed=.25f,distortionCenterX=.5f,distortionCenterY=.5f;
    float scanlineWidth=3.18f,scanlineSoftness=.6f,scanlineAngle=0;
    bool blurMotion=false;int blurMotionMode=0;
    float blurMotionSpeed=.08f,blurCoverage=.45f,blurScale=2,blurFeather=.45f,blurAngle=25;
    bool largeGlitch=false,glitchColorShift=false;int largeGlitchMode=3,largeGlitchSmallCount=6;
    float largeGlitchArea=.35f,largeGlitchStrength=.45f,largeGlitchRate=12,largeGlitchBlend=.7f,largeGlitchRgbOffset=28;
    float largeGlitchInterval=4,largeGlitchDuration=.25f,largeGlitchSmallStrength=.55f,glitchHueShift=45;
    bool glitchRandomHue=false,largeGlitchColorShift=false,largeGlitchRandomHue=false;float largeGlitchHueShift=45;
    bool audioZoom=false;float audioZoomAmount=.08f;
    bool audioReactive=false;int audioSource=0,audioBand=0;
    float audioSensitivity=1.5f,audioSmoothing=.18f,audioBeatThreshold=.35f,audioAmount=.75f;
    bool audioScanlines=true,audioGlitch=true,audioDots=true,audioBlur=true,audioDistortion=true,audioGlow=true,audioNoise=false;
    bool audioLegacy=true;double audioAttack=.018;std::vector<AudioRule> audioRules;
    bool HasAudioRules() const {for(const auto& r:audioRules)if(r.enabled)return true;return false;}
    GlitchLayer layers[6];
    int dotShape=0,rippleDotShape=0;float dotRotation=0,rippleDotRotation=0;
    bool Active() const {return (intensity>0&&!packageShaders.empty())||BaseActive();}
    bool BaseActive() const {bool cluster=false;if(glitchCluster&&clusterBlend>0)for(int i=0;i<=clusterSmallCount;++i)cluster|=layers[i].Active();return maskPreview>=0||(intensity>0&&((audioReactive&&audioAmount>0&&HasAudioRules())||(particles.enabled&&particles.opacity>0)||(subParticles.enabled&&subParticles.opacity>0)||(zoomTunnel.enabled&&zoomTunnel.strength>0)||(rainRipples.enabled&&(rainRipples.strength>0||(rainOutline&&rainOutlineStrength>0)))||(waterWave.enabled&&waterWave.strength>0&&waterWave.amplitude>0)||(edgeOutline.enabled&&edgeOutline.strength>0)||(pixelate.enabled&&pixelate.strength>0)||transform.Active()||lightFlare.Active()||(sunPrism.enabled&&sunPrism.strength>0)||(audioReactive&&audioLegacy&&audioZoom&&(!transformProtocol||(transform.enabled&&transform.strength>0))&&audioZoomAmount>0&&audioAmount>0)||(scanlines&&scanlineStrength>0)||(rgbSplit&&rgbOffset>0)||(softening&&softRadius>0)||brightness!=0||contrast!=1||saturation!=1||(vignette&&vignetteStrength>0)||(glow&&glowStrength>0)||(noise&&noiseStrength>0)||(glitch&&(glitchCluster?cluster:(glitchStrength>0&&glitchCoverage>0)))||(largeGlitch&&largeGlitchStrength>0&&largeGlitchBlend>0)||(dots&&dotStrength>0)||(dotRipple&&rippleDotStrength>0)||(blur&&blurRadius>0&&blurStrength>0&&(!blurMotion||blurCoverage>0))||(distortion&&distortionStrength!=0)));}
    bool NeedsAnimation() const {
        if(!Active())return false;
        for(const auto& shader:packageShaders)if(shader.animated)return true;
        for(const auto& mask:masks)if(mask.shape>=6&&mask.speed!=0)return true;
        if(maskPreview>=0||intensity<=0)return false;
        return lightFlare.Animated()||(audioReactive&&audioAmount>0&&(audioLegacy||HasAudioRules()))||
            (particles.enabled&&particles.opacity>0)||(subParticles.enabled&&subParticles.opacity>0)||
            (noise&&noiseStrength>0)||(scanlines&&scanlineMotion&&scanlineSpeed!=0)||
            glitch||largeGlitch||(dots&&dotMotion)||(dotRipple&&(dotRippleSpeed!=0||rippleColorMode>=2))||
            (blur&&blurMotion&&blurMotionSpeed!=0)||(distortion&&distortionMotion&&distortionSpeed!=0)||
            zoomTunnel.enabled||(rainRipples.enabled&&rainRipples.speed!=0)||
            (waterWave.enabled&&waterWave.speed!=0)||(sunPrism.enabled&&(sunPrism.speed!=0||sunRotationSpeed!=0||
                (sunMoveX!=0&&sunMoveSpeedX!=0)||(sunMoveY!=0&&sunMoveSpeedY!=0)));
    }
    static VideoEffects Parse(const std::string& text) {
        VideoEffects fx;int version,scan,rgb,soft,vignette,glow,noise,motion=0,glitch=0,dots=0,dotGlow=0,dotMotion=0,glitchRgb=1,cluster=0,ripple=0,rippleGlow=1,bends=0,blur=0,distort=0,distortMotion=0;std::string extra;
        std::istringstream in(text);in.imbue(std::locale::classic());
        if(!(in>>version>>fx.intensity>>scan>>fx.scanlineStrength>>fx.scanlineDensity>>rgb>>fx.rgbOffset>>soft>>fx.softRadius>>fx.brightness>>fx.contrast>>fx.saturation>>vignette>>fx.vignetteStrength>>glow>>fx.glowStrength>>fx.glowThreshold>>noise>>fx.noiseStrength)||(version<2||version>38))
            throw std::runtime_error("Invalid effects protocol or version");
        if(version>=3&&!(in>>motion>>fx.scanlineSpeed>>glitch>>fx.glitchStrength>>fx.glitchRate>>fx.glitchCoverage>>dots>>fx.dotStrength>>fx.dotSpacing>>fx.dotSize>>dotGlow>>fx.dotGlowStrength>>dotMotion>>fx.dotSpeed>>fx.dotPulse))throw std::runtime_error("Incomplete dynamic effects parameters");
        if(version>=4) {
            if(!(in>>glitchRgb>>fx.glitchRgbOffset>>cluster>>ripple>>fx.dotRippleStrength>>fx.dotRippleSpeed>>fx.dotRippleWidth>>fx.dotRippleCount))throw std::runtime_error("Incomplete cluster/ripple parameters");
            for(auto& layer:fx.layers) {int enabled;if(!(in>>enabled>>layer.mode>>layer.strength>>layer.rate>>layer.coverage>>layer.size>>layer.rgbOffset)||enabled<0||enabled>1)throw std::runtime_error("Invalid glitch layer");layer.enabled=enabled!=0;}
        } else fx.glitchRgbOffset=10;
        if(version>=5) {
            if(!(in>>fx.rippleDotStrength>>fx.rippleDotSpacing>>fx.rippleDotSize>>rippleGlow>>fx.clusterBlend>>fx.clusterSize>>fx.clusterSmallCount>>bends>>fx.scanBendAmplitude>>fx.scanBendSpan>>fx.scanBendDensity))throw std::runtime_error("Incomplete localized effects parameters");
        } else if(version==4) {
            fx.rippleDotStrength=fx.dotStrength;fx.rippleDotSpacing=fx.dotSpacing;fx.rippleDotSize=fx.dotSize;
            ripple=ripple&&dots;if(ripple)dots=0;
        }
        if(version>=6&&!(in>>blur>>fx.blurRadius>>fx.blurStrength>>distort>>fx.distortionMode>>fx.distortionStrength>>fx.distortionScale>>distortMotion>>fx.distortionSpeed>>fx.distortionCenterX>>fx.distortionCenterY>>fx.scanlineWidth>>fx.scanlineSoftness>>fx.scanlineAngle))throw std::runtime_error("Incomplete blur/distortion/scan width parameters");
        int blurMotion=0;
        if(version>=7&&!(in>>blurMotion>>fx.blurMotionMode>>fx.blurMotionSpeed>>fx.blurCoverage>>fx.blurScale>>fx.blurFeather>>fx.blurAngle))throw std::runtime_error("Incomplete moving blur parameters");
        if(blurMotion<0||blurMotion>1)throw std::runtime_error("Invalid moving blur switch");fx.blurMotion=blurMotion!=0;
        int largeGlitch=0,glitchColor=0;
        if(version>=8&&!(in>>largeGlitch>>fx.largeGlitchMode>>fx.largeGlitchArea>>fx.largeGlitchStrength>>fx.largeGlitchRate>>fx.largeGlitchBlend>>fx.largeGlitchRgbOffset>>glitchColor))throw std::runtime_error("Incomplete large glitch parameters");
        if(largeGlitch<0||largeGlitch>1||glitchColor<0||glitchColor>1)throw std::runtime_error("Invalid large glitch/color switch");
        fx.largeGlitch=largeGlitch!=0;fx.glitchColorShift=glitchColor!=0;
        // v8's last flag used to enable inversion; migrate it to a half-turn hue shift.
        if(version==8&&fx.glitchColorShift)fx.glitchHueShift=180;
        if(version>=9&&!(in>>fx.glitchHueShift>>fx.largeGlitchInterval>>fx.largeGlitchDuration>>fx.largeGlitchSmallCount>>fx.largeGlitchSmallStrength))throw std::runtime_error("Incomplete glitch burst/color parameters");
        if(version>=10) {
            int randomSmall=0,largeColor=0,randomLarge=0;
            if(!(in>>randomSmall>>largeColor>>fx.largeGlitchHueShift>>randomLarge))throw std::runtime_error("Incomplete independent glitch color parameters");
            for(int flag:{randomSmall,largeColor,randomLarge})if(flag<0||flag>1)throw std::runtime_error("Invalid glitch color switch");
            fx.glitchRandomHue=randomSmall!=0;fx.largeGlitchColorShift=largeColor!=0;fx.largeGlitchRandomHue=randomLarge!=0;
        } else {
            if(version>=8&&(fx.largeGlitchMode<0||fx.largeGlitchMode>2))throw std::runtime_error("Invalid legacy large glitch mode");
            fx.largeGlitchColorShift=fx.glitchColorShift;fx.largeGlitchHueShift=fx.glitchHueShift;
            if(version>=8)fx.largeGlitchMode=3;
        }
        if(version>=11) {
            int audio=0,scanAudio=0,glitchAudio=0,dotAudio=0,blurAudio=0,distortAudio=0,glowAudio=0,noiseAudio=0;
            if(!(in>>audio>>fx.audioSource>>fx.audioBand>>fx.audioSensitivity>>fx.audioSmoothing>>fx.audioBeatThreshold>>fx.audioAmount
                >>scanAudio>>glitchAudio>>dotAudio>>blurAudio>>distortAudio>>glowAudio>>noiseAudio))throw std::runtime_error("Incomplete audio mapping parameters");
            for(int flag:{audio,scanAudio,glitchAudio,dotAudio,blurAudio,distortAudio,glowAudio,noiseAudio})if(flag<0||flag>1)throw std::runtime_error("Invalid audio mapping switch");
            fx.audioReactive=audio!=0;fx.audioScanlines=scanAudio!=0;fx.audioGlitch=glitchAudio!=0;fx.audioDots=dotAudio!=0;
            fx.audioBlur=blurAudio!=0;fx.audioDistortion=distortAudio!=0;fx.audioGlow=glowAudio!=0;fx.audioNoise=noiseAudio!=0;
        }
        if(version>=12) {int zoom=0;if(!(in>>zoom>>fx.audioZoomAmount)||zoom<0||zoom>1||!std::isfinite(fx.audioZoomAmount)||fx.audioZoomAmount<0||fx.audioZoomAmount>.5f)throw std::runtime_error("Invalid audio zoom");fx.audioZoom=zoom!=0;}
        if(version>=14) {int random=0;if(!(in>>random>>fx.largeGlitchVariation)||random<0||random>1||!std::isfinite(fx.largeGlitchVariation)||fx.largeGlitchVariation<0||fx.largeGlitchVariation>1)throw std::runtime_error("Invalid large glitch variation");fx.largeGlitchRandomize=random!=0;}
        if(version>=15&&!(in>>fx.dotShape>>fx.dotRotation>>fx.rippleDotShape>>fx.rippleDotRotation))throw std::runtime_error("Incomplete dot shape parameters");
        if(version>=16) {
            {int enabled=0;if(!(in>>enabled>>fx.zoomTunnel.strength>>fx.zoomTunnel.period>>fx.zoomTunnel.zoom>>fx.zoomTunnel.centerX>>fx.zoomTunnel.centerY)||enabled<0||enabled>1)throw std::runtime_error("Invalid zoomTunnel parameters");fx.zoomTunnel.enabled=enabled!=0;}
            {int enabled=0;if(!(in>>enabled>>fx.rainRipples.strength>>fx.rainRipples.density>>fx.rainRipples.speed>>fx.rainRipples.radius>>fx.rainRipples.width)||enabled<0||enabled>1)throw std::runtime_error("Invalid rainRipples parameters");fx.rainRipples.enabled=enabled!=0;}
            {int enabled=0;if(!(in>>enabled>>fx.waterWave.strength>>fx.waterWave.amplitude>>fx.waterWave.frequency>>fx.waterWave.speed>>fx.waterWave.angle)||enabled<0||enabled>1)throw std::runtime_error("Invalid waterWave parameters");fx.waterWave.enabled=enabled!=0;}
            {int enabled=0;if(!(in>>enabled>>fx.edgeOutline.strength>>fx.edgeOutline.width>>fx.edgeOutline.threshold>>fx.edgeOutline.mode)||enabled<0||enabled>1)throw std::runtime_error("Invalid edgeOutline parameters");fx.edgeOutline.enabled=enabled!=0;}
            {int enabled=0;if(!(in>>enabled>>fx.pixelate.strength>>fx.pixelate.blockSize)||enabled<0||enabled>1)throw std::runtime_error("Invalid pixelate parameters");fx.pixelate.enabled=enabled!=0;}
            {int enabled=0;if(!(in>>enabled>>fx.sunPrism.strength>>fx.sunPrism.angle>>fx.sunPrism.width>>fx.sunPrism.position>>fx.sunPrism.speed)||enabled<0||enabled>1)throw std::runtime_error("Invalid sunPrism parameters");fx.sunPrism.enabled=enabled!=0;}
        }
        if(version>=17) {fx.ReadAdvanced(in,version);fx.blurMotion=false;}
        if(version>=18)fx.ReadSunAnimation(in);
        if(version>=19)fx.ReadSunCone(in);
        if(version>=20) {
            if(!(in>>fx.edgeOutline.color)||fx.edgeOutline.color>0xFFFFFF)throw std::runtime_error("Invalid edge outline color");
        } else fx.edgeOutline.color=fx.edgeOutline.mode==1?0:0xFFFFFF;
        if(version>=21) {int legacy;if(!(in>>legacy>>fx.audioAttack)||legacy<0||legacy>1||!std::isfinite(fx.audioAttack)||fx.audioAttack<.005||fx.audioAttack>1)throw std::runtime_error("Invalid audio mapping header");fx.audioLegacy=legacy!=0;fx.audioRules=ReadAudioRules(in);}
        if(version>=22)fx.particles.Read(in,version);
        if(version>=24) {fx.subParticles.Read(in,version);if(fx.subParticles.style==5)throw std::runtime_error("Invalid secondary particle style");}
        if(version>=26)fx.effectOrder=ReadEffectOrder(in,version);
        if(version>=27) {ScanCardsOptions retired;retired.Read(in,version);} // Discard the removed effect even from old clients.
        if(version>=34&&(!(in>>fx.blurSamples)||fx.blurSamples<5||fx.blurSamples>257))throw std::runtime_error("Invalid blur sample count");
        if(version>=35)fx.lightFlare.Read(in);
        if(version>=36) {fx.transform.Read(in);fx.transformProtocol=true;}
        if(version>=37)ReadAudioRuleBehavior(in,fx.audioRules);
        if(version>=38){
            unsigned count=0;
            if(!(in>>count)||count>16)throw std::runtime_error("Invalid package shader count");
            for(unsigned i=0;i<count;++i){
                PackageShader s;
                if(!(in>>s.generation>>s.file>>s.strength>>s.animated)||(s.generation!="builtin"&&(s.generation.size()!=64||
                    s.generation.find_first_not_of("0123456789abcdef")!=std::string::npos))||
                    s.file.size()>96||!s.file.ends_with(".cso")||s.file.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-")!=std::string::npos||
                    !std::isfinite(s.strength)||s.strength<0||s.strength>1||(s.animated!=0&&s.animated!=1))
                    throw std::runtime_error("Invalid package shader");
                fx.packageShaders.push_back(s);
            }
        }
        if(in>>extra)throw std::runtime_error("Trailing effects parameters");
        for(int flag:{scan,rgb,soft,vignette,glow,noise,motion,glitch,dots,dotGlow,dotMotion,glitchRgb,cluster,ripple,rippleGlow,bends,blur,distort,distortMotion})if(flag<0||flag>1)throw std::runtime_error("Invalid effects switch");
        auto range=[](float value,float min,float max) {if(!std::isfinite(value)||value<min||value>max)throw std::runtime_error("Effect parameter out of range");};
        range(fx.zoomTunnel.strength,0,1);range((float)fx.zoomTunnel.period,1.0f,20.0f);range((float)fx.zoomTunnel.zoom,1.1f,8.0f);range((float)fx.zoomTunnel.centerX,0.0f,1.0f);range((float)fx.zoomTunnel.centerY,0.0f,1.0f);
        range(fx.rainRipples.strength,0,1);range((float)fx.rainRipples.density,2.0f,10.0f);range((float)fx.rainRipples.speed,0.0f,3.0f);range((float)fx.rainRipples.radius,0.2f,1.0f);range((float)fx.rainRipples.width,0.04f,0.3f);
        range(fx.waterWave.strength,0,1);range((float)fx.waterWave.amplitude,0.0f,40.0f);range((float)fx.waterWave.frequency,1.0f,30.0f);range((float)fx.waterWave.speed,0.0f,3.0f);range((float)fx.waterWave.angle,-180.0f,180.0f);
        range(fx.edgeOutline.strength,0,1);range((float)fx.edgeOutline.width,0.5f,4.0f);range((float)fx.edgeOutline.threshold,0.0f,1.0f);range((float)fx.edgeOutline.mode,0.0f,2.0f);
        range(fx.pixelate.strength,0,1);range((float)fx.pixelate.blockSize,2.0f,128.0f);
        range(fx.sunPrism.strength,0,1);range((float)fx.sunPrism.angle,-180.0f,180.0f);range((float)fx.sunPrism.width,0.05f,1.0f);range((float)fx.sunPrism.position,-1.0f,1.0f);range((float)fx.sunPrism.speed,0.0f,1.0f);
        range(fx.intensity,0,1);range(fx.scanlineStrength,0,.35f);range(fx.scanlineDensity,1,260);range(fx.rgbOffset,0,version>=13?16384.0f:3.0f);range(fx.softRadius,0,100);
        range(fx.brightness,-.5f,.5f);range(fx.contrast,.5f,1.5f);range(fx.saturation,0,2);range(fx.vignetteStrength,0,1);range(fx.glowStrength,0,3);range(fx.glowThreshold,0,1);range(fx.noiseStrength,0,.3f);
        range(fx.scanlineSpeed,-60,60);range(fx.glitchStrength,0,1);range(fx.glitchRate,0,20);range(fx.glitchCoverage,0,1);
        range((float)fx.largeGlitchMode,0,3);range(fx.largeGlitchArea,.2f,.6f);range(fx.largeGlitchStrength,0,1);range(fx.largeGlitchRate,0,20);range(fx.largeGlitchBlend,0,1);range(fx.largeGlitchRgbOffset,0,80);
        range(fx.largeGlitchInterval,1,20);range(fx.largeGlitchDuration,.05f,.9f);range((float)fx.largeGlitchSmallCount,0,10);range(fx.largeGlitchSmallStrength,0,1);range(fx.glitchHueShift,-180,180);range(fx.largeGlitchHueShift,-180,180);
        range((float)fx.audioSource,0,1);range((float)fx.audioBand,0,3);range(fx.audioSensitivity,.25f,5);range(fx.audioSmoothing,.02f,2);range(fx.audioBeatThreshold,.05f,.95f);range(fx.audioAmount,0,1);
        range((float)fx.dotShape,0,7);range((float)fx.rippleDotShape,0,7);range(fx.dotRotation,0,360);range(fx.rippleDotRotation,0,360);
        range(fx.dotStrength,0,1);range(fx.dotSpacing,4,40);range(fx.dotSize,.1f,1);range(fx.dotGlowStrength,0,1);range(fx.dotSpeed,-2,2);range(fx.dotPulse,0,1);
        range(fx.glitchRgbOffset,0,80);range(fx.dotRippleStrength,0,2);range(fx.dotRippleSpeed,0,4);range(fx.dotRippleWidth,.02f,.5f);range((float)fx.dotRippleCount,1,6);
        range(fx.rippleDotStrength,0,1);range(fx.rippleDotSpacing,4,40);range(fx.rippleDotSize,.1f,1);
        range(fx.clusterBlend,0,1);range(fx.clusterSize,.08f,.4f);range((float)fx.clusterSmallCount,0,5);
        range(fx.scanBendAmplitude,0,80);range(fx.scanBendSpan,40,800);range(fx.scanBendDensity,0,1);
        range(fx.blurRadius,0,100);range(fx.blurStrength,0,1);range((float)fx.distortionMode,0,2);range(fx.distortionStrength,-1,1);range(fx.distortionScale,1,20);
        range((float)fx.blurMotionMode,0,2);range(fx.blurMotionSpeed,-2,2);range(fx.blurCoverage,0,1);range(fx.blurScale,.5f,8);range(fx.blurFeather,0,1);range(fx.blurAngle,-180,180);
        range(fx.distortionSpeed,-4,4);range(fx.distortionCenterX,0,1);range(fx.distortionCenterY,0,1);range(fx.scanlineWidth,.25f,12);range(fx.scanlineSoftness,0,1);range(fx.scanlineAngle,-90,90);
        fx.blur=blur!=0;fx.distortion=distort!=0;fx.distortionMotion=distortMotion!=0;
        fx.rippleDotGlow=rippleGlow!=0;fx.scanlineBends=bends!=0;
        for(const auto& layer:fx.layers) {range((float)layer.mode,0,3);range(layer.strength,0,1);range(layer.rate,0,40);range(layer.coverage,0,1);range(layer.size,2,240);range(layer.rgbOffset,0,80);}
        fx.glitchRgbSplit=glitchRgb!=0;fx.glitchCluster=cluster!=0;fx.dotRipple=ripple!=0;
        fx.scanlineMotion=motion!=0;fx.glitch=glitch!=0;fx.dots=dots!=0;fx.dotGlow=dotGlow!=0;fx.dotMotion=dotMotion!=0;
        fx.scanlines=scan!=0;fx.rgbSplit=rgb!=0;fx.softening=false;fx.vignette=vignette!=0;fx.glow=glow!=0;fx.noise=noise!=0;return fx;
    }
};
