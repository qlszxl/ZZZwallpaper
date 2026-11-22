#pragma once
#include <array>
#include <algorithm>
#include <cmath>
#include <cstddef>

// One fixed rolling window per rule. No timers, queues of deferred work or
// allocations in the audio/render loop; only accepted events occupy a slot.
class AudioTriggerLimiter {
    std::array<double,100> times{};
    size_t head=0,count=0;
    double lastTime=0;
public:
    bool Accept(double now,double window,int maximum) {
        if(!std::isfinite(now))return false;
        if(now<lastTime){head=0;count=0;}
        lastTime=now;
        while(count>0&&now-times[head]>=window) {head=(head+1)%times.size();--count;}
        const auto limit=static_cast<size_t>(std::clamp(maximum,1,100));
        if(count>=limit)return false;
        times[(head+count)%times.size()]=now;++count;return true;
    }
};
