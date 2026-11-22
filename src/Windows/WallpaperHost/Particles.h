#pragma once
#include <cmath>
#include <istream>
#include <stdexcept>
#include <string>
#include <algorithm>

struct ParticleOptions {
    static constexpr unsigned BuiltinSize=200,BuiltinCount=7,FirstBuiltinStyle=8,LastStyle=FirstBuiltinStyle+BuiltinCount-1;
    bool enabled=false;int style=0,count=120,quality=1,fade=1,localRegion=0;
    float size=5,speed=35,direction=90,opacity=.8f,sway=.5f,rotation=20,twinkle=.25f;
    unsigned color=0xFFFFFF;float centerX=.5f,centerY=.5f,width=1,height=1;
    int emitter=0,emission=0;float lifetime=8,burstInterval=2,spread=30,gravity=0,sizeRandom=.3f,speedRandom=.2f;std::string image="-";
    unsigned randomMask=0;std::string builtins="-";
    bool randomRotation=true;
    void Read(std::istream& in,int version) {
        int on;
        if(!(in>>on>>style>>count>>size>>speed>>direction>>opacity>>color>>sway>>rotation>>twinkle>>fade>>quality>>localRegion>>centerX>>centerY>>width>>height))throw std::runtime_error("Incomplete particle parameters");
        auto range=[](double value,double min,double max) {if(!std::isfinite(value)||value<min||value>max)throw std::runtime_error("Invalid particle parameter");};
        range(on,0,1);range(style,0,version>=24?LastStyle:version>=23?7:5);range(count,1,2000);range(size,1,256);range(speed,0,600);range(direction,0,360);range(opacity,0,1);range(color,0,0xFFFFFF);
        range(sway,0,1);range(rotation,-180,180);range(twinkle,0,1);range(fade,0,1);range(quality,0,2);range(localRegion,0,1);range(centerX,0,1);range(centerY,0,1);range(width,.05,1);range(height,.05,1);enabled=on!=0&&style!=5;
        if(version>=23) {
            if(!(in>>emitter>>emission>>lifetime>>burstInterval>>spread>>gravity>>sizeRandom>>speedRandom>>image))throw std::runtime_error("Incomplete particle emitter");
            range(emitter,0,2);range(emission,0,1);range(lifetime,.2,60);range(burstInterval,0,60);range(spread,0,360);range(gravity,-300,300);range(sizeRandom,0,1);range(speedRandom,0,1);
            if(image!="-"&&(image.size()!=64||!std::all_of(image.begin(),image.end(),[](char c){return (c>='0'&&c<='9')||(c>='A'&&c<='F');})))throw std::runtime_error("Invalid particle image key");
        }
        if(version>=24) {
            if(!(in>>randomMask>>builtins))throw std::runtime_error("Incomplete particle appearance parameters");
            if(randomMask>=(1u<<(LastStyle+1))||(randomMask&(1u<<5)))throw std::runtime_error("Invalid particle appearance selection");
            if(builtins!="-"&&(builtins.size()!=64||!std::all_of(builtins.begin(),builtins.end(),[](char c){return (c>='0'&&c<='9')||(c>='A'&&c<='F');})))throw std::runtime_error("Invalid built-in particle key");
            enabled=on!=0&&(style!=5||randomMask!=0);
        }
        // Older wire formats retain their uniform signed rotation speed.
        randomRotation=false;
        if(version>=25) {
            int random;
            if(!(in>>random))throw std::runtime_error("Incomplete particle rotation parameters");
            range(random,0,1);randomRotation=random!=0;
        }
    }
};
struct ParticleConstants {
    float mix=0,style=0,count=120,size=5;
    float time=0,lifetime=8,speed=35,direction=0;
    float color[3]{1,1,1},spread=0;
    float centerX=.5f,centerY=.5f,width=1,height=1;
    float sway=.5f,rotation=0,twinkle=.25f,fade=1;
    float emission=0,emitter=0,gravity=0,sizeRandom=.3f;
    float speedRandom=.2f,burstInterval=2,customImage=0,randomRotation=1;
    float randomMask=0,builtinImage=0,seedOffset=0,padding=0;
};
static_assert(sizeof(ParticleConstants)==128);
