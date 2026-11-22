#pragma once
#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <string>

// Audio decoding and device I/O stay off the video rendering thread.
class VideoAudioOutput {
    struct Impl;
    std::unique_ptr<Impl> impl_;
public:
    using Status=std::function<void(const char*,int)>;
    VideoAudioOutput(std::string path,std::atomic_bool& stopped,std::function<bool()> permitted,Status status,
        std::function<float()> volume=[] {return 1.f;});
    ~VideoAudioOutput();
    void Update(double mediaSeconds,bool playing,uint64_t generation);
    // Media position consumed by the audio endpoint, excluding queued samples.
    std::optional<double> Position(uint64_t generation);
};
