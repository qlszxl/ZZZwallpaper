#include "../src/Windows/WallpaperHost/AudioRuleEngine.h"
#include "../src/Windows/WallpaperHost/AudioReactive.h"
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>

int main(int argc,char** argv) {
    int checks=0;
    auto check=[&](bool ok,const char* label){if(!ok)throw std::runtime_error(label);++checks;std::cout<<"PASS "<<label<<"\n";};
    if(argc!=2)throw std::runtime_error("Pass managed all-parameters wire fixture");
    std::ifstream file(argv[1]);std::stringstream text;text<<file.rdbuf();auto all=VideoEffects::Parse(text.str());
    check(all.audioRules.size()==74&&!all.audioLegacy,"Managed/native 74 parameter wire contract");
    AudioRuleEngine engine;AudioDrive drive;AudioLevels sound{.1f,.1f,.05f,0,true,0};
    engine.Step(sound,all,.02,1,drive);
    for(const auto& rule:all.audioRules) {
        auto mapping=drive.Mapping(rule.parameter);check(mapping&&std::isfinite(mapping->value),"Catalog native getter and mapping finite");
        auto candidate=all;SetAudioParameter(candidate,rule.parameter,rule.strong);
        check(std::abs(AudioParameterValue(candidate,rule.parameter)-rule.strong)<.001,"Catalog native setter matches parameter ID");
    }
    VideoEffects fx;fx.audioReactive=true;fx.audioLegacy=false;fx.glow=true;fx.glowStrength=.4f;fx.audioAmount=1;
    AudioRule rule;rule.parameter=static_cast<int>(AudioParameterId::GlowStrength);rule.weak=.1;rule.strong=1.2;rule.sensitivity=1;rule.attack=.05;rule.release=.1;
    fx.audioRules={rule};engine.Reset();engine.Step(sound,fx,.02,1,drive);
    check(std::abs(drive.mappings[0].value-1.2)<1e-6,"Full sound reaches strong endpoint");
    AudioLevels silence{0,0,0,0,true,0};
    for(int i=0;i<100;++i)engine.Step(silence,fx,.02,1+i*.02,drive);
    check(std::abs(drive.mappings[0].value-.1)<.001,"Valid silence settles to weak endpoint");
    fx.audioRules[0].weak=1.2;fx.audioRules[0].strong=.1;engine.Reset();engine.Step(sound,fx,.02,1,drive);
    check(std::abs(drive.mappings[0].value-.1)<1e-6,"Reverse mapping follows loud sound downward");
    fx.audioAmount=0;engine.Step(sound,fx,.02,2,drive);check(std::abs(drive.mappings[0].value-.4)<1e-6,"Zero influence preserves baseline");
    fx.audioAmount=.5;engine.Step(sound,fx,.02,2,drive);check(std::abs(drive.mappings[0].value-.25)<1e-6,"Partial influence blends baseline and endpoint");
    fx.audioAmount=1;fx.audioRules[0]=rule;fx.audioRules[0].enabled=0;engine.Step(sound,fx,.02,3,drive);
    check(std::abs(drive.mappings[0].value-.4)<1e-6&&!drive.mappings[0].enabled,"Disabled rule preserves baseline");
    fx.audioRules[0]=rule;fx.audioRules[0].band=3;engine.Reset();engine.Step(sound,fx,.02,1,drive);
    check(std::abs(drive.mappings[0].value-.1)<1e-6,"Independent treble ignores bass-only sound");
    fx.audioRules[0].band=1;engine.Reset();engine.Step(sound,fx,.02,1,drive);
    check(std::abs(drive.mappings[0].value-1.2)<1e-6,"Bass band responds independently");
    fx.audioRules[0]=rule;engine.Reset();AudioLevels noise{.0001f,.0001f,0,0,true,0};engine.Step(noise,fx,.02,1,drive);
    check(std::abs(drive.mappings[0].value-.1)<1e-6,"Quiet noise is not amplified to full scale");
    fx.audioRules[0].response=1;engine.Reset();engine.Step(silence,fx,.02,1,drive);engine.Step(sound,fx,.02,1.02,drive);
    check(drive.mappings[0].beats==1&&drive.mappings[0].signal>0,"Transient onset triggers independent beat");
    const double frozen=drive.mappings[0].signal;engine.Step(silence,fx,0,1.02,drive);
    check(drive.mappings[0].signal==frozen&&drive.mappings[0].beats==1,"Paused effect time freezes envelope without retriggering");
    for(int i=0;i<100;++i)engine.Step(sound,fx,.02,2+i*.02,drive);
    check(drive.mappings[0].beats==1&&drive.mappings[0].signal<.001,"Sustained tone releases without repeated fake beats");
    for(int i=0;i<30;++i)engine.Step(silence,fx,.02,5+i*.02,drive);engine.Step(sound,fx,.02,6,drive);
    check(drive.mappings[0].beats==2,"A fresh onset rearms the beat rule");
    AudioRule speed=rule;speed.parameter=static_cast<int>(AudioParameterId::ScanlineSpeed);speed.weak=1;speed.strong=4;
    fx.scanlines=true;fx.scanlineSpeed=1;fx.audioRules={rule,speed};engine.Reset();engine.Step(sound,fx,0,10,drive);
    double phase=drive.Mapping(speed.parameter)->phase;engine.Step(silence,fx,.02,10.02,drive);
    check(drive.Mapping(speed.parameter)->phase>phase&&drive.Mapping(speed.parameter)->phase-phase<=.081,"Speed integrates phase rather than multiplying absolute time");
    phase=drive.Mapping(speed.parameter)->phase;fx.audioRules.erase(fx.audioRules.begin());engine.Step(silence,fx,0,10.02,drive);
    check(drive.Mapping(speed.parameter)->phase==phase,"Deleting another rule retains speed phase");
    AudioRule zoom=rule;zoom.parameter=static_cast<int>(AudioParameterId::AudioZoomAmount);zoom.weak=0;zoom.strong=.3;fx.audioAmount=0;fx.audioRules={zoom};engine.Reset();engine.Step(sound,fx,.02,1,drive);
    check(drive.mappings[0].value==0,"Zero zoom influence preserves original framing");
    bool rejected=false;try {std::istringstream bad("1 999 1 0 0 0 1 1 .02 .1 .35");ReadAudioRules(bad);}catch(const std::exception&){rejected=true;}check(rejected,"Unknown native parameter rejected");
    rejected=false;try {std::istringstream bad("2 6 1 0 0 0 1 1 .02 .1 .35 6 1 0 0 0 1 1 .02 .1 .35");ReadAudioRules(bad);}catch(const std::exception&){rejected=true;}check(rejected,"Duplicate native parameter rejected");
    rejected=false;try {std::istringstream bad("1 6 1 0 0 0 4 1 .02 .1 .35");ReadAudioRules(bad);}catch(const std::exception&){rejected=true;}check(rejected,"Out of range native endpoint rejected");
    std::cout<<"Audio engine checks passed: "<<checks<<"\n";
}
