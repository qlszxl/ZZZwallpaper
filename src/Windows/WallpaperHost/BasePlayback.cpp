#include "HostOptions.h"
#include "HostProtocol.h"
#include "HostTools.h"
#include "PlaybackSession.h"
#include <iostream>

extern "C" __declspec(dllexport) int __cdecl RunWallpaperBackendV1(int argc,wchar_t** argv) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    av_log_set_level(AV_LOG_QUIET);
    auto protocol=std::make_shared<HostProtocol>(std::cout);
    try {
        auto options=HostOptions::Parse(argc,argv);
        if(options.capabilities) {PrintHostCapabilities();return 0;}
        if(options.diagnose) {DiagnoseDesktop();return 0;}
        return PlaybackSession(std::move(options),protocol).Run();
    }catch(const std::exception& error) {
        protocol->Report("{\"ok\":false,\"event\":\"error\",\"error\":"+JsonString(error.what())+"}");return 1;
    }
}
