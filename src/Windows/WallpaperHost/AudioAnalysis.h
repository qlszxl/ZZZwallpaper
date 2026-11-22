#pragma once
#include <cstddef>
#include <cstdint>
#include <vector>
#include <array>

// Linear RMS amplitudes; the mapping layer applies sensitivity and beat detection.
// Samples are analyzed in memory only and are never recorded or transmitted.
struct AudioOnset {uint64_t sequence=0;double seconds=0;std::array<float,4> strength{};};
struct AudioLevels {
    float level=0,bass=0,mid=0,treble=0;
    bool available=false;
    int32_t error=0;
    bool adaptive=false;
    uint64_t epoch=0,onsetSequence=0;
    double seconds=0;
    std::array<float,4> raw{};
    std::array<AudioOnset,64> onsets{};
};

struct AudioAnalysisFrame {AudioLevels levels{};std::array<float,256> bands{};};

class AudioMeter {
    struct Channel {float low=0,upper=0;};
    std::vector<Channel> channels_;
    float lowAlpha_=0,upperAlpha_=0,energyAlpha_=0;
    double power_=0,bassPower_=0,midPower_=0,treblePower_=0;
public:
    void Reset(int sampleRate,int channels);
    // nullptr represents frameCount silent frames, including WASAPI silent packets.
    void ProcessInterleaved(const float* samples,size_t frameCount);
    AudioLevels Levels() const;
};
