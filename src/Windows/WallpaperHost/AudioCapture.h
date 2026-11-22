#pragma once
#include "AudioAnalysis.h"
#include <memory>
#include <functional>
#include <string>

// Captures the default playback device's mix through WASAPI loopback.
// Construction is idle; only SetEnabled(true) opens an audio endpoint.
class AudioCapture {
    struct Impl;
    std::unique_ptr<Impl> impl_;
public:
    explicit AudioCapture(bool spectrum=false);
    ~AudioCapture();
    AudioCapture(const AudioCapture&)=delete;
    AudioCapture& operator=(const AudioCapture&)=delete;
    void SetEnabled(bool enabled);
    void SetDevice(const std::wstring& id);
    std::wstring Device() const;
    AudioLevels Snapshot() const;
    AudioAnalysisFrame SnapshotFrame() const;
};
