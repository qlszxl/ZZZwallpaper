#pragma once
#include <chrono>
#include <algorithm>

// One presentation per time slot. Decode/PTS clocks continue at source speed.
class FramePacer {
    using Clock=std::chrono::steady_clock;
    Clock::time_point next_{};
    int limit_=0;
public:
    bool Allow(int limit,Clock::time_point now=Clock::now()) {
        limit=std::clamp(limit,0,240);
        if(limit!=limit_) {limit_=limit;next_=now;}
        if(limit==0)return true;
        auto period=std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>(1.0/limit));
        // Source timestamps and the 1 ms message-pump wait have finite precision.
        // Avoid a 30->15 conversion oscillating between 1/2/3 source-frame gaps.
        auto tolerance=std::min(std::chrono::duration_cast<Clock::duration>(std::chrono::milliseconds(1)),period/50);
        if(now+tolerance<next_)return false;
        // Advance whole slots when late; never burst to recover skipped slots.
        if(next_==Clock::time_point{})next_=now;
        next_+=period*((now-next_)/period+1);
        return true;
    }
};
