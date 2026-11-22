#include "AudioReactive.h"
#include "AudioImpactEnvelope.h"
#include "AudioRuleEngine.h"
#include "AudioAnalysis.h"
#include "AudioCapture.h"
#include "VideoAudioReader.h"
#include "VideoEffects.h"
#include "../../Shared/MediaCore/MediaSession.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <limits>

namespace {
bool HasTargets(const VideoEffects& fx) {
    if(fx.HasAudioRules())return true;
    if(!fx.audioLegacy)return false;
    bool cluster=false;
    if(fx.glitchCluster&&fx.clusterBlend>0)
        for(int i=0;i<=fx.clusterSmallCount;++i)cluster|=fx.layers[i].Active();
    bool glitches=(fx.glitch&&(fx.glitchCluster?cluster:(fx.glitchStrength>0&&fx.glitchCoverage>0)))||
        (fx.largeGlitch&&fx.largeGlitchStrength>0&&fx.largeGlitchBlend>0);
    return (fx.audioZoom&&(!fx.transformProtocol||(fx.transform.enabled&&fx.transform.strength>0))&&fx.audioZoomAmount>0)||(fx.audioScanlines&&fx.scanlines&&fx.scanlineStrength>0)||
        (fx.audioGlitch&&glitches)||
        (fx.audioDots&&((fx.dots&&fx.dotStrength>0)||(fx.dotRipple&&fx.rippleDotStrength>0)))||
        (fx.audioBlur&&fx.blur&&fx.blurStrength>0&&fx.blurRadius>0&&(!fx.blurMotion||fx.blurCoverage>0))||
        (fx.audioDistortion&&fx.distortion&&fx.distortionStrength!=0)||
        (fx.audioGlow&&fx.glow&&fx.glowStrength>0)||
        (fx.audioNoise&&fx.noise&&fx.noiseStrength>0);
}
}

struct AudioReactive::Impl {
    enum class Mode {Disabled,NoTargets,Paused,Running};
    const std::string path;
    std::atomic_bool& stopped;
    std::unique_ptr<AudioCapture> capture;
    std::wstring captureDevice;
    VideoAudioReader video;
    Mode mode=Mode::Disabled;
    int source=-1,band=-1;
    AudioDrive drive;
    AudioImpactEnvelope impact;
    AudioRuleEngine rules;
    std::string status="disabled";
    int32_t error=0;
    double previousTime=std::numeric_limits<double>::quiet_NaN(),timeline=0;
    double lastBeat=-1e9,lastLarge=-1e9,pulseAge=1e9;
    float energyAverage=0,previousEnergy=0,previousAmplitude=0,onsetPeak=0,valleyEnergy=0,referencePeak=0;
    float sensitivity=-1;
    bool armed=true,baselineReady=false,hadAdaptive=false;
    uint64_t sharedEpoch=0,sharedOnset=0;
    bool resumeWaiting=false;
    std::chrono::steady_clock::time_point resumeDeadline{};

    Impl(const std::string& file,std::atomic_bool& cancel):path(file),stopped(cancel) {}
    void CloseSources() {
        if(capture)capture->SetEnabled(false);
        video.Close();
    }
    void ResetOnset() {
        impact.Reset();drive.impact=0;
        energyAverage=previousEnergy=previousAmplitude=onsetPeak=valleyEnergy=referencePeak=0;
        armed=true;baselineReady=false;sharedEpoch=sharedOnset=0;
    }
    void ResetDetection() {
        ResetOnset();
        previousTime=std::numeric_limits<double>::quiet_NaN();lastBeat=-1e9;
    }
    void Reset() {
        ResetDetection();rules.Reset();drive={};timeline=0;lastLarge=-1e9;pulseAge=1e9;error=0;resumeWaiting=false;
    }
    void OpenSource() {
        error=0;
        try {
            if(source==0) {
                if(!capture)capture=std::make_unique<AudioCapture>();
                capture->SetDevice(captureDevice);
                capture->SetEnabled(true);
            } else video.Open(path,stopped);
        } catch(...) {
            // Thread/device initialization failures must not stop video playback.
            error=static_cast<int32_t>(0x80004005u);
        }
        status=source==0?"system-unavailable":"video-unavailable";
    }
    void Configure(const VideoEffects& fx,bool compareOriginal,bool suspended) {
        Mode next;
        if(!fx.audioReactive||compareOriginal||fx.audioAmount<=0||fx.intensity<=0||stopped.load())next=Mode::Disabled;
        else if(!HasTargets(fx))next=Mode::NoTargets;
        else if(suspended)next=Mode::Paused;
        else next=Mode::Running;
        // Changing frequency bands or sensitivity changes the energy scale, not the animation
        // phase. Establish a fresh onset baseline without reopening either source.
        if(band!=fx.audioBand||sensitivity!=fx.audioSensitivity) {
            band=fx.audioBand;sensitivity=fx.audioSensitivity;ResetOnset();
        }
        const bool sourceChanged=source!=fx.audioSource;
        if(next==mode&&!sourceChanged)return;
        const bool preserveResume=mode==Mode::Paused&&next==Mode::Running&&!sourceChanged&&drive.active;
        resumeWaiting=false;
        CloseSources();
        if(sourceChanged||next==Mode::Disabled||next==Mode::NoTargets)Reset();
        else if(next==Mode::Paused||mode==Mode::Paused) {
            // Preserve the last visible drive and phase. Reopening after pause must
            // not integrate wall-clock pause time or replay the previous onset.
            ResetDetection();error=0;
        }
        source=fx.audioSource;mode=next;
        switch(mode) {
        case Mode::Disabled:status="disabled";break;
        case Mode::NoTargets:status="no-targets";break;
        case Mode::Paused:status="paused";break;
        case Mode::Running:
            OpenSource();
            if(preserveResume&&error==0) {
                resumeWaiting=true;
                resumeDeadline=std::chrono::steady_clock::now()+std::chrono::milliseconds(750);
            }
            break;
        }
    }
    AudioDrive Sample(const VideoFrame& frame,const VideoEffects& fx) {
        if(mode!=Mode::Running)return drive;
        AudioLevels levels;
        if(source==0) {if(capture)levels=capture->Snapshot();}
        else levels=video.Sample(frame.pts);
        drive.deviceId=source==0&&capture?Utf8(capture->Device()):"";
        drive.bass=levels.bass;drive.mid=levels.mid;drive.treble=levels.treble;
        if(!levels.available) {
            // WASAPI reopening is asynchronous. Its initial empty snapshot is not
            // a source failure: keep a paused burst until startup finishes. Bound
            // this grace period so a stalled endpoint cannot hold stale effects.
            if(resumeWaiting&&levels.error==0&&error==0&&std::chrono::steady_clock::now()<resumeDeadline)return drive;
            resumeWaiting=false;
            error=levels.error?levels.error:error;
            status=source==0?"system-unavailable":"video-unavailable";
            drive.active=false;drive.mappingCount=0;rules.Reset();drive.input=drive.level=drive.pulse=0;drive.largePhase=pulseAge=1e9;
            // Returning no drive restores baseline effects while a device recovers.
            // Preserve integrated motion to avoid resetting every dynamic phase.
            ResetDetection();return drive;
        }
        resumeWaiting=false;
        status=source==0?"system-listening":"video-listening";error=0;
        const float amplitudes[]={levels.level,levels.bass,levels.mid,levels.treble};
        const float compensation[]={1.0f,1.6f,1.4f,2.0f};
        const int selectedBand=std::clamp(fx.audioBand,0,3);
        const float raw=std::isfinite(amplitudes[selectedBand])?std::clamp(amplitudes[selectedBand],0.0f,1.0f):0;
        drive.input=raw;
        if(hadAdaptive!=levels.adaptive) {ResetOnset();hadAdaptive=levels.adaptive;}
        const bool firstLevel=!baselineReady;
        double dt=std::isfinite(previousTime)?frame.effectTime-previousTime:0;
        previousTime=frame.effectTime;drive.active=true;
        // Preview redraws can sample the same frame repeatedly while WASAPI keeps
        // producing new data. Do not consume an onset until video time advances.
        if(!std::isfinite(dt)||dt<=0)dt=0;
        else dt=std::min(dt,1.0);
        if(dt==0&&!firstLevel)return drive;
        rules.Step(levels,fx,dt,frame.effectTime,drive);
        if(levels.adaptive) {
            if(sharedEpoch!=levels.epoch) {sharedEpoch=levels.epoch;sharedOnset=levels.onsetSequence;pulseAge=1e9;}
            baselineReady=true;timeline+=dt;pulseAge=std::min(1e9,pulseAge+dt);drive.largePhase=std::min(1e9,drive.largePhase+dt);
            if(dt>0) {
                uint64_t begin=std::max(sharedOnset+1,levels.onsetSequence>levels.onsets.size()?levels.onsetSequence-levels.onsets.size()+1:1);
                for(uint64_t sequence=begin;sequence<=levels.onsetSequence;++sequence) {
                    const auto& event=levels.onsets[(sequence-1)%levels.onsets.size()];double age=std::max(0.0,levels.seconds-event.seconds);
                    if(event.sequence!=sequence||age>std::min(1.0,dt+.03)||event.strength[selectedBand]*std::sqrt(fx.audioSensitivity)<.25+.65*fx.audioBeatThreshold)continue;
                    pulseAge=age;++drive.beatCount;
                    if(fx.audioLegacy&&fx.audioGlitch&&fx.largeGlitch&&timeline-lastLarge>=fx.largeGlitchInterval) {lastLarge=timeline;drive.largePhase=age;++drive.burst;}
                }
                sharedOnset=levels.onsetSequence;
            }
            float target=std::clamp(raw*fx.audioSensitivity,0.0f,1.0f);
            double smoothing=target>drive.level?.018:std::max(.01,static_cast<double>(fx.audioSmoothing));
            drive.level+=static_cast<float>(1-std::exp(-dt/smoothing))*(target-drive.level);
            drive.pulse=pulseAge<=.065?1.0f:static_cast<float>(std::exp(-(pulseAge-.065)/.14));
            if(drive.pulse<.0001f)drive.pulse=0;
            drive.impact=drive.pulse;drive.motion+=(.25*drive.level+1.5*drive.pulse+3*drive.impact)*dt;
            return drive;
        }

        // Normalize to recent peaks rather than using an absolute loudness as the
        // beat threshold. The fixed floor and smooth gate cap quiet-input gain;
        // silence and low-level noise must never be normalized to full strength.
        float gate=std::clamp((raw-.0015f)/.0025f,0.0f,1.0f);
        gate=gate*gate*(3.0f-2.0f*gate);
        const float amplitude=raw*compensation[selectedBand];
        referencePeak=std::max(amplitude,referencePeak*static_cast<float>(std::exp(-dt/2.5)));
        const float energy=gate*amplitude/std::max(.010f,referencePeak)*fx.audioSensitivity;
        if(firstLevel) {
            energyAverage=previousEnergy=onsetPeak=valleyEnergy=energy;baselineReady=true;
            previousAmplitude=amplitude;
        }
        if(dt==0)return drive;
        timeline+=dt;
        const bool impactOnset=impact.Step(raw,dt);drive.impact=impact.value;
        if(drive.largePhase<1e8)drive.largePhase+=dt;
        if(pulseAge<1e8)pulseAge+=dt;
        const float target=1.0f-std::exp(-energy*1.3f);
        const double smoothing=target>drive.level?.018:std::max(.01,static_cast<double>(fx.audioSmoothing));
        drive.level+=static_cast<float>(1-std::exp(-dt/smoothing))*(target-drive.level);

        const float threshold=std::clamp(fx.audioBeatThreshold,.05f,.95f);
        const float contrast=.035f+threshold*.16f;
        const float minimumEnergy=.06f+threshold*.45f;
        valleyEnergy=std::min(valleyEnergy,energy);
        if(!armed) {
            onsetPeak=std::max(onsetPeak,energy);
            // A modest genuine fall is sufficient to arm the next beat. Requiring
            // a 28% drop prevented repeated onsets in compressed music.
            if(timeline-lastBeat>=.10&&(onsetPeak-energy>=contrast*.65f||energy<minimumEnergy*.6f))armed=true;
        }
        const float slope=static_cast<float>((energy-previousEnergy)/std::max(dt,.008));
        // Gain recovery by itself is not an attack: the physical source envelope
        // must rise as well as the normalized energy.
        const bool rising=amplitude>previousAmplitude+.00001f&&energy>previousEnergy+.003f&&slope>.20f+threshold*.4f;
        const bool prominent=energy>energyAverage+contrast||energy-valleyEnergy>contrast*1.35f;
        const bool onset=!firstLevel&&armed&&energy>=minimumEnergy&&prominent&&rising&&timeline-lastBeat>=.16;
        if(onset||impactOnset) {
            lastBeat=timeline;onsetPeak=valleyEnergy=energy;armed=false;pulseAge=0;++drive.beatCount;
            if(fx.audioLegacy&&fx.audioGlitch&&fx.largeGlitch&&fx.largeGlitchStrength>0&&fx.largeGlitchBlend>0&&
               (impactOnset||timeline-lastLarge>=fx.largeGlitchInterval)) {
                lastLarge=timeline;drive.largePhase=0;++drive.burst;
            }
        }
        // Hold a detected attack long enough to survive low video frame rates,
        // then release independently of the user's slower loudness smoothing.
        drive.pulse=pulseAge<=.065?1.0f:static_cast<float>(std::exp(-(pulseAge-.065)/.14));
        if(drive.pulse<.0001f)drive.pulse=0;
        drive.motion+=(.25*drive.level+1.5*drive.pulse+3*drive.impact)*dt;
        energyAverage+=static_cast<float>(1-std::exp(-dt/.32))*(energy-energyAverage);
        previousEnergy=energy;
        previousAmplitude=amplitude;
        return drive;
    }
};
void AudioReactive::SetDevice(const std::wstring& id) {if(impl_->captureDevice==id)return;impl_->captureDevice=id;if(impl_->capture)impl_->capture->SetDevice(id);}

AudioReactive::AudioReactive(const std::string& path,std::atomic_bool& stopped):impl_(std::make_unique<Impl>(path,stopped)) {}
AudioReactive::~AudioReactive()=default;
void AudioReactive::Configure(const VideoEffects& effects,bool compareOriginal,bool suspended) {impl_->Configure(effects,compareOriginal,suspended);}
void AudioReactive::ResetForPreset() {
    impl_->CloseSources();
    impl_->Reset();
    impl_->mode=Impl::Mode::Disabled;
    impl_->source=-1;
}
AudioDrive AudioReactive::Sample(const VideoFrame& frame,const VideoEffects& effects) {return impl_->Sample(frame,effects);}
AudioDrive AudioReactive::Snapshot() const {return impl_->drive;}
std::string AudioReactive::Status() const {return impl_->status;}
int32_t AudioReactive::ErrorCode() const {return impl_->error;}
