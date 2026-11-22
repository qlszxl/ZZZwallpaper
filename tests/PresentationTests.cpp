#include "../src/Windows/WallpaperHost/PresentationClock.h"
#include "../src/Windows/WallpaperHost/FramePacer.h"
#include "../src/Windows/WallpaperHost/VideoEffects.h"
#include <cassert>
#include <iostream>
int main() {
    using C=PresentationClock::Clock;
    auto start=C::time_point(std::chrono::seconds(100));
    PresentationClock clock(7,start);
    assert(clock.Tick(false,start+std::chrono::seconds(10))==7);
    assert(clock.Tick(true,start+std::chrono::seconds(11))==7);
    assert(clock.Tick(true,start+std::chrono::seconds(12))==8);
    assert(clock.Tick(false,start+std::chrono::seconds(13))==8);
    assert(clock.Tick(false,start+std::chrono::seconds(100))==8);
    assert(clock.Tick(true,start+std::chrono::seconds(101))==8);
    assert(clock.Tick(true,start+std::chrono::seconds(102))==9);
    assert(PresentationClock::Rate(0,true)==60);
    assert(PresentationClock::Rate(0,false)==0);
    assert(PresentationClock::Rate(0,true,120)==120); // Do not cap a high-rate video at 60.
    assert(PresentationClock::Rate(30,true,120)==30);
    for(int fps:{1,15,24,30,60,120,240})assert(PresentationClock::Rate(fps,true)==fps);
    FramePacer pacer;int draws=0;int repeats=0,previousSource=-1;
    for(int ms=0;ms<1000;ms++)if(pacer.Allow(60,start+std::chrono::milliseconds(ms))) {
        ++draws;int source=ms*24/1000;
        if(source==previousSource)++repeats;previousSource=source;
    }
    assert(draws==60&&repeats>=35);
    FramePacer limited;draws=0;
    for(int ms=0;ms<1000;ms++)if(limited.Allow(15,start+std::chrono::milliseconds(ms)))++draws;
    assert(draws>=15&&draws<=16); // The existing pacer permits <1 ms early presentation.
    VideoEffects fx;assert(!fx.NeedsAnimation());
    fx.brightness=.2f;assert(fx.Active()&&!fx.NeedsAnimation());
    fx.particles.enabled=true;assert(fx.NeedsAnimation());
    fx.particles.enabled=false;fx.subParticles.enabled=true;assert(fx.NeedsAnimation());
    fx.subParticles.enabled=false;fx.noise=true;fx.noiseStrength=.1f;assert(fx.NeedsAnimation());
    fx.intensity=0;assert(!fx.NeedsAnimation());
    fx={};fx.masks.push_back(EffectMaskOptions{});fx.maskPreview=0;
    assert(!fx.NeedsAnimation());fx.masks[0].shape=6;fx.masks[0].speed=.1f;assert(fx.NeedsAnimation());
    std::cout<<"PASS: effect clock pause/resume, 24-to-60 independent cadence, caps 1..240, static effects idle, both particle layers and mask animation\n";
}
