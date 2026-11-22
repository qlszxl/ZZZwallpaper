#include <Windows.h>
#include <string>
#include <vector>
#include <shellapi.h>
#include "StartupDelay.h"
#pragma comment(lib,"shell32.lib")

static bool WaitForStartupDelay() {
    int count=0;auto values=CommandLineToArgvW(GetCommandLineW(),&count);
    auto options=ParseStartupLaunch(count,values);if(values)LocalFree(values);
    return WaitForStartupDelay(options);
}
// Also works when launched from a packaged development tool: do not inherit its ms-appx resource identity.
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR arguments,int) {
    if(!WaitForStartupDelay())return 0;
    wchar_t module[32768];GetModuleFileNameW(nullptr,module,32768);
    std::wstring path=module;path=path.substr(0,path.find_last_of(L"\\/"))+L"\\ZZZwallpaper.exe";
    std::wstring command=L"\""+path+L"\" "+arguments;
    SIZE_T size=0;InitializeProcThreadAttributeList(nullptr,1,0,&size);std::vector<unsigned char> memory(size);
    STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);startup.lpAttributeList=(LPPROC_THREAD_ATTRIBUTE_LIST)memory.data();
    if(!InitializeProcThreadAttributeList(startup.lpAttributeList,1,0,&size))return 1;
    DWORD policy=PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
    BOOL updated=UpdateProcThreadAttribute(startup.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY,&policy,sizeof(policy),nullptr,nullptr);
    PROCESS_INFORMATION process{};
    BOOL launched=updated&&CreateProcessW(path.c_str(),command.data(),nullptr,nullptr,FALSE,EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&process);
    DeleteProcThreadAttributeList(startup.lpAttributeList);
    if(!launched) {MessageBoxW(nullptr,L"无法启动ZZZwallpaper。请确认已完整解压整个文件夹。",L"ZZZwallpaper",MB_ICONERROR);return 2;}
    CloseHandle(process.hThread);CloseHandle(process.hProcess);return 0;
}
