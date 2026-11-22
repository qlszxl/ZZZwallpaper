#pragma once
#include "HostOptions.h"
#include "HostProtocol.h"
#include <memory>

// A session owns playback; stdin only publishes into its control mailbox. No
// mutable process-global playback state is shared between these responsibilities.
class PlaybackSession {
    HostOptions options_;
    std::shared_ptr<PlaybackControlState> state_;
    std::shared_ptr<HostProtocol> protocol_;
public:
    PlaybackSession(HostOptions options,std::shared_ptr<HostProtocol> protocol);
    int Run();
};
