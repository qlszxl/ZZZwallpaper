#pragma once
#include <array>
#include <vector>
#include <string>
#include <cmath>
#include <istream>
#include <stdexcept>

struct EffectMaskOptions {
    int shape=0;float x=.5f,y=.5f,width=.6f,height=.6f,rotation=0,feather=.05f,corner=.2f;
    int invert=0;float speed=0,coverage=.45f,scale=2;std::string image;
};
struct AdvancedEffects {
    int blurSamples=65;
    int coloredNoise=0,rippleColorMode=0;unsigned rippleColor=0xFFFFFF;
    float rippleHueMin=0,rippleHueMax=360,rippleSatMin=.7f,rippleSatMax=1,rippleValueMin=.7f,rippleValueMax=1,rippleColorRate=1;
    int blurMethod=0;float blurDirection=0,blurCenterX=.5f,blurCenterY=.5f;
    int rainOutline=0;float rainOutlineStrength=.65f,rainOutlineWidth=2;unsigned rainColor=0xFFFFFF;
    int sunShape=0;float sunCurvature=0,sunArc=180,sunRadius=.4f,sunRepeat=1;
    std::vector<unsigned> sunColors{0xFF4040,0xFFAA33,0xFFFF55,0x55FF88,0x44BBFF,0x6655FF,0xDD66FF};
    float sunCenterX=.5f,sunCenterY=.5f,sunMoveX=0,sunMoveY=0,sunMoveSpeedX=.05f,sunMoveSpeedY=.05f,sunMovePhase=90,sunRotationSpeed=0,sunConeAngle=45;
    std::vector<double> sunPositions;
    int sunConeCorner=0;float sunConeDirection=45,sunConeLength=1,sunConeSwing=8;
    void ReadSunCone(std::istream& in) {
        if(!(in>>sunConeCorner>>sunConeDirection>>sunConeLength>>sunConeSwing)||sunConeCorner<0||sunConeCorner>3||
            !std::isfinite(sunConeDirection)||sunConeDirection<0||sunConeDirection>90||
            !std::isfinite(sunConeLength)||sunConeLength<.1f||sunConeLength>2||
            !std::isfinite(sunConeSwing)||sunConeSwing<0||sunConeSwing>45)throw std::runtime_error("Invalid corner light parameters");
    }
    void ReadSunAnimation(std::istream& in) {
        if(!(in>>sunCenterX>>sunCenterY>>sunMoveX>>sunMoveY>>sunMoveSpeedX>>sunMoveSpeedY>>sunMovePhase>>sunRotationSpeed>>sunConeAngle))throw std::runtime_error("Incomplete prism animation");
        auto range=[](float v,float a,float b) {if(!std::isfinite(v)||v<a||v>b)throw std::runtime_error("Prism animation out of range");};
        range(sunCenterX,0,1);range(sunCenterY,0,1);range(sunMoveX,0,1);range(sunMoveY,0,1);range(sunMoveSpeedX,0,2);range(sunMoveSpeedY,0,2);range(sunMovePhase,-180,180);range(sunRotationSpeed,-180,180);range(sunConeAngle,1,170);
        sunPositions.resize(sunColors.size());double previous=-1;
        for(auto& p:sunPositions) {if(!(in>>p)||!std::isfinite(p)||p<0||p>1||p<=previous)throw std::runtime_error("Invalid prism color positions");previous=p;}
    }
    std::vector<EffectMaskOptions> masks;
    std::array<int,25> effectMasks=[] {std::array<int,25> values;values.fill(-1);return values;}();
    int maskPreview=-1;
    void ReadAdvanced(std::istream& in,int version=26) {
        int colors,count;
        if(!(in>>coloredNoise>>rippleColorMode>>rippleColor>>rippleHueMin>>rippleHueMax>>rippleSatMin>>rippleSatMax>>rippleValueMin>>rippleValueMax>>rippleColorRate
            >>blurMethod>>blurDirection>>blurCenterX>>blurCenterY>>rainOutline>>rainOutlineStrength>>rainOutlineWidth>>rainColor
            >>sunShape>>sunCurvature>>sunArc>>sunRadius>>sunRepeat>>colors)||colors<2||colors>12)throw std::runtime_error("Invalid advanced effects");
        auto range=[](double v,double a,double b) {if(!std::isfinite(v)||v<a||v>b)throw std::runtime_error("Advanced effect out of range");};
        range(coloredNoise,0,1);range(rippleColorMode,0,3);range(rippleColor,0,0xFFFFFF);range(rippleHueMin,0,360);range(rippleHueMax,0,360);
        range(rippleSatMin,0,1);range(rippleSatMax,rippleSatMin,1);range(rippleValueMin,0,1);range(rippleValueMax,rippleValueMin,1);range(rippleColorRate,0,10);
        range(blurMethod,0,4);range(blurDirection,-180,180);range(blurCenterX,0,1);range(blurCenterY,0,1);range(rainOutline,0,1);range(rainOutlineStrength,0,1);range(rainOutlineWidth,.5,10);range(rainColor,0,0xFFFFFF);
        range(sunShape,0,5);range(sunCurvature,-2,2);range(sunArc,1,360);range(sunRadius,.05,1.5);range(sunRepeat,1,12);
        sunColors.resize(colors);for(auto& c:sunColors) {if(!(in>>c)||c>0xFFFFFF)throw std::runtime_error("Invalid prism palette");}
        if(!(in>>count)||count<0||count>16)throw std::runtime_error("Invalid mask count");masks.resize(count);
        for(auto& m:masks) {
            if(!(in>>m.shape>>m.x>>m.y>>m.width>>m.height>>m.rotation>>m.feather>>m.corner>>m.invert>>m.speed>>m.coverage>>m.scale>>m.image))throw std::runtime_error("Incomplete mask");
            range(m.shape,0,8);range(m.x,0,1);range(m.y,0,1);range(m.width,.01,2);range(m.height,.01,2);range(m.rotation,-180,180);range(m.feather,0,1);range(m.corner,0,1);range(m.invert,0,1);range(m.speed,-2,2);range(m.coverage,0,1);range(m.scale,.5,8);
            if(m.image!="-"&&(m.image.size()!=64||m.image.find_first_not_of("0123456789ABCDEF")!=std::string::npos))throw std::runtime_error("Invalid mask image identifier");
        }
        for(int i=0;i<(version>=36?25:version>=35?24:version>=27?23:21);++i) {auto& index=effectMasks[i];if(!(in>>index)||index<-1||index>=count||(i==21&&index!=-1))throw std::runtime_error("Invalid effect mask reference");}
        if(!(in>>maskPreview)||maskPreview<-1||maskPreview>=count)throw std::runtime_error("Invalid mask preview");
    }
};

// Matches the appended HLSL rows; all fields occupy complete float4 registers.
struct MaskConstants {float rect[4]{},params[4]{},dynamics[4]{};};
struct AdvancedConstants {
    float noiseColor=0,rippleColorMode=0,rippleHueMin=0,rippleHueSpan=1;
    float rippleColor[3]{1,1,1},rippleColorTick=0;
    float rippleSatMin=.7f,rippleSatMax=1,rippleValueMin=.7f,rippleValueMax=1;
    float blurMethod=0,blurDirection=0,blurCenterX=.5f,blurCenterY=.5f;
    float rainOutlineMix=0,rainOutlineWidth=2,rainPad1=0,rainPad2=0;
    float rainColor[3]{1,1,1},rainColorPad=0;
    float sunShape=0,sunCurvature=0,sunArc=3.14159265f,sunRadius=.4f;
    float sunColorCount=7,sunRepeat=1,maskPreview=0,filterMethod=0;
    float sunColors[12][4]{};
    MaskConstants masks[16];
    float maskIndices[7][4]{};
    float sunCenterX=.5f,sunCenterY=.5f,sunConeAngle=.78539816f,maskCount=0;
    float sunConeCorner=0,sunConeDirection=.78539816f,sunConeLength=1,sunConeSwing=.13962634f;
};
static_assert(sizeof(AdvancedConstants)==1232);
