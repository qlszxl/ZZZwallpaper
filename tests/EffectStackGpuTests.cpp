#include <Windows.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <cctype>
#include "../src/Windows/WallpaperHost/Renderer.h"

// Uses only an owned hidden window and synthetic frames. Never attaches to
// Explorer, presents a visible window, reads the desktop or starts playback.
static VideoFrame Fixture(const std::string& kind) {
    VideoFrame frame;frame.width=640;frame.height=360;frame.effectTime=.4;frame.frameIndex=24;
    if(kind=="portrait") {frame.width=240;frame.height=480;}
    else if(kind=="wide") {frame.width=733;frame.height=257;}
    else if(kind=="rotated"||kind=="rotated-black")frame.rotation=90;
    else if(kind!="black"&&kind!="color")throw std::runtime_error("Unknown source fixture");
    frame.aspect=double(frame.width)/frame.height;frame.bgra.resize(size_t(frame.width)*frame.height*4);
    for(int y=0;y<frame.height;y++)for(int x=0;x<frame.width;x++) {
        auto* pixel=&frame.bgra[(size_t(y)*frame.width+x)*4];pixel[3]=255;
        if(kind=="black"||kind=="rotated-black")continue;
        const bool stripe=((x/23+y/31)%2)==0;
        pixel[2]=static_cast<unsigned char>(35+x*170/frame.width);
        pixel[1]=static_cast<unsigned char>(25+y*185/frame.height);
        pixel[0]=static_cast<unsigned char>(stripe?210:55);
        if(x>frame.width*2/5&&x<frame.width*3/5&&y>frame.height/3&&y<frame.height*2/3)pixel[0]=pixel[1]=pixel[2]=245;
    }
    return frame;
}

int wmain(int argc,wchar_t** argv) {
    HWND window=nullptr;
    try {
        if(argc!=2)throw std::runtime_error("Pass the isolated fixture output directory");
        const std::filesystem::path folder(argv[1]);
        std::ifstream input(folder/L"fixtures.tsv");if(!input)throw std::runtime_error("Missing fixture manifest");
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Effect stack pixel tests",WS_POPUP,-32000,-32000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        if(!window||IsWindowVisible(window))throw std::runtime_error("Cannot create a hidden owned render surface");
        Renderer renderer;renderer.Open(window,640,360);
        std::ofstream results(folder/L"renders.tsv");if(!results)throw std::runtime_error("Cannot create result manifest");
        std::string line;unsigned count=0;
        while(std::getline(input,line)) {
            if(!line.empty()&&line.back()=='\r')line.pop_back();
            const auto first=line.find('\t'),second=line.find('\t',first+1);
            if(first==std::string::npos||second==std::string::npos)throw std::runtime_error("Malformed fixture manifest");
            const auto name=line.substr(0,first),source=line.substr(first+1,second-first-1);
            if(name.empty()||!std::all_of(name.begin(),name.end(),[](unsigned char c){return std::isalnum(c)||c=='-';}))throw std::runtime_error("Invalid fixture name");
            const auto wire=line.substr(second+1);
            if(!wire.starts_with(name.ends_with("-legacy")?"25 ":"31 "))throw std::runtime_error("Fixture wire version does not match its render path");
            auto frame=Fixture(source);auto fx=VideoEffects::Parse(wire);
            bool contain=source=="portrait"||source.starts_with("rotated")||source=="wide";
            AudioDrive audio;
            if(name.starts_with("audiozoom-")) {audio.active=true;audio.level=1;audio.pulse=1;}
            renderer.Draw(frame,contain,fx,false,false,audio);
            const int passes=renderer.LastPasses;
            renderer.SaveFrame((folder/(name+".bmp")).wstring());
            double submit=0,gpu=0;unsigned samples=0;
            if(name=="pointwise-forward"||name=="pointwise-reverse"||name=="pointwise-legacy"||name=="particles-blur"||name=="particles-glow") {
                for(int i=0;i<8;i++)renderer.Draw(frame,contain,fx,false,false,{},false);
                renderer.CompletePresentation(false);renderer.SetPerformanceEnabled(true);
                for(int i=0;i<40;i++) {renderer.Draw(frame,contain,fx,false,false,{},false);submit+=renderer.LastSubmitMs;}
                renderer.CompletePresentation(false);auto timings=renderer.TakeGpuTimings();
                submit/=40;gpu=timings.totalMs;samples=timings.samples;renderer.SetPerformanceEnabled(false);
            }
            results<<name<<'\t'<<passes<<'\t'<<submit<<'\t'<<gpu<<'\t'<<samples<<'\n';results.flush();
            std::cout<<"Rendered "<<name<<" ("<<passes<<" passes)"<<std::endl;++count;
        }
        DestroyWindow(window);window=nullptr;
        std::cout<<"Rendered "<<count<<" isolated synthetic fixtures; no desktop attachment."<<std::endl;return 0;
    }catch(const std::exception& error) {
        if(window)DestroyWindow(window);std::cerr<<error.what()<<std::endl;return 1;
    }
}

