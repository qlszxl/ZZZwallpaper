#pragma once
#include "VideoEffects.h"
#include <algorithm>
#include <cmath>
#include <cstdint>

// A burst gets one deterministic sample per parameter. Values stay constant
// throughout that burst, including pauses, and never overwrite saved settings.
constexpr float GlitchVariationHash(uint32_t seed) {
    seed=(seed^(seed>>16u))*2246822519u;
    seed=(seed^(seed>>13u))*3266489917u;seed^=seed>>16u;
    return (seed&65535u)/65536.0f;
}
constexpr float GlitchVary(float value,float range,float sample,float low,float high) {
    return std::clamp(value*(1+(sample*2-1)*range),low,high);
}
struct GlitchBurst {double cycle,phase;};
enum class GlitchBurstSource {None,Timed,Audio};
constexpr GlitchBurstSource ChooseGlitchBurst(double timedPhase,double timedDuration,
    bool audioEnabled,uint64_t audioBurst,double audioPhase,double audioDuration) {
    const bool timed=timedPhase>=0&&timedPhase<timedDuration;
    const bool beat=audioEnabled&&audioBurst>0&&audioPhase>=0&&audioPhase<audioDuration;
    // Beats supplement the periodic schedule. An idle or expired audio event
    // must never suppress a configured timed burst. Prefer the newer event
    // during overlap so its random sample remains stable throughout the burst.
    if(beat&&(!timed||audioPhase<timedPhase))return GlitchBurstSource::Audio;
    return timed?GlitchBurstSource::Timed:GlitchBurstSource::None;
}
inline GlitchBurst GlitchBurstAt(double time,double interval,bool random,float range) {
    time=std::max(0.0,time);
    double cycle=std::floor(time/interval);
    if(!random||range<=0)return {cycle,time-cycle*interval};
    // Jitter each start by at most half the requested interval variation.
    // Adjacent intervals therefore stay within base * (1 +/- range), with
    // ordered starts and no frame-dependent state or unbounded catch-up loop.
    auto start=[&](double n) {
        if(n<=0)return 0.0;
        uint32_t id=(uint32_t)std::fmod(n,4294967296.0);
        return n*interval+(GlitchVariationHash(id*1973u+9109u)-.5)*interval*range;
    };
    if(cycle>0&&time<start(cycle))cycle--;
    else if(time>=start(cycle+1))cycle++;
    return {cycle,time-start(cycle)};
}
inline void VaryLargeGlitch(VideoEffects& fx,uint32_t burst) {
    if(!fx.largeGlitchRandomize||fx.largeGlitchVariation<=0)return;
    uint32_t seed=burst*1973u+17027u;
    auto vary=[&](float value,float low,float high,uint32_t salt) {
        return GlitchVary(value,fx.largeGlitchVariation,GlitchVariationHash(seed+salt),low,high);
    };
    fx.largeGlitchArea=vary(fx.largeGlitchArea,.2f,.6f,1103u);
    fx.largeGlitchStrength=vary(fx.largeGlitchStrength,0,1.5f,2207u);
    fx.largeGlitchBlend=vary(fx.largeGlitchBlend,0,1,3301u);
    fx.largeGlitchRgbOffset=vary(fx.largeGlitchRgbOffset,0,80,4409u);
    fx.largeGlitchRate=vary(fx.largeGlitchRate,0,20,5519u);
    fx.largeGlitchDuration=vary(fx.largeGlitchDuration,.05f,.9f,6619u);
    fx.largeGlitchSmallCount=(int)std::round(vary((float)fx.largeGlitchSmallCount,0,10,7723u));
    fx.largeGlitchSmallStrength=vary(fx.largeGlitchSmallStrength,0,1,8831u);
}
