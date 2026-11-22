#include "../../src/Windows/WallpaperHost/VideoEffects.h"
#include <fstream>
#include <iostream>
#include <iterator>
#include <sstream>
#include <vector>

int main(int argc,char** argv) {
    try {
        if(argc!=5)return 2;
        auto read=[](const char* file){std::ifstream input(file);return std::string(std::istreambuf_iterator<char>(input),{});};
        auto fx=VideoEffects::Parse(read(argv[1]));
        auto check=[](bool condition){if(!condition)throw std::runtime_error("Native particle contract mismatch");};
        check(fx.effectOrder.size()==VisualEffectKinds.size());
        check(fx.particles.enabled&&fx.subParticles.enabled&&fx.particles.count==80&&fx.subParticles.count==23);
        check(fx.particles.randomMask==7&&fx.subParticles.randomMask==88);
        check(!fx.particles.randomRotation&&fx.subParticles.randomRotation);
        check(fx.particles.builtins=="-"&&fx.particles.builtins==fx.subParticles.builtins);
        check(std::abs(fx.particles.opacity-.4f)<.001f&&std::abs(fx.subParticles.opacity-.25f)<.001f);
        fx.particles.enabled=false;check(fx.Active());fx.subParticles.enabled=false;check(!fx.Active());
        auto old=VideoEffects::Parse(read(argv[2]));check(old.particles.enabled&&old.particles.style==6&&old.particles.count==91&&!old.subParticles.enabled);
        check(!old.particles.randomRotation);
        auto old24=VideoEffects::Parse(read(argv[3]));check(old24.particles.count==80&&old24.subParticles.count==23&&!old24.particles.randomRotation&&!old24.subParticles.randomRotation);
        auto parameterWire=read(argv[4]);
        auto missingRotation=parameterWire;missingRotation.erase(missingRotation.find_last_of(' '));
        auto invalidRotation=missingRotation+" 2";
        for(auto value:{missingRotation,invalidRotation}) {
            bool rejected=false;try{VideoEffects::Parse(value);}catch(const std::exception&){rejected=true;}check(rejected);
        }
        for(auto value:{std::string("garbage"),read(argv[1])+" extra",read(argv[1]).substr(0,read(argv[1]).size()-65)}){
            bool rejected=false;try{VideoEffects::Parse(value);}catch(const std::exception&){rejected=true;}check(rejected);
        }
        std::cout<<"Native particle wire checks passed\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
