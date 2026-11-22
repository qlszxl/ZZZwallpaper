#pragma once
#include <algorithm>
#include <cmath>
#include "RenderSettings.h"

// Weights depend on the user-selected count, not radius or output resolution.
// Compute them once on the CPU per submitted frame, never per output pixel.
inline void ConfigureBlurSampling(RenderSettings& settings,int samples,int method) {
    settings.blurSamples=static_cast<uint32_t>(std::clamp(samples,5,257));
    float sum=0;
    for(uint32_t i=0;i<settings.blurSamples;++i) {
        float t=2.f*i/(settings.blurSamples-1)-1.f;
        float weight=method==0?std::exp(-4.5f*t*t):1.f;
        settings.blurWeights[i/4][i%4]=weight;sum+=weight;
    }
    for(uint32_t i=0;i<settings.blurSamples;++i)settings.blurWeights[i/4][i%4]/=sum;
}
