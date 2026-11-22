#include "../src/Windows/WallpaperHost/LargeGlitchVariation.h"
// Compile-time regression checks; this file has no executable entry point.
static_assert(GlitchVary(50,.2f,0,0,100)==40);
static_assert(GlitchVary(50,.2f,1,0,100)>59.999f&&GlitchVary(50,.2f,1,0,100)<60.001f);
static_assert(GlitchVary(50,0,.8f,0,100)==50);
static_assert(GlitchVary(0,1,1,0,100)==0);
static_assert(GlitchVary(100,1,1,0,100)==100);
static_assert(GlitchVary(20,1,0,20,60)==20);
static_assert(GlitchVariationHash(17027u)==GlitchVariationHash(17027u));
static_assert(GlitchVariationHash(17027u)!=GlitchVariationHash(19000u));

// An enabled audio mapping must not gate periodic bursts on beat detection.
static_assert(ChooseGlitchBurst(.1,.25,true,0,1e9,.25)==GlitchBurstSource::Timed);
static_assert(ChooseGlitchBurst(.1,.25,true,7,5,.25)==GlitchBurstSource::Timed);
static_assert(ChooseGlitchBurst(2,.25,true,7,.1,.25)==GlitchBurstSource::Audio);
static_assert(ChooseGlitchBurst(.05,.25,true,7,.1,.25)==GlitchBurstSource::Timed);
static_assert(ChooseGlitchBurst(.2,.25,true,7,.1,.25)==GlitchBurstSource::Audio);
static_assert(ChooseGlitchBurst(2,.25,false,7,.1,.25)==GlitchBurstSource::None);
static_assert(ChooseGlitchBurst(2,.25,true,7,2,.25)==GlitchBurstSource::None);
static_assert(ChooseGlitchBurst(.1,.25,true,7,-.1,.25)==GlitchBurstSource::Timed);
static_assert(ChooseGlitchBurst(.2,.25,true,7,.2,.1)==GlitchBurstSource::Timed);
static_assert(ChooseGlitchBurst(.3,.1,true,7,.3,.4)==GlitchBurstSource::Audio);
