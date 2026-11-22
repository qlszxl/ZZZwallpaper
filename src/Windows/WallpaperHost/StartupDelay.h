#pragma once
#include <Windows.h>
#include <cwchar>
#include <cstdlib>

struct StartupLaunchOptions {bool startup=false;DWORD delaySeconds=0;};
inline StartupLaunchOptions ParseStartupLaunch(int count,const wchar_t* const* values) {
    StartupLaunchOptions options;
    for(int i=1;i<count;i++) {
        if(wcscmp(values[i],L"--startup")==0)options.startup=true;
        if(wcscmp(values[i],L"--startup-delay")==0&&i+1<count) {
            const wchar_t* value=values[++i];wchar_t* end=nullptr;unsigned long parsed=wcstoul(value,&end,10);
            if(*value>=L'0'&&*value<=L'9'&&end&&!*end&&parsed<=3600)options.delaySeconds=parsed;
        }
    }
    return options;
}
inline bool WaitForStartupDelay(StartupLaunchOptions options,const wchar_t* name=L"Local\\ZZZwallpaper.StartupDelay.Cancel") {
    if(!options.startup) {
        if(HANDLE event=OpenEventW(EVENT_MODIFY_STATE,FALSE,name)) {SetEvent(event);CloseHandle(event);}
        return true;
    }
    if(options.delaySeconds==0)return true;
    HANDLE event=CreateEventW(nullptr,TRUE,FALSE,name);if(!event)return true;
    if(GetLastError()==ERROR_ALREADY_EXISTS) {CloseHandle(event);return false;}
    DWORD result=WaitForSingleObject(event,options.delaySeconds*1000);CloseHandle(event);
    return result==WAIT_TIMEOUT;
}
