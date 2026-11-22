#pragma once
#include <array>
#include <cstdint>
#include <vector>
#include <algorithm>
#include <cmath>
#include <istream>
#include <stdexcept>
#include "AudioParameters.Generated.h"
enum class AudioParameterId {
#define AUDIO_ID(id,key,member,guard,min,max) key=id,
 AUDIO_PARAMETERS(AUDIO_ID)
#undef AUDIO_ID
#define AUDIO_TRIGGER_ID(id,key) key=id,
 AUDIO_TRIGGERS(AUDIO_TRIGGER_ID)
#undef AUDIO_TRIGGER_ID
};
inline bool IsAudioTrigger(int id) {return id>=static_cast<int>(AudioParameterId::Glitch_Trigger)&&id<=static_cast<int>(AudioParameterId::Particles_Trigger);}
struct AudioRule {
    int parameter=0,enabled=1,band=0,response=0;
    double weak=0,strong=0,sensitivity=1.5,attack=.018,release=.18,threshold=.35;
    bool thresholdSwitch=false,triggerLimitEnabled=false;
    double switchThreshold=.5,triggerLimitWindow=1;int triggerLimitCount=1;
    bool operator==(const AudioRule&) const=default;
};
struct AudioMappedValue {int parameter=0;double value=0,signal=0,phase=0;bool enabled=false;double beatAge=1e9;uint64_t beats=0;};
// Fixed history for overlapping particle bursts; no per-beat allocation.
struct AudioTriggerHistory {
    static constexpr size_t Capacity=8;
    std::array<double,Capacity> ages{};uint64_t count=0;
    void Step(double dt) {for(auto& age:ages)age=std::min(1e9,age+dt);}
    void Push(double age=0) {ages[count%Capacity]=std::clamp(age,0.0,1e9);++count;}
};
inline std::vector<AudioRule> ReadAudioRules(std::istream& in) {
    int count;if(!(in>>count)||count<0||count>128)throw std::runtime_error("Invalid audio rule count");
    std::vector<AudioRule> rules;rules.reserve(count);
    auto range=[](double v,double lo,double hi){return std::isfinite(v)&&v>=lo&&v<=hi;};
    for(int i=0;i<count;++i) {
        AudioRule r;if(!(in>>r.parameter>>r.enabled>>r.band>>r.response>>r.weak>>r.strong>>r.sensitivity>>r.attack>>r.release>>r.threshold))throw std::runtime_error("Incomplete audio rule");
        double lo=0,hi=0;
        switch(r.parameter) {
#define AUDIO_RANGE(id,key,member,guard,min,max) case id:lo=min;hi=max;break;
            AUDIO_PARAMETERS(AUDIO_RANGE)
#undef AUDIO_RANGE
#define AUDIO_TRIGGER_RANGE(id,key) case id:lo=0;hi=1;break;
            AUDIO_TRIGGERS(AUDIO_TRIGGER_RANGE)
#undef AUDIO_TRIGGER_RANGE
            default:throw std::runtime_error("Unknown audio parameter");
        }
        if(!range(r.enabled,0,1)||!range(r.band,0,3)||!range(r.response,0,1)||!range(r.weak,lo,hi)||!range(r.strong,lo,hi)||!range(r.sensitivity,.25,5)||!range(r.attack,.005,1)||!range(r.release,.02,2)||!range(r.threshold,.05,.95))throw std::runtime_error("Invalid audio rule value");
        for(const auto& old:rules)if(old.parameter==r.parameter)throw std::runtime_error("Duplicate audio parameter");
        rules.push_back(r);
    }
    return rules;
}

// Optional tail introduced in wire v37. Old wire rules retain disabled defaults.
inline void ReadAudioRuleBehavior(std::istream& in,std::vector<AudioRule>& rules) {
    int count;if(!(in>>count)||count!=static_cast<int>(rules.size()))throw std::runtime_error("Invalid audio behavior count");
    std::array<bool,128> seen{};
    for(int i=0;i<count;++i) {
        int id,hard,limited,maximum;double threshold,window;
        if(!(in>>id>>hard>>threshold>>limited>>window>>maximum))throw std::runtime_error("Incomplete audio behavior");
        auto rule=std::find_if(rules.begin(),rules.end(),[&](const AudioRule& r){return r.parameter==id;});
        if(id<1||id>128||rule==rules.end()||seen[id-1]||hard<0||hard>1||limited<0||limited>1||
            !std::isfinite(threshold)||threshold<.01||threshold>1||!std::isfinite(window)||window<.1||window>60||maximum<1||maximum>100)
            throw std::runtime_error("Invalid audio behavior value");
        seen[id-1]=true;rule->thresholdSwitch=hard!=0&&!IsAudioTrigger(id);rule->switchThreshold=threshold;
        rule->triggerLimitEnabled=limited!=0;rule->triggerLimitWindow=window;rule->triggerLimitCount=maximum;
    }
}
