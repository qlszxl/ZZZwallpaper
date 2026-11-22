#pragma once
#include "HostProtocol.h"
#include "Renderer.h"
#include "../../Shared/MediaCore/MediaSession.h"
#include <chrono>
#include <optional>

struct PerformanceInterval {
    using Clock=std::chrono::steady_clock;
    Clock::time_point started=Clock::now();
    uint64_t frames=0,decodes=0,audioCalls=0;
    double decodeMs=0,audioMs=0,submitMs=0,presentMs=0;
    Clock::time_point lastPresent{};
    std::vector<double> intervals;
    void Presented() {
        auto now=Clock::now();if(lastPresent!=Clock::time_point{}&&intervals.size()<4096)intervals.push_back(std::chrono::duration<double,std::milli>(now-lastPresent).count());lastPresent=now;
    }
    void Reset() {*this=PerformanceInterval{};}
};

class PlaybackDiagnostics {
    HostProtocol& protocol;
    Renderer& renderer;
        PerformanceInterval performance;bool monitoring=false,performanceSuspended=true;
        uint64_t lastDecoded=0,lastLate=0,lastRate=0;
        std::string lastAudioStatus;int32_t lastAudioError=0;int lastAudioSource=-1;
        std::chrono::steady_clock::time_point lastAudioLevels{};

public:
    PlaybackDiagnostics(HostProtocol& output,Renderer& rendering):protocol(output),renderer(rendering) {}
    void Sync(bool enabled,bool suspended,const MediaSession& media);
    bool Measuring() const {return monitoring&&!performanceSuspended;}
    void AudioMeasured(double ms) {performance.audioMs+=ms;++performance.audioCalls;}
    void DecodeMeasured(double ms) {performance.decodeMs+=ms;++performance.decodes;}
    void Presented() {++performance.frames;performance.Presented();performance.submitMs+=renderer.LastSubmitMs;performance.presentMs+=renderer.LastPresentMs;}
    void ReportPerformance(const MediaSession& media,const VideoFrame& frame,int w,int h,bool stillImage,bool paused,const std::optional<double>& telemetryAudioPosition);
    void ReportAudio(const AudioReactive& audio,const VideoEffects& effects);
};
