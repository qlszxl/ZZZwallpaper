#pragma once
#include "AudioParameters.h"
#include "AudioAnalysis.h"
#include "AudioTriggerLimiter.h"

// Per-rule envelopes share one audio snapshot. They never modify saved effects.
class AudioRuleEngine {
    struct State {
        AudioRule rule;bool initialized=false,armed=true,aboveThreshold=false,high=false;
        AudioTriggerLimiter limiter;
        bool adaptive=false;uint64_t epoch=0,lastOnset=0;
        double average=0,previous=0,previousRaw=0,peak=0,signal=0,age=1e9,detectionAge=1e9,phase=0;uint64_t beats=0;
    };
    std::array<State,128> states{};
    std::array<double,4> peaks{};
    bool adaptiveInput=false;
    AudioTriggerHistory particleTriggers;
    template<class Drive> void StepAdaptive(State& s,const AudioRule& r,const AudioLevels& levels,const VideoEffects& fx,
        double baseline,double dt,double time,bool first,int index,bool particleTrigger,Drive& drive) {
        s.age+=dt;s.detectionAge+=dt;
        const bool hard=r.thresholdSwitch&&!IsAudioTrigger(r.parameter),discrete=r.response==1||IsAudioTrigger(r.parameter);
        bool hit=false;
        if(!first&&dt>0) {
            uint64_t begin=std::max(s.lastOnset+1,levels.onsetSequence>levels.onsets.size()?levels.onsetSequence-levels.onsets.size()+1:1);
            for(uint64_t sequence=begin;sequence<=levels.onsetSequence;++sequence) {
                const auto& event=levels.onsets[(sequence-1)%levels.onsets.size()];
                double age=std::max(0.0,levels.seconds-event.seconds);
                if(event.sequence!=sequence||age>std::min(1.0,dt+.03)||event.strength[r.band]*std::sqrt(r.sensitivity)<.25+.65*r.threshold)continue;
                s.detectionAge=age;
                if(hard)hit=true;
                else if(!discrete||!r.enabled||!r.triggerLimitEnabled||s.limiter.Accept(time-age,r.triggerLimitWindow,r.triggerLimitCount)) {
                    s.age=age;++s.beats;hit=true;if(particleTrigger&&r.enabled)particleTriggers.Push(age);
                }
            }
        }
        if(first||dt>0)s.lastOnset=levels.onsetSequence;
        const double values[]={levels.level,levels.bass,levels.mid,levels.treble};
        double age=hard?s.detectionAge:s.age;
        double target=discrete?(age<=.065?1.0:0.0):std::clamp(values[r.band]*r.sensitivity,0.0,1.0);
        if(first&&!discrete)s.signal=target;
        else if(dt>0)s.signal+=(1-std::exp(-dt/(target>s.signal?r.attack:r.release)))*(target-s.signal);
        // A short transient remains observable when it occurred between two video
        // frames. Shared sample timestamps also preserve the trigger quota timing.
        if(discrete&&hit) {
            double rise=1-std::exp(-std::min(.065,age+.01)/r.attack);
            s.signal=std::max(s.signal,rise*std::exp(-std::max(0.0,age-.065)/r.release));
        }
        if(s.signal<.00001)s.signal=0;
        double output=std::clamp(s.signal,0.0,1.0);
        if(hard) {
            bool above=output>=r.switchThreshold;
            if(!above)s.high=false;
            else if(!s.aboveThreshold) {
                s.high=r.enabled&&(!r.triggerLimitEnabled||s.limiter.Accept(time,r.triggerLimitWindow,r.triggerLimitCount));
                if(s.high) {s.age=0;++s.beats;}
            }
            s.aboveThreshold=above;output=s.high?1:0;
        }
        double mapped=hard?(s.high?r.strong:r.weak):r.weak+(r.strong-r.weak)*output;
        double value=r.enabled?(hard?mapped:baseline+(mapped-baseline)*fx.audioAmount):baseline;
        if(dt>0)s.phase+=value*dt;
        drive.mappings[index]={r.parameter,value,output,s.phase,r.enabled!=0,s.age,s.beats};
    }
public:
    void Reset(){states={};peaks={};adaptiveInput=false;particleTriggers={};}
    template<class Drive> void Step(const AudioLevels& levels,const VideoEffects& fx,double dt,double time,Drive& drive) {
        dt=std::clamp(std::isfinite(dt)?dt:0.0,0.0,1.0);
        const double inputs[]={levels.level,levels.bass,levels.mid,levels.treble};
        if(adaptiveInput!=levels.adaptive) {peaks={};adaptiveInput=levels.adaptive;}
        if(!levels.adaptive)for(int b=0;b<4;++b)peaks[b]=std::max(std::isfinite(inputs[b])?std::clamp(inputs[b],0.0,1.0):0.0,peaks[b]*std::exp(-dt/2.5));
        bool hasParticleTrigger=false;
        drive.mappingCount=static_cast<int>(fx.audioRules.size());std::array<bool,128> used{};
        for(int i=0;i<drive.mappingCount;++i) {
            const auto& r=fx.audioRules[i];auto& s=states[r.parameter-1];used[r.parameter-1]=true;
            const double raw=std::isfinite(inputs[r.band])?std::clamp(inputs[r.band],0.0,1.0):0;
            double gate=std::clamp((raw-.0015)/.0025,0.0,1.0);gate=gate*gate*(3-2*gate);
            double energy=gate*raw/std::max(.01,peaks[r.band])*r.sensitivity;
            const double baseline=r.parameter==static_cast<int>(AudioParameterId::AudioZoomAmount)?0:AudioParameterValue(fx,r.parameter);
            bool first=!s.initialized||s.rule!=r||s.adaptive!=levels.adaptive||(levels.adaptive&&s.epoch!=levels.epoch);
            if(first) {
                double phase=s.initialized&&s.rule.parameter==r.parameter?s.phase:time*baseline;
                s={};s.initialized=true;s.rule=r;s.average=s.previous=s.peak=energy;s.previousRaw=raw;s.phase=phase;s.adaptive=levels.adaptive;s.epoch=levels.epoch;s.lastOnset=levels.onsetSequence;
            }
            const bool particleTrigger=r.parameter==static_cast<int>(AudioParameterId::Particles_Trigger);
            if(particleTrigger) {hasParticleTrigger=true;if(first)particleTriggers={};particleTriggers.Step(dt);}
            if(levels.adaptive) {StepAdaptive(s,r,levels,fx,baseline,dt,time,first,i,particleTrigger,drive);continue;}
            s.age+=dt;s.detectionAge+=dt;
            const double contrast=.035+r.threshold*.16;
            if(!s.armed&&(s.peak-energy>contrast*.65||energy<.06+r.threshold*.2))s.armed=true;
            bool onset=!first&&dt>0&&s.armed&&s.detectionAge>=.16&&raw>s.previousRaw+.00001&&energy>s.previous+.003&&energy>s.average+contrast&&energy>.06+r.threshold*.45;
            const bool hard=r.thresholdSwitch&&!IsAudioTrigger(r.parameter);
            const bool discrete=r.response==1||IsAudioTrigger(r.parameter);
            auto allowed=[&] {return !r.triggerLimitEnabled||s.limiter.Accept(time,r.triggerLimitWindow,r.triggerLimitCount);};
            if(onset) {
                // Consume the detected edge even when rejected, so a sustained
                // peak cannot retry as soon as the quota becomes available.
                s.detectionAge=0;s.armed=false;s.peak=energy;
                if(!hard&&(!discrete||!r.enabled||allowed())) {
                    s.age=0;++s.beats;if(particleTrigger&&r.enabled)particleTriggers.Push();
                }
            } else s.peak=std::max(s.peak,energy);
            const double responseAge=hard?s.detectionAge:s.age;
            double target=!discrete?std::clamp(energy,0.0,1.0):(responseAge<=.065?1.0:0.0);
            if(first&&r.response==0&&!IsAudioTrigger(r.parameter))s.signal=target;
            else if(dt>0)s.signal+=(1-std::exp(-dt/(target>s.signal?r.attack:r.release)))*(target-s.signal);
            if(s.signal<.00001)s.signal=0;
            double output=std::clamp(s.signal,0.0,1.0);
            if(hard) {
                const bool above=output>=r.switchThreshold;
                if(!above)s.high=false;
                else if(!s.aboveThreshold) {
                    s.high=r.enabled&&allowed();
                    if(s.high){s.age=0;++s.beats;}
                }
                s.aboveThreshold=above;output=s.high?1:0;
            }
            double mapped=hard?(s.high?r.strong:r.weak):r.weak+(r.strong-r.weak)*output;
            // Binary mode uses the configured endpoints directly. Global audio
            // enable/zero amount is still handled by AudioReactive/Renderer.
            double value=r.enabled?(hard?mapped:baseline+(mapped-baseline)*fx.audioAmount):baseline;
            if(dt>0)s.phase+=value*dt;
            drive.mappings[i]={r.parameter,value,output,s.phase,r.enabled!=0,s.age,s.beats};
            s.average+=(1-std::exp(-dt/.32))*(energy-s.average);s.previous=energy;s.previousRaw=raw;
        }
        if(!hasParticleTrigger)particleTriggers={};drive.particleTriggers=particleTriggers;
        for(size_t i=0;i<states.size();++i)if(!used[i])states[i].initialized=false;
    }
};
