#pragma once
#include <algorithm>
#include <cmath>
#include <istream>
#include <stdexcept>

struct LightFlareOptions {
    bool enabled=false,ghostsEnabled=false,animation=false;
    float strength=.7f,brightness=1,centerX=.5f,centerY=.5f,size=.2f;
    unsigned color=0xFFFFFF,haloColor=0x80BFFF;
    float coreSize=.08f,haloStrength=.65f,haloRadius=.6f,softness=.7f;
    int blendMode=0,rayCount=8,ghostCount=3;
    float rayLength=1,rayWidth=.025f,rotation=0,alternate=.5f;
    float ghostSpacing=.65f,ghostSize=.16f,ghostStrength=.25f,dispersion=.6f;
    float pulseAmount=.1f,pulsePeriod=4,rotationSpeed=3;
    bool Active() const {return enabled&&strength>0&&brightness>0;}
    bool Animated() const {return Active()&&animation&&(pulseAmount>0||((rayCount>0||(ghostsEnabled&&ghostStrength>0))&&rotationSpeed!=0));}
    void Read(std::istream& in) {
        int on=0,ghosts=0,motion=0;
        if(!(in>>on>>strength>>brightness>>centerX>>centerY>>size>>color>>haloColor
            >>coreSize>>haloStrength>>haloRadius>>softness>>blendMode>>rayCount>>rayLength>>rayWidth>>rotation>>alternate
            >>ghosts>>ghostCount>>ghostSpacing>>ghostSize>>ghostStrength>>dispersion>>motion>>pulseAmount>>pulsePeriod>>rotationSpeed))
            throw std::runtime_error("Incomplete light flare parameters");
        for(int flag:{on,ghosts,motion})if(flag<0||flag>1)throw std::runtime_error("Invalid light flare switch");
        auto range=[](float v,float a,float b) {if(!std::isfinite(v)||v<a||v>b)throw std::runtime_error("Light flare parameter out of range");};
        range(strength,0,1);range(brightness,0,2);range(centerX,0,1);range(centerY,0,1);range(size,.01f,.8f);
        if(color>0xFFFFFF||haloColor>0xFFFFFF)throw std::runtime_error("Invalid light flare color");
        range(coreSize,.01f,.3f);range(haloStrength,0,1);range(haloRadius,.1f,1.5f);range(softness,0,1);
        if(blendMode<0||blendMode>4||(rayCount!=0&&rayCount!=4&&rayCount!=6&&rayCount!=8&&rayCount!=12)||ghostCount<1||ghostCount>4)
            throw std::runtime_error("Invalid light flare mode/count");
        range(rayLength,.1f,2);range(rayWidth,.005f,.2f);range(rotation,-180,180);range(alternate,0,1);
        range(ghostSpacing,.1f,1.5f);range(ghostSize,.03f,.5f);range(ghostStrength,0,1);range(dispersion,0,1);
        range(pulseAmount,0,1);range(pulsePeriod,1,20);range(rotationSpeed,-30,30);
        enabled=on!=0;ghostsEnabled=ghosts!=0;animation=motion!=0;
    }
};

// Appended to the existing constant buffer; every row is exactly one float4.
struct LightFlareConstants {
    float centerX=.5f,centerY=.5f,radius=.1f,mix=0;
    float color[3]{1,1,1},brightness=1;
    float haloColor[3]{.5f,.75f,1},coreSize=.08f;
    float haloStrength=.65f,haloRadius=.6f,softness=.7f,rayAxes=0;
    float rayWidth=.025f,ghostCount=0,blendMode=0,bounds=1;
    float rays[6][4]{};
    float ghosts[4][4]{};
    float ghostColors[4][4]{};
};
static_assert(sizeof(LightFlareConstants)==304);

inline LightFlareConstants PrepareLightFlare(const LightFlareOptions& f,double time) {
    LightFlareConstants c;
    if(!f.Active())return c;
    c.centerX=f.centerX;c.centerY=f.centerY;c.radius=f.size*.5f;c.mix=f.strength;c.brightness=f.brightness;
    auto rgb=[](unsigned hex,float* p) {p[0]=((hex>>16)&255)/255.f;p[1]=((hex>>8)&255)/255.f;p[2]=(hex&255)/255.f;};
    rgb(f.color,c.color);rgb(f.haloColor,c.haloColor);
    constexpr double tau=6.283185307179586;
    double phase=f.animation?std::fmod(std::max(0.0,time)/f.pulsePeriod,1.0)*tau:0;
    if(f.animation)c.brightness*=1-f.pulseAmount*(.5f-.5f*static_cast<float>(std::cos(phase)));
    double rotation=f.rotation*tau/360+(f.animation?std::fmod(time*f.rotationSpeed/360,1.0)*tau:0);
    c.coreSize=f.coreSize;c.haloStrength=f.haloStrength;c.haloRadius=f.haloRadius;c.softness=f.softness;
    c.rayAxes=static_cast<float>(f.rayCount/2);c.rayWidth=f.rayWidth;c.blendMode=static_cast<float>(f.blendMode);
    c.bounds=std::max(f.coreSize*3,f.haloStrength>0?f.haloRadius:0.f);
    for(int i=0;i<f.rayCount/2;++i) {
        double a=rotation+tau*i/f.rayCount;
        c.rays[i][0]=static_cast<float>(std::cos(a));c.rays[i][1]=static_cast<float>(std::sin(a));
        c.rays[i][2]=f.rayLength*(i%2?1-.8f*f.alternate:1);
        c.rays[i][3]=i%2?1-.5f*f.alternate:1;
        c.bounds=std::max(c.bounds,c.rays[i][2]+f.rayWidth*3);
    }
    c.ghostCount=f.ghostsEnabled&&f.ghostStrength>0?static_cast<float>(f.ghostCount):0;
    constexpr unsigned palette[4]{0x80BFFF,0xFFC46B,0xB3FF73,0xAC87FF};
    for(int i=0;i<static_cast<int>(c.ghostCount);++i) {
        float offset=(i%2?-1.f:1.f)*(i/2+1)*f.ghostSpacing;
        auto& g=c.ghosts[i];g[0]=static_cast<float>(std::cos(rotation))*offset;g[1]=static_cast<float>(std::sin(rotation))*offset;
        g[2]=f.ghostSize*(1+.25f*i);g[3]=f.ghostStrength/(1+.3f*i);
        rgb(palette[i],c.ghostColors[i]);for(int j=0;j<3;++j)c.ghostColors[i][j]=c.color[j]+(c.ghostColors[i][j]-c.color[j])*f.dispersion;
        c.bounds=std::max(c.bounds,std::abs(offset)+g[2]);
    }
    return c;
}
