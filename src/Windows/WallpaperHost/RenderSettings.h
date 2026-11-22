#pragma once
#include <cstdint>
#include "AdvancedEffects.h"
#include "Particles.h"
#include "LightFlare.h"
#include "TransformEffect.h"

// CPU layout shared by the legacy graph and isolated layers; matches PostProcess.h.
struct SatelliteConstants {float cx=0,cy=0,hx=0,hy=0,shiftX=0,shiftY=0,rgb=0,weight=0,hue=0,pad1=0,pad2=0,pad3=0;};
struct GlitchConstants {float strength=0,coverage=0,size=24,rgb=0;uint32_t tick=0,mode=0,seed=0;float hue=0,cx=.5f,cy=.5f,hx=.1f,hy=.05f;};
struct RenderSettings {float cx=1,cy=1,angle=0,intensity=1,brightness=0,contrast=1,saturation=1,vignette=0,glow=0,threshold=.75f,px=0,py=0,bx=0,by=0,rgb=0,soft=0,scan=0,density=170,noise=0,transfer=1,outputHeight=1080;uint32_t frameIndex=0;float scanPhase=0,dotPhase=0,glitch=0,coverage=0;uint32_t glitchTick=0;float dots=0,spacing=12,size=.8f,dotGlow=0,pulse=0,glitchRgb=0;uint32_t groupCount=0;float ripple=0,ripplePhase=0,rippleWidth=.12f,rippleCount=2,scanExposure=0,scanSweep=-1,rippleDots=0,rippleSpacing=12,rippleSize=.8f,dotShape=0,clusterBlend=.35f,bendAmplitude=0,bendSpan=240,bendDensity=.4f,blurMix=0,distortionStrength=0,distortionScale=4,distortionPhase=0,distortionCenterX=.5f,distortionCenterY=.5f;uint32_t distortionMode=0;float scanWidth=3.18f,scanSoftness=.6f,scanAngle=0,dotRotation=0,rippleRotation=0;uint32_t blurMotion=0,blurMotionMode=0;float blurMotionPhase=0,blurCoverage=.45f,blurScale=2,blurFeather=.45f,blurAngle=0,blurMaskPass=0,largeStrength=0,largeBlend=0,largeRgb=0,glitchHue=0,largeCenterX=.5f,largeCenterY=.5f,largeHalfX=.5f,largeHalfY=.175f,largeShiftX=0,largeShiftY=0;uint32_t largeTick=0,largeMode=0,largeSatelliteCount=0;float largeHue=0;uint32_t smallRandomHue=0;float rippleShape=0;GlitchConstants groups[6];SatelliteConstants largeSatellites[10];
    float zoomMix=0,zoomPhase=0,zoomScale=4,zoomCenterX=.5f;
    float zoomCenterY=.5f,rainMix=0,rainPhase=0,rainDensity=5;
    float rainRadius=.8f,rainWidth=.12f,waterMix=0,waterPhase=0;
    float waterAmplitude=12,waterFrequency=5,waterAngle=0,edgeMix=0;
    float edgeWidth=1.2f,edgeThreshold=.15f,edgeMode=0,pixelMix=0;
    float pixelBlock=16,sunMix=0,sunPhase=0,sunAngle=0;
    float sunWidth=.3f,sunPosition=.2f,extendedPad1=0,extendedPad2=0;
    float originalCropX=1,originalCropY=1,originalAngle=0,originalPadding=0;
    float sourceOffsetX=0,sourceOffsetY=0,originalOffsetX=0,originalOffsetY=0;
    AdvancedConstants advanced;
    float edgeR=1,edgeG=1,edgeB=1,edgeColorPadding=0;
    ParticleConstants particles;
    float inputScaleX=1,inputScaleY=1,inputBiasX=0,inputBiasY=0;
    float rasterOriginX=0,rasterOriginY=0,rasterPaddingX=0,rasterPaddingY=0;
    uint32_t blurSamples=65;float blurPadding[3]{};
    float blurWeights[65][4]{};
    LightFlareConstants lightFlare;
    TransformConstants transform;
};
static_assert(sizeof(RenderSettings)==3584+sizeof(ParticleConstants)+sizeof(LightFlareConstants)+sizeof(TransformConstants));
