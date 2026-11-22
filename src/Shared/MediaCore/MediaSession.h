#pragma once
#include "VideoFrame.h"
#include "MediaUtilities.h"
#include <atomic>
#include <string>
#include <vector>
#include <stdexcept>
struct ID3D11Device;
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
#include <libavutil/display.h>
#include <libavutil/imgutils.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>
#include <libavfilter/avfilter.h>
#include <libavfilter/buffersink.h>
#include <libavfilter/buffersrc.h>
}

struct MediaDecodeLimits {
    int threads=0,maxDimension=0;
    int64_t maxPixels=0;
    bool exactGifTiming=false;
    int64_t CodecBufferPixels() const {
        if(maxPixels<=0||maxDimension<=0)return maxPixels;
        // FFmpeg may align its private buffers (e.g. MPEG-4 600 -> 640).
        // Keep that allocation allowance separate from exact source/frame
        // limits, capped by 64-pixel alignment in both axes.
        const int64_t aligned=(int64_t(maxDimension)+63)/64*64;
        const int64_t square=aligned*aligned,padded=maxPixels+126ll*maxDimension+63ll*63;
        return square<padded?square:padded;
    }
};
class MediaSession {
    AVFormatContext* format=nullptr;
    AVCodecContext* codec=nullptr;
    AVBufferRef* hardware=nullptr;
    AVFrame *decoded=nullptr,*downloaded=nullptr,*filtered=nullptr;
    AVPacket* packet=nullptr;
    SwsContext* scaler=nullptr,*alphaScaler=nullptr;
    AVFilterGraph* filterGraph=nullptr;
    AVFilterContext *filterSource=nullptr,*filterSink=nullptr;
    int stream=-1;
    int maximumFrameDimension=0;
    MediaDecodeLimits limits;
    bool draining=false, nextLoop=false, useHardware=false;
    bool exportHardwareFrames=false;
    void ConvertPixels(AVFrame* frame,VideoFrame& output);
    int selectionRate=0;
    double nextSelectionTime=0;
    std::atomic_bool& cancelled;
    std::atomic<long long> deadline{0};
    void Close();
    AVFrame* Deinterlace(AVFrame* input);
    static int Interrupt(void* opaque);
    static AVPixelFormat SelectFormat(AVCodecContext*,const AVPixelFormat* formats);
public:
    double duration=0, rotation=0, startTime=0, sourceFps=0;
    uint64_t decodedFrames=0, selectedFrames=0;
    uint64_t lateSkippedFrames=0,rateSkippedFrames=0;
    int width=0,height=0;
    bool hasAudio=false;
    std::string codecName,color,decoderName;
    std::string metadataJson="{}";
    MediaSession(std::atomic_bool& cancel):cancelled(cancel) {}
    ~MediaSession() { Close(); }
    void Open(const std::string& file,bool software=false,int maxFrameDimension=0,MediaDecodeLimits sourceLimits={},ID3D11Device* sharedDevice=nullptr);
    ID3D11Device* HardwareDevice() const;
    void SetGpuFrames(bool enabled) {exportHardwareFrames=enabled;}
    void Materialize(VideoFrame& frame);
    // Reference decoding continues; unwanted progressive frames are released before readback/conversion.
    bool Next(VideoFrame& frame,bool allowLoop=true,int fps=0,double minimumEffectTime=-1,double minimumSourceSeconds=-1);
    void Seek(double relativeSeconds);
    bool Hardware() const { return useHardware; }
};
