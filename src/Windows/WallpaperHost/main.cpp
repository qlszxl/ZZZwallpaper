#include <windows.h>
#include <filesystem>
#include <iostream>
#include <string>

int wmain(int argc,wchar_t** argv) {
    std::wstring path(32768,L'\0');
    DWORD length=GetEnvironmentVariableW(L"ZZZWALLPAPER_BASE_DIRECTORY",path.data(),static_cast<DWORD>(path.size()));
    std::filesystem::path directory;
    if(length && length<path.size()) {
        path.resize(length);directory=path;
    } else {
        length=GetModuleFileNameW(nullptr,path.data(),static_cast<DWORD>(path.size()));
        if(!length||length>=path.size())return 1;
        path.resize(length);directory=std::filesystem::path(path).parent_path()/L"ExpansionPacks"/L"base";
    }
    SetEnvironmentVariableW(L"ZZZWALLPAPER_BASE_DIRECTORY",directory.c_str());
    auto module=LoadLibraryExW((directory/L"BasePlayback.dll").c_str(),nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_APPLICATION_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    using Run=int(__cdecl*)(int,wchar_t**);
    auto run=module?reinterpret_cast<Run>(GetProcAddress(module,"RunWallpaperBackendV1")):nullptr;
    if(!run) {
        std::cout<<"{\"ok\":false,\"event\":\"error\",\"error\":\"Base playback API v1 unavailable\"}"<<std::endl;
        return 1;
    }
    // The input reader exits with its process. Keep the backend mapped until then.
    return run(argc,argv);
}
