#include "AudioAnalysis.h"
#include <algorithm>
#include <cmath>

void AudioMeter::Reset(int sampleRate,int channels) {
    channels_.clear();power_=bassPower_=midPower_=treblePower_=0;
    if(sampleRate<8000||sampleRate>768000||channels<1||channels>32)return;
    channels_.resize(static_cast<size_t>(channels));
    constexpr double tau=6.283185307179586;
    lowAlpha_=static_cast<float>(1-std::exp(-tau*180/sampleRate));
    upperAlpha_=static_cast<float>(1-std::exp(-tau*2500/sampleRate));
    // Keep drum attacks visible before the user-controlled release smoothing.
    // A 150 ms RMS window erased most short transients before rhythm detection.
    energyAlpha_=static_cast<float>(1-std::exp(-1.0/(sampleRate*.025)));
}

void AudioMeter::ProcessInterleaved(const float* samples,size_t frameCount) {
    if(channels_.empty())return;
    const size_t channelCount=channels_.size();
    for(size_t frame=0;frame<frameCount;++frame) {
        double energy=0,lowEnergy=0,midEnergy=0,highEnergy=0;
        for(size_t channel=0;channel<channelCount;++channel) {
            float sample=samples?samples[frame*channelCount+channel]:0;
            if(!std::isfinite(sample))sample=0;
            sample=std::clamp(sample,-1.0f,1.0f);
            auto& filter=channels_[channel];
            filter.low+=lowAlpha_*(sample-filter.low);
            filter.upper+=upperAlpha_*(sample-filter.upper);
            float middle=filter.upper-filter.low,high=sample-filter.upper;
            energy+=sample*sample;lowEnergy+=filter.low*filter.low;
            midEnergy+=middle*middle;highEnergy+=high*high;
        }
        // Average channel energy, not waveforms: opposite-phase stereo still reacts.
        double count=static_cast<double>(channelCount);
        power_+=energyAlpha_*(energy/count-power_);
        bassPower_+=energyAlpha_*(lowEnergy/count-bassPower_);
        midPower_+=energyAlpha_*(midEnergy/count-midPower_);
        treblePower_+=energyAlpha_*(highEnergy/count-treblePower_);
    }
}

AudioLevels AudioMeter::Levels() const {
    if(channels_.empty())return {};
    auto amplitude=[](double power) {return static_cast<float>(std::sqrt(std::clamp(power,0.0,1.0)));};
    return {amplitude(power_),amplitude(bassPower_),amplitude(midPower_),amplitude(treblePower_),true,0};
}
