#include "HostOptions.h"
#include <algorithm>
#include <filesystem>
#include <stdexcept>

HostOptions HostOptions::Parse(int argc,wchar_t** argv) {
    HostOptions options;
    for(int i=1;i<argc;++i) {
        std::wstring arg=argv[i];
        if(arg==L"--capabilities") {options.capabilities=true;return options;}
        if(arg==L"--file"&&i+1<argc) options.file=argv[++i];
        else if(arg==L"--poster"&&i+1<argc) options.poster=argv[++i];
        else if(arg==L"--probe") options.probe=true;
        else if(arg==L"--image")options.stillImage=true;
        else if(arg==L"--software") {options.software=true;options.decoderMode=2;}
        else if(arg==L"--decoder-mode"&&i+1<argc)options.decoderMode=std::clamp(_wtoi(argv[++i]),0,2);
        else if(arg==L"--benchmark"&&i+1<argc)options.benchmarkSeconds=std::clamp(_wtoi(argv[++i]),1,6);
        else if(arg==L"--poster-session")options.posterSession=true;
        else if(arg==L"--metadata-only")options.metadataOnly=true;
        else if(arg==L"--poster-time"&&i+1<argc)options.posterTime=std::stod(argv[++i]);
        else if(arg==L"--poster-direction"&&i+1<argc)options.posterDirection=std::clamp(_wtoi(argv[++i]),-1,1);
        else if(arg==L"--contain") options.contain=true;
        else if(arg==L"--position"&&i+2<argc) {options.positionX=std::stoi(argv[++i]);options.positionY=std::stoi(argv[++i]);}
        else if(arg==L"--no-loop") options.looping=false;
        else if(arg==L"--fps-limit"&&i+1<argc)options.fpsLimit=std::clamp(_wtoi(argv[++i]),0,240);
        else if(arg==L"--diagnose") options.diagnose=true;
        else if(arg==L"--preview") options.preview=true;
        else if(arg==L"--headless")options.headless=true;
        else if(arg==L"--image-preview"&&i+1<argc) {options.imagePreview=argv[++i];options.headless=true;options.software=true;}
        else if(arg==L"--behind"&&i+1<argc)options.behind=reinterpret_cast<HWND>((uintptr_t)_wcstoui64(argv[++i],nullptr,10));
        else if(arg==L"--transition-ms"&&i+1<argc)options.transitionMs=std::clamp(_wtoi(argv[++i]),0,1000);
        else if(arg==L"--effects"&&i+1<argc)options.effectText=argv[++i];
        else if(arg==L"--original")options.compareOriginal=true;
        else if(arg==L"--render-frame"&&i+1<argc) {options.renderFrame=argv[++i];options.smoke=true;}
        else if(arg==L"--trace-frames"&&i+1<argc)options.traceFrames=argv[++i];
        else if(arg==L"--capture-frame"&&i+1<argc)options.captureFrame=_wtoi(argv[++i]);
        else if(arg==L"--smoke"&&i+1<argc) {options.smoke=true;options.seconds=_wtoi(argv[++i]);}
        else if(arg==L"--rect"&&i+4<argc) {options.bounds.left=_wtoi(argv[++i]);options.bounds.top=_wtoi(argv[++i]);options.bounds.right=options.bounds.left+_wtoi(argv[++i]);options.bounds.bottom=options.bounds.top+_wtoi(argv[++i]);}
    }
    return options;
}
void HostOptions::ValidateSource() const {
        if(positionX< -100||positionX>100||positionY< -100||positionY>100)throw std::runtime_error("Wallpaper position must be -100..100");
        if(captureFrame<1||captureFrame>600||(captureFrame!=1&&renderFrame.empty()))throw std::runtime_error("Capture frame must be 1..600 and requires --render-frame");
        if(!traceFrames.empty()&&!smoke&&!headless)throw std::runtime_error("Frame trace requires an isolated smoke/headless output");
        if(file.empty()) throw std::runtime_error("A local video source is required");
        // Local sources only. Containers cannot open network child resources through this app.
        if(!std::filesystem::is_regular_file(std::filesystem::path(file))) throw std::runtime_error("Source missing or unreadable");
}
