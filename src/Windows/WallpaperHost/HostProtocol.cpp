#include "HostProtocol.h"
#include "../../Shared/MediaCore/MediaUtilities.h"
#include <sstream>
#include <utility>

void HostProtocol::Report(const std::string& json) {std::lock_guard guard(outputMutex_);output_<<json<<std::endl;}
bool HostProtocol::ApplyCommand(const std::string& command,PlaybackControlState& state) {
        if(command=="stop") {state.stopped=true;return false;}
        if(command=="pause") state.paused=true;
        if(command=="resume") state.paused=false;
        if(command=="show") state.show=true;
        if(command=="contain") state.contain=true;
        if(command=="cover") state.contain=false;
        if(command.rfind("position ",0)==0) {
            std::istringstream input(command.substr(9));int x,y,scale=100;std::string extra;
            if(input>>x>>y) {
                input>>std::ws;bool valid=input.eof();
                if(!valid)valid=bool(input>>scale)&&!(input>>extra);
                if(valid&&x>=-100&&x<=100&&y>=-100&&y<=100&&scale>=10&&scale<=400)state.position=PackWallpaperPosition(x,y,scale);
            }
        }
        if(command=="monitor 1") state.monitor=true;
        if(command=="monitor 0") state.monitor=false;
        if(command=="loop 1") state.looping=true;
        if(command=="loop 0") state.looping=false;
        if(command=="restart") state.restartRequested=true;
        if(command=="sound 1") state.soundEnabled=true;
        if(command=="sound 0") state.soundEnabled=false;
        if(command.rfind("sound-volume ",0)==0) {
            try {
                const auto text=command.substr(13);size_t used=0;int value=std::stoi(text,&used);
                if(used==text.size()&&value>=0&&value<=100)state.soundVolume=value;
            } catch(...) { }
        }
        if(command.rfind("audio-device ",0)==0) {
            auto id=command.substr(13);if(id.size()<=1024&&id.find_first_of("\r\n")==std::string::npos) {
                std::wstring wide(id.begin(),id.end());state.SetAudioDevice(id=="default"?L"":wide);
            }
        }
        if(command.rfind("decoder-mode ",0)==0) {try {int mode=std::stoi(command.substr(13));if(mode>=0&&mode<=2)state.decoderMode=mode;}catch(...) {}}
        if(command.rfind("fps-limit ",0)==0) {try {int value=std::stoi(command.substr(10));if(value>=0&&value<=240)state.fpsLimit=value;}catch(...) {}}
        bool preset=command.rfind("configure-preset ",0)==0;
        if(preset||command.rfind("configure ",0)==0) {
            try {
                std::istringstream input(command.substr(preset?17:10));int fit,compare;EffectUpdate update;update.replaceAudioSession=preset;std::string values;
                if(!(input>>update.revision>>fit>>compare)||fit<0||fit>1||compare<0||compare>1)throw std::runtime_error("Invalid preview configuration");
                std::getline(input,values);update.effects=VideoEffects::Parse(values);update.fit=fit!=0;update.compare=compare!=0;
                state.Configure(std::move(update));
            } catch(const std::exception& error) {Report("{\"event\":\"configuration-error\",\"error\":"+JsonString(error.what())+"}");}
        }
    return true;
}
void HostProtocol::ReadCommands(std::istream& input,PlaybackControlState& state) {
    std::string command;
    while(std::getline(input,command))if(!ApplyCommand(command,state))return;
    state.stopped=true; // Pipe EOF must never orphan a wallpaper.
}
