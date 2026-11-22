#pragma once
#include "VideoEffects.h"
inline double AudioParameterValue(const VideoEffects& fx,int parameter) {
    switch(parameter) {
#define AUDIO_GET(id,key,member,guard,min,max) case id:return fx.member;
        AUDIO_PARAMETERS(AUDIO_GET)
#undef AUDIO_GET
    }
    return 0;
}
inline void SetAudioParameter(VideoEffects& fx,int parameter,double value) {
    switch(parameter) {
#define AUDIO_SET(id,key,member,guard,min,max) case id:if(guard)fx.member=static_cast<decltype(fx.member)>(std::clamp(value,double(min),double(max)));break;
        AUDIO_PARAMETERS(AUDIO_SET)
#undef AUDIO_SET
    }
}
