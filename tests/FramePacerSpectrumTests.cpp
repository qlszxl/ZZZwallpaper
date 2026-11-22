#include "../src/Windows/WallpaperHost/FramePacer.h"
#include "../src/Windows/WallpaperHost/SpectrumAnalysis.h"
#include <cassert>
#include <vector>

// Compile-only during this delivery. Pure deterministic cases for later execution.
int main() {
    using Clock=std::chrono::steady_clock;auto start=Clock::time_point(std::chrono::seconds(100));
    FramePacer cap;int presented=0;
    for(int i=0;i<300;i++)if(cap.Allow(15,start+std::chrono::microseconds((long long)i*1000000/30)))presented++;
    assert(presented==150); // 30 fps timestamps -> 15 fps, still ten seconds.
    assert(cap.Allow(15,start+std::chrono::seconds(20)));
    assert(!cap.Allow(15,start+std::chrono::seconds(20))); // no catch-up burst
    for(int i=0;i<60;i++)assert(cap.Allow(0,start+std::chrono::seconds(21)+std::chrono::microseconds(i*16667)));
    static_assert(SpectrumAnalysis::BandCount==256);
    SpectrumAnalysis spectrum;std::vector<float> stereo(8192*2);
    for(size_t i=0;i<8192;i++) {float v=(float)(.5*std::sin(6.283185307179586*1000*i/48000));stereo[i*2]=v;stereo[i*2+1]=-v;}
    spectrum.Feed(stereo.data(),8192,48000,2);auto bands=spectrum.Snapshot();
    auto peak=std::max_element(bands.begin(),bands.end());int position=(int)(peak-bands.begin());
    assert(bands.size()==256);
    int expected=(int)(SpectrumAnalysis::BandCount*std::log(1000.0/40)/std::log(16000.0/40));
    assert(std::abs(position-expected)<=3&&*peak>.1); // anti-phase content remains visible
    spectrum.Feed(nullptr,8192,48000,2);for(float value:spectrum.Snapshot())assert(value<.0001);
}
