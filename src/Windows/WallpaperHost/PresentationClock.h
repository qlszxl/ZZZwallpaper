#pragma once
#include <chrono>
#include <algorithm>
#include <cmath>

// Effect time advances between decoded frames, but never through a pause.
class PresentationClock {
public:
    using Clock=std::chrono::steady_clock;
private:
    Clock::time_point last_;
    double time_;
    bool running_=false;
public:
    explicit PresentationClock(double initial,Clock::time_point now=Clock::now()):last_(now),time_(initial) {}
    double Tick(bool running,Clock::time_point now=Clock::now()) {
        if(running&&running_)time_+=std::max(0.0,std::chrono::duration<double>(now-last_).count());
        running_=running;last_=now;return time_;
    }
    double Time() const {return time_;}
    static int Rate(int limit,bool animated,double sourceRate=0) {
        if(limit>0)return std::clamp(limit,1,240);
        if(!animated)return 0;
        return std::isfinite(sourceRate)?static_cast<int>(std::clamp(std::ceil(sourceRate),60.0,240.0)):60;
    }
};
