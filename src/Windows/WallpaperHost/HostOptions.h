#pragma once
#include <Windows.h>
#include <string>

// Immutable launch configuration. Runtime commands never mutate launch options.
struct HostOptions {
    std::wstring file,poster,effectText,renderFrame,traceFrames,imagePreview;
    bool compareOriginal=false,stillImage=false,posterSession=false,metadataOnly=false;
    bool probe=false,software=false,smoke=false,diagnose=false,preview=false,headless=false,capabilities=false;
    bool contain=false,looping=true;
    int positionX=0,positionY=0;
    double posterTime=-1;
    int posterDirection=0,benchmarkSeconds=0,seconds=3,captureFrame=1,transitionMs=0,fpsLimit=0,decoderMode=0;
    HWND behind=nullptr;
    RECT bounds{0,0,640,360};
    static HostOptions Parse(int argc,wchar_t** argv);
    void ValidateSource() const;
};
