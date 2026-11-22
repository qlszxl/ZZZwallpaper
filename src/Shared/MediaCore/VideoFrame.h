#pragma once
#include <cstdint>
#include <vector>
#include <memory>
struct AVFrame;

struct VideoFrame {
    int width=0,height=0;
    double pts=0, duration=1.0/30, aspect=1, rotation=0;
    double effectTime=0;
    bool looped=false;
    int transfer=1 /* AVCOL_TRC_BT709; stored as metadata, independent of FFmpeg headers */;
    uint32_t frameIndex=0;
    std::vector<unsigned char> bgra;
    // Pins the decoder's texture slice until it has been submitted to the GPU.
    std::shared_ptr<AVFrame> hardware;
    int colorSpace=2;
    bool fullRange=false;
};

