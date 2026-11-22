#pragma once
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include "AudioMappingTypes.h"

struct VideoFrame;
struct VideoEffects;
struct AudioDrive {
    bool active=false;
    std::array<AudioMappedValue,128> mappings{};int mappingCount=0;
    AudioTriggerHistory particleTriggers;
    const AudioMappedValue* Mapping(int id) const {for(int i=0;i<mappingCount;++i)if(mappings[i].parameter==id)return &mappings[i];return nullptr;}
    double Phase(AudioParameterId id,double fallback) const {auto value=Mapping(static_cast<int>(id));return active&&value&&value->enabled?value->phase:fallback;}
    // input is the selected band's raw RMS, before gating or normalization.
    float input=0,level=0,pulse=0,impact=0;
    float bass=0,mid=0,treble=0;
    std::string deviceId;
    double motion=0,largePhase=1e9;
    uint64_t burst=0,beatCount=0;
};

// Source lifetime and rhythm tracking belong to the playback loop, not the shader.
class AudioReactive {
    struct Impl;
    std::unique_ptr<Impl> impl_;
public:
    AudioReactive(const std::string& path,std::atomic_bool& stopped);
    ~AudioReactive();
    AudioReactive(const AudioReactive&)=delete;
    AudioReactive& operator=(const AudioReactive&)=delete;
    void Configure(const VideoEffects& effects,bool compareOriginal,bool suspended);
    void ResetForPreset();
    AudioDrive Sample(const VideoFrame& frame,const VideoEffects& effects);
    AudioDrive Snapshot() const;
    std::string Status() const;
    int32_t ErrorCode() const;
    void SetDevice(const std::wstring& id);
};
