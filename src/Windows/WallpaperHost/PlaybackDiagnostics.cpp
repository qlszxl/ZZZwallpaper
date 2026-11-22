#include "PlaybackDiagnostics.h"
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <locale>
#include <sstream>

void PlaybackDiagnostics::Sync(bool enabled,bool suspended,const MediaSession& media) {
            if(enabled!=monitoring||suspended!=performanceSuspended) {
                monitoring=enabled;performanceSuspended=suspended;performance.Reset();
                lastDecoded=media.decodedFrames;lastLate=media.lateSkippedFrames;lastRate=media.rateSkippedFrames;
                renderer.SetPerformanceEnabled(enabled&&!suspended);
            }
 }
void PlaybackDiagnostics::ReportPerformance(const MediaSession& media,const VideoFrame& frame,int w,int h,bool stillImage,bool paused,const std::optional<double>& telemetryAudioPosition) {
            if(!monitoring)return;
            double elapsed=std::chrono::duration<double>(PerformanceInterval::Clock::now()-performance.started).count();
            if(elapsed<1)return;
            auto gpu=renderer.TakeGpuTimings();
            bool active=!performanceSuspended;bool gpuValid=active&&gpu.samples>0;
            std::ostringstream json;json.imbue(std::locale::classic());json<<std::fixed<<std::setprecision(4);
            json<<"{\"event\":\"performance\",\"fps\":"<<(active?performance.frames/elapsed:0)
                <<",\"frames\":"<<(active?performance.frames:0);
            auto intervals=performance.intervals;std::sort(intervals.begin(),intervals.end());
            json<<",\"windowSeconds\":"<<elapsed<<",\"intervalSamples\":"<<intervals.size()<<",\"frameIntervalP95Ms\":";
            if(intervals.empty())json<<"null";else json<<intervals[static_cast<size_t>(std::ceil(intervals.size()*.95))-1];
            json<<",\"longestIntervalMs\":";if(intervals.empty())json<<"null";else json<<intervals.back();
            json<<",\"decodedFrames\":"<<(media.decodedFrames>=lastDecoded?media.decodedFrames-lastDecoded:media.decodedFrames)
                <<",\"lateSkippedFrames\":"<<(media.lateSkippedFrames-lastLate)<<",\"rateSkippedFrames\":"<<(media.rateSkippedFrames-lastRate)
                <<",\"videoPtsSeconds\":";if(stillImage)json<<"null";else json<<frame.pts;
            json<<",\"audioPtsSeconds\":";if(telemetryAudioPosition&&active)json<<*telemetryAudioPosition;else json<<"null";
            json<<",\"avOffsetSeconds\":";if(telemetryAudioPosition&&active&&!stillImage)json<<frame.pts-*telemetryAudioPosition;else json<<"null";
            lastDecoded=media.decodedFrames;lastLate=media.lateSkippedFrames;lastRate=media.rateSkippedFrames;
            auto average=[&](const char* key,double sum,uint64_t samples) {
                json<<",\""<<key<<"\":";if(active&&samples)json<<sum/samples;else json<<"null";
            };
            average("decodeMs",performance.decodeMs,performance.decodes);
            average("audioMs",performance.audioMs,performance.audioCalls);
            average("submitMs",performance.submitMs,performance.frames);
            average("presentMs",performance.presentMs,performance.frames);
            auto gpuValue=[&](const char* key,double value) {json<<",\""<<key<<"\":";if(gpuValid)json<<value;else json<<"null";};
            gpuValue("gpuMs",gpu.totalMs);gpuValue("gpuBaseMs",gpu.baseMs);
            gpuValue("gpuGlowMs",gpu.glowMs);gpuValue("gpuBlurMs",gpu.blurMs);
            json<<",\"gpuValid\":"<<(gpuValid?"true":"false")<<",\"gpuSamples\":"<<(gpuValid?gpu.samples:0)
                <<",\"passes\":"<<(active?renderer.LastPasses:0)<<",\"width\":"<<w<<",\"height\":"<<h
                <<",\"videoWidth\":"<<frame.width<<",\"videoHeight\":"<<frame.height
                <<",\"decoder\":"<<JsonString(media.decoderName)<<",\"paused\":"<<(paused?"true":"false")<<"}";
            protocol.Report(json.str());auto lastPresent=performance.lastPresent;performance.Reset();performance.lastPresent=lastPresent;
 }
void PlaybackDiagnostics::ReportAudio(const AudioReactive& audio,const VideoEffects& effects) {
            auto status=audio.Status();auto error=audio.ErrorCode();
            bool changed=status!=lastAudioStatus||error!=lastAudioError||effects.audioSource!=lastAudioSource;
            if(changed) {
                lastAudioStatus=status;lastAudioError=error;lastAudioSource=effects.audioSource;
                protocol.Report("{\"event\":\"audio-status\",\"source\":"+std::to_string(effects.audioSource)+",\"status\":"+JsonString(status)+",\"code\":"+std::to_string(error)+"}");
            }
            auto now=std::chrono::steady_clock::now();
            if(changed||(effects.audioReactive&&now-lastAudioLevels>=std::chrono::milliseconds(100))) {
                lastAudioLevels=now;auto drive=audio.Snapshot();
                bool responding=drive.active&&(status=="system-listening"||status=="video-listening");
                std::string rules="[";
                if(responding)for(int i=0;i<drive.mappingCount;++i) {const auto& r=drive.mappings[i];if(i)rules+=",";rules+="{\"id\":"+std::to_string(r.parameter)+",\"value\":"+std::to_string(r.value)+",\"signal\":"+std::to_string(r.enabled?r.signal:0)+"}";}
                rules+="]";
                protocol.Report("{\"event\":\"audio-level\",\"input\":"+std::to_string(responding?drive.input:0)+
                    ",\"level\":"+std::to_string(responding?drive.level:0)+",\"pulse\":"+std::to_string(responding?drive.pulse:0)+
                    ",\"beats\":"+std::to_string(drive.beatCount)+",\"active\":"+(responding?"true":"false")+
                    ",\"bass\":"+std::to_string(drive.bass)+",\"mid\":"+std::to_string(drive.mid)+",\"treble\":"+std::to_string(drive.treble)+",\"deviceId\":"+JsonString(drive.deviceId)+",\"rules\":"+rules+"}");
            }
 }
