#include "../src/Windows/WallpaperHost/HostOptions.h"
#include "../src/Windows/WallpaperHost/HostProtocol.h"
#include <cassert>
#include <iostream>
#include <sstream>
#include <thread>
#include "../src/Windows/WallpaperHost/WallpaperPosition.h"
#include "../src/Windows/WallpaperHost/StartupDelay.h"
#include "../src/Windows/WallpaperHost/TaskbarTargets.h"

int main() {
    std::ostringstream events;
    HostProtocol protocol(events);PlaybackControlState state;
    protocol.ApplyCommand("position -100 37",state);
    assert(WallpaperPositionX(state.position)==-100&&WallpaperPositionY(state.position)==37);
    for(const char* invalid:{"position 101 1","position 1 -101","position 1","position 1 2 extra","position 1tail 2"})protocol.ApplyCommand(invalid,state);
    assert(WallpaperPositionX(state.position)==-100&&WallpaperPositionY(state.position)==37);
    protocol.ApplyCommand("position 23 -41 175",state);
    assert(WallpaperPositionX(state.position)==23&&WallpaperPositionY(state.position)==-41&&WallpaperPositionScale(state.position)==175);
    for(const char* invalid:{"position 0 0 9","position 0 0 401","position 0 0 120oops","position 0 0 120 1"})protocol.ApplyCommand(invalid,state);
    assert(WallpaperPositionScale(state.position)==175);
    protocol.ApplyCommand("position 0 0",state);assert(WallpaperPositionScale(state.position)==100);
    const wchar_t* delayArgs[]={L"launcher",L"--startup",L"--startup-delay",L"42"};
    auto delay=ParseStartupLaunch(4,delayArgs);assert(delay.startup&&delay.delaySeconds==42);
    for(const wchar_t* bad:{L"-1",L"",L"3601",L"12tail"}) {delayArgs[3]=bad;assert(ParseStartupLaunch(4,delayArgs).delaySeconds==0);}
    std::wstring eventName=L"Local\\ZZZwallpaper.Test.StartupDelay."+std::to_wstring(GetCurrentProcessId());
    auto before=GetTickCount64();assert(WaitForStartupDelay({true,1},eventName.c_str()));assert(GetTickCount64()-before>=900);
    bool cancelled=false;std::thread waiting([&] {cancelled=!WaitForStartupDelay({true,5},eventName.c_str());});
    HANDLE held=nullptr;for(int i=0;i<500&&!held;i++) {held=OpenEventW(EVENT_MODIFY_STATE,FALSE,eventName.c_str());if(!held)Sleep(1);}
    assert(held);assert(!WaitForStartupDelay({true,5},eventName.c_str()));assert(WaitForStartupDelay({false,0},eventName.c_str()));
    waiting.join();CloseHandle(held);assert(cancelled);
    auto targets=ParseTaskbarTargets("\\\\.\\DISPLAY1=2|\\\\.\\DISPLAY2=3|\\\\.\\DISPLAY3");
    assert(targets.size()==3&&targets.at(L"\\\\.\\DISPLAY1").mode==2&&targets.at(L"\\\\.\\DISPLAY3").mode==1);
    assert(ParseTaskbarTargets("\\\\.\\DISPLAY1=9|\\\\.\\DISPLAY=1|invalid=2|\\\\.\\DISPLAY2=22").empty());
    auto appearanceTargets=ParseTaskbarTargets(R"(\\.\DISPLAY1=3,25,48,16711680,0)");
    assert(appearanceTargets.size()==1);auto visual=appearanceTargets.begin()->second;
    assert(visual.mode==3&&visual.opacity==25&&visual.blur==48&&visual.rgb==0xff0000&&!visual.border&&visual.ABGR()==0x400000ff);
    for(const char* bad:{"3,101,1,0,0","2,1,101,0,0","3,1,1,16777216,0","2,0,0,0,2","3,0,0,0,0,1","2,0,0,0,0,","3,-1,0,0,0","3,20","3,,0,0,0",""})
        assert(ParseTaskbarTargets(std::string("\\\\.\\DISPLAY1=")+bad).empty());
    assert(protocol.ApplyCommand("pause",state)&&state.paused);
    protocol.ApplyCommand("resume",state);assert(!state.paused);
    protocol.ApplyCommand("show",state);assert(state.show);
    protocol.ApplyCommand("sound 1",state);assert(state.soundEnabled);
    protocol.ApplyCommand("sound-volume 37",state);assert(state.soundVolume==37);
    for(const char* invalid:{"sound-volume -1","sound-volume 101","sound-volume 12tail"})protocol.ApplyCommand(invalid,state);
    assert(state.soundVolume==37);
    protocol.ApplyCommand("fps-limit 60",state);protocol.ApplyCommand("fps-limit 241",state);assert(state.fpsLimit==60);
    protocol.ApplyCommand("decoder-mode 2",state);protocol.ApplyCommand("decoder-mode -1",state);assert(state.decoderMode==2);
    protocol.ApplyCommand("audio-device endpoint-1",state);assert(state.AudioDevice()==L"endpoint-1");
    protocol.ApplyCommand("audio-device invalid\nendpoint",state);assert(state.AudioDevice()==L"endpoint-1");
    protocol.ApplyCommand("audio-device default",state);assert(state.AudioDevice().empty());

    // A supported legacy configuration still reaches the mailbox as a complete,
    // single-use revision, and malformed updates cannot replace the last good one.
    const std::string legacy="2 1 0 0.1 100 0 0 0 0 0 1 1 0 0 0 0 0.5 0 0";
    protocol.ApplyCommand("configure 41 1 0 "+legacy,state);
    protocol.ApplyCommand("configure 42 3 0 "+legacy,state);
    auto update=state.TakeConfiguration();assert(update&&update->revision==41&&update->fit&&!update->compare);
    assert(!state.TakeConfiguration());
    assert(events.str().find("\"event\":\"configuration-error\"")!=std::string::npos);
    protocol.ApplyCommand("configure-preset 42 1 0 "+legacy,state);
    protocol.ApplyCommand("configure 43 0 0 "+legacy,state);
    update=state.TakeConfiguration();
    assert(update&&update->revision==43&&update->replaceAudioSession);
    protocol.ApplyCommand("configure 43 0 1 "+legacy,state);
    protocol.ApplyCommand("configure 44 1 0 "+legacy,state);
    update=state.TakeConfiguration();assert(update&&update->revision==44);
    assert(events.str().find("\"event\":\"configured\"")==std::string::npos); // Only presentation may acknowledge.

    PlaybackControlState other;protocol.ApplyCommand("loop 0",other);assert(!other.looping&&state.looping);
    std::istringstream input("pause\nresume\n");protocol.ReadCommands(input,other);assert(other.stopped&&!other.paused);
    assert(!protocol.ApplyCommand("stop",state)&&state.stopped);

    const wchar_t* args[]={L"host",L"--preview",L"--rect",L"10",L"20",L"640",L"360",L"--fps-limit",L"999",L"--software",L"--no-loop"};
    auto options=HostOptions::Parse(static_cast<int>(std::size(args)),const_cast<wchar_t**>(args));
    assert(options.preview&&options.bounds.left==10&&options.bounds.right==650&&options.bounds.bottom==380);
    assert(options.fpsLimit==240&&options.software&&options.decoderMode==2&&!options.looping);
    std::cout<<"Host protocol, mailbox isolation, EOF and launch option checks passed\n";
}
