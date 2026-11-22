#pragma once
#include "PosterFrame.h"
#include "DecodeResources.h"
#include <iomanip>
#include <sstream>
#include <numeric>

inline std::string PosterFrameJson(const MediaSession& media,const VideoFrame& frame) {
    auto q=MeasurePosterFrame(frame);std::ostringstream j;j.imbue(std::locale::classic());j<<std::setprecision(17);
    j<<"{\"ok\":true,\"seconds\":"<<std::max(0.0,frame.pts-media.startTime)<<",\"duration\":"<<media.duration
     <<",\"frameDuration\":"<<frame.duration<<",\"sourceFps\":"<<media.sourceFps
     <<",\"width\":"<<frame.width<<",\"height\":"<<frame.height<<",\"sharpness\":"<<q.sharpness<<"}";
    return j.str();
}
inline double ProcessCpuSeconds() {
    FILETIME created{},exited{},kernel{},user{};if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return 0;
    ULARGE_INTEGER k{},u{};k.LowPart=kernel.dwLowDateTime;k.HighPart=kernel.dwHighDateTime;u.LowPart=user.dwLowDateTime;u.HighPart=user.dwHighDateTime;
    return (k.QuadPart+u.QuadPart)/1e7;
}
inline std::string BenchmarkMedia(MediaSession& media,VideoFrame& frame,int seconds,int fps) {
    using Clock=std::chrono::steady_clock;
    // Warm up the decoder and conversion buffers outside the measured interval.
    for(int i=0;i<8;++i)if(!media.Next(frame,false,fps))break;
    DecodeResources resources;
    double first=frame.effectTime,cpu=ProcessCpuSeconds();auto start=Clock::now();
    resources.Start();
    uint64_t decoded=media.decodedFrames,selected=media.selectedFrames;std::vector<double> timings;
    while(Clock::now()-start<std::chrono::seconds(seconds)) {
        auto before=Clock::now();if(!media.Next(frame,true,fps))break;
        timings.push_back(std::chrono::duration<double,std::milli>(Clock::now()-before).count());
    }
    double elapsed=std::chrono::duration<double>(Clock::now()-start).count();
    double cpuPercent=std::clamp((ProcessCpuSeconds()-cpu)/std::max(.001,elapsed)/std::max(1u,std::thread::hardware_concurrency())*100,0.0,100.0);
    resources.Stop();
    double total=std::accumulate(timings.begin(),timings.end(),0.0);std::sort(timings.begin(),timings.end());
    std::ostringstream j;j.imbue(std::locale::classic());j<<std::setprecision(8);
    j<<"{\"ok\":true,\"decoder\":"<<JsonString(media.decoderName)<<",\"fpsLimit\":"<<fps<<",\"sourceFps\":"<<media.sourceFps
     <<",\"decodedFrames\":"<<media.decodedFrames-decoded<<",\"selectedFrames\":"<<media.selectedFrames-selected
     <<",\"elapsedSeconds\":"<<elapsed<<",\"mediaSeconds\":"<<frame.effectTime-first
     <<",\"speed\":"<<(elapsed>0?(frame.effectTime-first)/elapsed:0)<<",\"selectedFps\":"<<(elapsed>0?timings.size()/elapsed:0)
     <<",\"meanMs\":"<<(timings.empty()?0:total/timings.size())<<",\"p95Ms\":"<<(timings.empty()?0:timings[timings.size()*95/100])
     <<",\"cpuPercent\":"<<cpuPercent;
    resources.Write(j);
    j<<",\"resourceScope\":\"benchmark process only; GPU busiest engine averaged over valid intervals; sampled memory averages and peaks; unavailable counters are null\""
     <<",\"scope\":\"decode, readback and conversion; excludes rendering, effects, audio and presentation\"}";
    return j.str();
}
