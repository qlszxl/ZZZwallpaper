#pragma once
#include "AudioAnalysis.h"
#include "AdaptiveAudioAnalysis.h"
#include "AudioProcessingConfig.h"
#include <memory>
#include <atomic>
#include <cstdint>
#include <string>
#include <vector>

struct AVFormatContext;
struct AVCodecContext;
struct AVFrame;
struct AVPacket;

// A silent, independently seekable reader. Sample accepts the displayed video's
// raw container PTS in seconds (VideoFrame::pts), not its continuous effect clock.
// All methods belong to the playback thread; stopped may be set by another thread.
class VideoAudioReader {
    AVFormatContext* format_=nullptr;
    AVCodecContext* codec_=nullptr;
    AVFrame* frame_=nullptr;
    AVPacket* packet_=nullptr;
    std::atomic_bool* stopped_=nullptr;
    std::atomic<int64_t> deadline_{0};
    std::unique_ptr<AdaptiveAudioAnalysis> meter_;
    AudioProcessingConfig analysisConfig_;
    AudioLevels levels_{};
    std::vector<float> samples_;
    int stream_=-1,sampleRate_=0,channels_=0,frameOffset_=0;
    bool packetPending_=false,frameReady_=false,draining_=false,eof_=false;
    bool sampled_=false;
    double streamStart_=0,frameStart_=0,nextFrameTime_=0,cursor_=0,lastTarget_=0;

    enum class DecodeResult { Frame, Yield, End, Failed };
    static int Interrupt(void* opaque);
    void Fail(int error) noexcept;
    bool Seek(double target);
    DecodeResult Decode(int& operations,int64_t workDeadline);
    bool AcceptFrame();
    void Convert(int count);
    void SilenceUntil(double target);
public:
    VideoAudioReader()=default;
    ~VideoAudioReader() { Close(); }
    VideoAudioReader(const VideoAudioReader&)=delete;
    VideoAudioReader& operator=(const VideoAudioReader&)=delete;
    void Open(const std::string& path,std::atomic_bool& stopped) noexcept;
    AudioLevels Sample(double mediaSeconds) noexcept;
    void Close() noexcept;
};
