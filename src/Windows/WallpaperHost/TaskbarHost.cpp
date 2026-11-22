#include <windows.h>
#include <atomic>
#include <iostream>
#include <map>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include "TaskbarAppearance.h"
#include "TaskbarTargets.h"

namespace {
std::mutex guard;
std::map<std::wstring,TaskbarTarget> desired;
struct Hidden {DWORD pid;std::wstring screen;TaskbarTarget target;bool hidden=true;ULONGLONG applied=0;};
std::map<HWND,Hidden> hidden;
std::atomic_bool done=false;
TaskbarAppearance appearance;
bool Taskbar(HWND window) {
    wchar_t name[128]{};GetClassNameW(window,name,128);
    return wcscmp(name,L"Shell_TrayWnd")==0||wcscmp(name,L"Shell_SecondaryTrayWnd")==0;
}
bool StillOwned(HWND window,const Hidden& saved) {
    DWORD pid=0;GetWindowThreadProcessId(window,&pid);return pid==saved.pid&&IsWindow(window)&&Taskbar(window);
}
void Show(HWND window) {ShowWindowAsync(window,SW_SHOWNOACTIVATE);}
void Restore(HWND window,const Hidden& saved) {if(saved.hidden)Show(window);if(saved.target.mode>=2)appearance.Restore(window);}
BOOL CALLBACK Visit(HWND window,LPARAM value) {
    if(!Taskbar(window))return TRUE;
    MONITORINFOEXW monitor{};monitor.cbSize=sizeof(monitor);
    if(!GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),&monitor))return TRUE;
    auto& active=*reinterpret_cast<std::map<std::wstring,TaskbarTarget>*>(value);
    if(!active.contains(monitor.szDevice))return TRUE;
    auto target=active.at(monitor.szDevice);int mode=target.mode;auto old=hidden.find(window);
    if(old!=hidden.end()&&old->second.target==target&&mode>=2&&!appearance.NeedsRefresh(old->second.applied))return TRUE;
    if(old!=hidden.end()&&old->second.target.mode!=mode) {Restore(window,old->second);hidden.erase(old);}
    DWORD pid=0;GetWindowThreadProcessId(window,&pid);
    if(mode==1) {if(IsWindowVisible(window)) {hidden[window]={pid,monitor.szDevice,target,true};ShowWindowAsync(window,SW_HIDE);}}
    else if(appearance.Apply(window,target))hidden[window]={pid,monitor.szDevice,target,false,GetTickCount64()};
    return TRUE;
}
}
int main(int argc,char** argv) {
    if(argc!=2)return 2;DWORD pid=0;try {pid=std::stoul(argv[1]);}catch(...) {return 2;}
    HANDLE parent=OpenProcess(SYNCHRONIZE,FALSE,pid);if(!parent)return 2;
    if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED))) {CloseHandle(parent);return 3;}
    std::thread reader([] {
        std::string line;
        while(std::getline(std::cin,line)) {
            if(line.size()>4096)break;auto next=ParseTaskbarTargets(line);
            std::lock_guard lock(guard);desired=std::move(next);
        }
        done=true;
    });
    while(!done&&WaitForSingleObject(parent,500)==WAIT_TIMEOUT) {
        std::map<std::wstring,TaskbarTarget> active;{std::lock_guard lock(guard);active=desired;}
        for(auto it=hidden.begin();it!=hidden.end();) {
            bool owned=StillOwned(it->first,it->second);
            MONITORINFOEXW monitor{};monitor.cbSize=sizeof(monitor);bool targeted=owned&&GetMonitorInfoW(MonitorFromWindow(it->first,MONITOR_DEFAULTTONEAREST),&monitor)&&active.contains(monitor.szDevice);
            if(!owned||!targeted) {if(owned)Restore(it->first,it->second);it=hidden.erase(it);}else ++it;
        }
        EnumWindows(Visit,reinterpret_cast<LPARAM>(&active));
    }
    for(auto& [window,saved]:hidden)if(StillOwned(window,saved))Restore(window,saved);
    appearance.Close();CoUninitialize();
    CloseHandle(parent);
    // A crashed parent may leave a blocked reader until pipe teardown. Restoration
    // precedes process exit; no detached thread can outlive the process.
    ExitProcess(0);
}
