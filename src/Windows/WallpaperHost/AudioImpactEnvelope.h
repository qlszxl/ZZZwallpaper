#pragma once
#include <algorithm>

// Detect contrast in physical RMS, independently of the beat detector's auto gain.
// A long quiet lead-in is required; continuous loud audio and device startup do not trigger.
struct AudioImpactEnvelope {
    float baseline=0,peak=0,previous=0,value=0;
    double quiet=0,cooldown=0,age=10;
    bool ready=false;
    constexpr void Reset() {*this={};}
    constexpr bool Step(float rms,double dt) {
        if(!(rms>=0&&rms<=1)||!(dt>0&&dt<=1))return false;
        if(!ready) {baseline=peak=previous=rms;ready=true;return false;}
        cooldown=std::max(0.0,cooldown-dt);age+=dt;
        const bool hit=quiet>=1.25&&cooldown==0&&rms>std::max(.04f,baseline*2.8f)&&rms>previous+.015f;
        if(hit) {age=0;cooldown=2.5;quiet=0;}
        else if(rms<=std::max(.035f,peak*.32f))quiet+=dt;
        else quiet=0;
        peak=std::max(rms,peak*static_cast<float>(1-dt/12));
        baseline+=(rms-baseline)*static_cast<float>(dt/(1.5+dt));previous=rms;
        value=age<.12?1:static_cast<float>(std::max(0.0,1-(age-.12)/.78));
        return hit;
    }
};
constexpr bool ImpactRegression() {
    AudioImpactEnvelope a;
    for(int i=0;i<180;i++)if(a.Step(.005f,1.0/60))return false;
    if(!a.Step(.25f,1.0/60)||a.value!=1)return false;
    for(int i=0;i<300;i++)if(a.Step(.25f,1.0/60))return false;
    if(a.value!=0)return false;
    a.Reset();for(int i=0;i<180;i++)if(a.Step(.4f,1.0/60))return false;
    a.Reset();for(int i=0;i<180;i++)if(a.Step(i%2?.003f:.006f,1.0/60))return false;
    return !a.Step(.02f,1.0/60);
}
static_assert(ImpactRegression(),"Quiet-to-loud impact regression");
