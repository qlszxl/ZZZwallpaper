#pragma once
#include "RenderSettings.h"
#include "VideoEffects.h"

struct StackRasterAxis {
    int first=0,count=1;
    float offset=0,scale=1,bias=0;
};
inline StackRasterAxis EffectStackRasterAxis(float origin,float extent) {
    // Pixel centers, not the rounded logical viewport size, determine the
    // storage grid. A fractional contain viewport must keep the same centers
    // through every intermediate stage and its final composite.
    int first=static_cast<int>(std::ceil(origin-.5f));
    int end=static_cast<int>(std::ceil(origin+extent-.5f));
    int count=std::max(1,end-first);
    float offset=origin-first;
    return {first,count,offset,extent/count,offset/count};
}

struct EffectStage {
    unsigned effects=0;int lastKind=-1;
    bool Contains(int kind) const {return (effects&(1u<<kind))!=0;}
};
inline int PointwiseEffectRank(int kind) {
    // This is the order inside styled(). All other effects sample neighboring
    // pixels, or need scratch targets, so they must finish a stage first.
    switch(kind) {case 0:return 0;case 4:return 1;case 6:return 2;case 5:return 3;case 20:return 4;case 23:return 5;default:return -1;}
}
inline bool EffectLayerActive(const RenderSettings& s,const VideoEffects& fx,int kind) {
    switch(kind) {
    case 0:return s.brightness!=0||s.contrast!=1||s.saturation!=1;
    case 1:return s.rgb>0;
    case 2:return false; // Retired softening slot.
    case 3:return s.glow>0;
    case 4:return s.vignette>0;
    case 5:return s.noise>0;
    case 6:return s.scan>0;
    case 7:
        if(s.groupCount==0)return s.glitch>0&&s.coverage>0;
        if(s.clusterBlend<=0)return false;
        for(unsigned i=0;i<s.groupCount;++i)if(s.groups[i].strength>0&&s.groups[i].coverage>0&&(s.groups[i].mode!=3||s.groups[i].rgb>0))return true;
        return false;
    case 8:return s.largeStrength>0&&s.largeBlend>0;
    case 9:return s.dots>0;
    case 10:return s.rippleDots>0;
    case 11:return s.blurMix>0;
    case 12:return s.distortionStrength!=0;
    case 15:return s.zoomMix>0;
    case 16:return s.rainMix>0||s.advanced.rainOutlineMix>0||
        (fx.particles.enabled&&fx.particles.opacity>0)||(fx.subParticles.enabled&&fx.subParticles.opacity>0);
    case 17:return s.waterMix>0&&s.waterAmplitude>0;
    case 18:return s.edgeMix>0;
    case 19:return s.pixelMix>0;
    case 20:return s.sunMix>0;
    case 24:return s.transform.mix>0;
    case 23:return s.lightFlare.mix>0&&s.lightFlare.brightness>0;
    case 22:return false; // Retired warehouse visual effect.
    default:return false;
    }
}
inline std::vector<EffectStage> BuildEffectStack(const RenderSettings& settings,const VideoEffects& fx) {
    std::vector<EffectStage> stages;stages.reserve(fx.effectOrder.size());
    for(int kind:fx.effectOrder) {
        if(!EffectLayerActive(settings,fx,kind))continue;
        int rank=PointwiseEffectRank(kind);
        if(!stages.empty()&&rank>=0&&PointwiseEffectRank(stages.back().lastKind)>=0&&rank>PointwiseEffectRank(stages.back().lastKind)) {
            stages.back().effects|=1u<<kind;stages.back().lastKind=kind;
        } else stages.push_back({1u<<kind,kind});
    }
    return stages;
}
inline RenderSettings EffectStageSettings(const RenderSettings& source,const EffectStage& stage) {
    auto s=source;
    // Stage inputs already have their source fit/rotation applied. The global
    // amount is deliberately absent here (including particle alpha), and is
    // mixed against the untouched video once by stackComposite.
    s.cx=s.cy=1;s.angle=0;s.intensity=1;s.bx=s.by=0;s.blurMaskPass=0;
    s.sourceOffsetX=s.sourceOffsetY=0;
    s.advanced.maskPreview=0;s.advanced.filterMethod=0;
    if(!stage.Contains(0)) {s.brightness=0;s.contrast=s.saturation=1;}
    if(!stage.Contains(1))s.rgb=0;
    if(!stage.Contains(2))s.soft=0;
    if(!stage.Contains(3))s.glow=0;
    if(!stage.Contains(4))s.vignette=0;
    if(!stage.Contains(5))s.noise=0;
    if(!stage.Contains(6))s.scan=0;
    if(!stage.Contains(7)) {s.glitch=0;s.groupCount=0;}
    if(!stage.Contains(8)) {s.largeStrength=0;s.largeSatelliteCount=0;}
    if(!stage.Contains(9))s.dots=0;
    if(!stage.Contains(10))s.rippleDots=0;
    if(!stage.Contains(11))s.blurMix=0;
    if(!stage.Contains(12))s.distortionStrength=0;
    if(!stage.Contains(15))s.zoomMix=0;
    if(!stage.Contains(16)) {s.rainMix=0;s.advanced.rainOutlineMix=0;s.particles.mix=0;}
    if(!stage.Contains(17))s.waterMix=0;
    if(!stage.Contains(18))s.edgeMix=0;
    if(!stage.Contains(19))s.pixelMix=0;
    if(!stage.Contains(20))s.sunMix=0;
    if(!stage.Contains(24))s.transform.mix=0;
    if(!stage.Contains(23))s.lightFlare.mix=0;
    return s;
}
