#pragma once
#include "PlaybackControlState.h"
#include <istream>
#include <memory>
#include <mutex>
#include <ostream>

// The existing stdin commands and stdout JSON events are the external contract.
// Parsing is independently testable; transport and playback have separate owners.
class HostProtocol {
    std::ostream& output_;
    std::mutex outputMutex_;
public:
    explicit HostProtocol(std::ostream& output):output_(output) {}
    void Report(const std::string& json);
    bool ApplyCommand(const std::string& command,PlaybackControlState& state);
    void ReadCommands(std::istream& input,PlaybackControlState& state);
};
