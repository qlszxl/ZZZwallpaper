#pragma once
#include "VideoEffects.h"
#include "WallpaperPosition.h"
#include <atomic>
#include <mutex>
#include <optional>
#include <string>

struct EffectUpdate {VideoEffects effects;bool compare=false,fit=false,replaceAudioSession=false;uint64_t revision=0;};

// Mailbox shared by the command reader and the single playback owner. Only complete
// configurations cross this boundary; FFmpeg, D3D and HWND resources stay on their owner.
class PlaybackControlState {
    std::mutex effectsMutex_,audioDeviceMutex_;
    std::optional<EffectUpdate> pendingEffects_;
    std::wstring audioDevice_;
public:
    std::atomic_uint64_t position{PackWallpaperPosition(0,0)};
    std::atomic_int fpsLimit{0},decoderMode{0},soundVolume{100};
    std::atomic_bool soundEnabled{false},stopped{false},paused{false},show{false},contain{false},monitor{false},looping{true},restartRequested{false};
    void Configure(EffectUpdate update) {
        std::lock_guard guard(effectsMutex_);
        if(pendingEffects_ && pendingEffects_->replaceAudioSession)update.replaceAudioSession=true;
        pendingEffects_=std::move(update);
    }
    std::optional<EffectUpdate> TakeConfiguration() {std::lock_guard guard(effectsMutex_);std::optional<EffectUpdate> update;update.swap(pendingEffects_);return update;}
    void SetAudioDevice(std::wstring id) {std::lock_guard guard(audioDeviceMutex_);audioDevice_=std::move(id);}
    std::wstring AudioDevice() {std::lock_guard guard(audioDeviceMutex_);return audioDevice_;}
};
