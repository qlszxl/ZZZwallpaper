#pragma once
#include <Windows.h>
#include <string>

// The controller atomically replaces this one-byte projection of its saved
// setting. Only analysis workers poll it, at most once per second, never per
// sample. The path is inherited by all child hosts, including widget helpers.
class AudioProcessingConfig {
    std::wstring path_;
    ULONGLONG checked_=0;
    bool enabled_=false,checkedOnce_=false;
public:
    AudioProcessingConfig() {
        wchar_t path[32768];
        DWORD size=GetEnvironmentVariableW(L"ZZZWALLPAPER_AUDIO_CONFIG",path,32768);
        if(size>0&&size<32768)path_.assign(path,size);
    }
    const std::wstring& Path() const {return path_;}
    bool Enabled() {
        const auto now=GetTickCount64();
        if(checkedOnce_&&now-checked_<1000)return enabled_;
        checked_=now;checkedOnce_=true;
        if(path_.empty())return enabled_;
        HANDLE file=CreateFileW(path_.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE)return enabled_;
        char value=0;DWORD read=0;
        if(ReadFile(file,&value,1,&read,nullptr)&&read==1&&(value=='0'||value=='1'))enabled_=value=='1';
        CloseHandle(file);return enabled_;
    }
};
