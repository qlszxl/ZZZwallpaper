#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <bcrypt.h>
#include <shlobj.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <array>
#include <algorithm>
#include "TaskbarTargets.h"
#pragma comment(lib,"bcrypt.lib")
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"shell32.lib")

// ABI from TranslucentTB 2026.2 ExplorerTAP/ITaskbarAppearanceService.idl.
// Its GPL-3.0 license and matching source ship in licenses/TranslucentTB.
struct __declspec(uuid("5bcf9150-c28a-4ef2-913c-4c3ea2f5ead0")) TaskbarAppearanceService : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE SetTaskbarAppearance(HWND,int,UINT)=0;
    virtual HRESULT STDMETHODCALLTYPE SetTaskbarBlur(HWND,UINT,FLOAT)=0;
    virtual HRESULT STDMETHODCALLTYPE ReturnTaskbarToDefaultAppearance(HWND)=0;
    virtual HRESULT STDMETHODCALLTYPE SetTaskbarBorderVisibility(HWND,BOOL)=0;
    virtual HRESULT STDMETHODCALLTYPE RestoreAllTaskbarsToDefault()=0;
    virtual HRESULT STDMETHODCALLTYPE RestoreAllTaskbarsToDefaultWhenProcessDies(DWORD)=0;
    virtual HRESULT STDMETHODCALLTYPE KillExplorerWhenPackageUninstalls(LPCWSTR)=0;
};
class TaskbarAppearance {
    using Inject=HRESULT (*)(HWND,REFIID,void**);
    Microsoft::WRL::ComPtr<TaskbarAppearanceService> service;
    HMODULE support=nullptr;DWORD explorer=0;int taskbarType=-1;ULONGLONG nextRetry=0;
    static bool Verified(const std::filesystem::path& path) {
        static constexpr unsigned char expected[]={0xC4,0x8F,0x1A,0x06,0x16,0x8A,0xCD,0x78,0x39,0x26,0x9D,0xDE,0x0D,0x15,0xCC,0xF5,0xD0,0x9C,0x70,0x25,0x12,0xB9,0x4D,0xD6,0xBA,0x9A,0x05,0xDF,0x1B,0x15,0x3D,0xC2};
        std::ifstream input(path,std::ios::binary);if(!input)return false;
        BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;
        if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
        bool good=BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
        std::array<char,16384> buffer{};std::array<unsigned char,32> digest{};
        while(good&&input) {input.read(buffer.data(),buffer.size());if(input.gcount())good=BCryptHashData(hash,reinterpret_cast<PUCHAR>(buffer.data()),static_cast<ULONG>(input.gcount()),0)>=0;}
        good=good&&input.eof()&&BCryptFinishHash(hash,digest.data(),static_cast<ULONG>(digest.size()),0)>=0;
        if(hash)BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(algorithm,0);
        return good&&std::equal(digest.begin(),digest.end(),std::begin(expected));
    }
    bool LoadSupport() {
        if(support)return true;
        wchar_t executable[32768]{};if(!GetModuleFileNameW(nullptr,executable,32768))return false;
        wchar_t home[32768];auto size=GetEnvironmentVariableW(L"ZZZWALLPAPER_INSTALL_DIRECTORY",home,32768);
        auto root=size&&size<32768?std::filesystem::path(home):std::filesystem::path(executable).parent_path();
        auto source=root/L"TaskbarSupport"/L"ExplorerTAP.dll";
        if(!Verified(source)) {std::cerr<<"Taskbar support checksum mismatch"<<std::endl;return false;}
        PWSTR local=nullptr;if(FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData,KF_FLAG_CREATE,nullptr,&local)))return false;
        auto cache=std::filesystem::path(local)/L"LumaWall"/L"TaskbarSupport"/L"2026.2-c48f1a06168acd78"/L"ExplorerTAP.dll";CoTaskMemFree(local);
        try {std::filesystem::create_directories(cache.parent_path());if(!Verified(cache))std::filesystem::copy_file(source,cache,std::filesystem::copy_options::overwrite_existing);}
        catch(...) {return false;}
        if(!Verified(cache))return false;
        // XAML pins its DLL in Explorer: versioned cache avoids locking install files.
        support=LoadLibraryExW(cache.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);return support!=nullptr;
    }
    static int TaskbarType(HWND taskbar,DWORD pid) {
        HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid);if(snapshot==INVALID_HANDLE_VALUE)return -1;
        MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);bool found=false;
        if(!Module32FirstW(snapshot,&entry)) {CloseHandle(snapshot);return -1;}
        do {if(_wcsicmp(entry.szModule,L"Taskbar.dll")==0) {found=true;break;}}while(Module32NextW(snapshot,&entry));
        CloseHandle(snapshot);if(!found)return 0;
        int islands=0;
        EnumChildWindows(taskbar,[](HWND child,LPARAM data)->BOOL {
            wchar_t name[128]{},title[128]{};GetClassNameW(child,name,128);GetWindowTextW(child,title,128);
            if(wcscmp(name,L"Windows.UI.Composition.DesktopWindowContentBridge")==0&&wcscmp(title,L"DesktopWindowXamlSource")==0)++*reinterpret_cast<int*>(data);
            return TRUE;
        },reinterpret_cast<LPARAM>(&islands));
        // The early Windows 11 taskbar has two islands and still uses AccentPolicy.
        return islands==1?1:islands==2?0:-1;
    }
    static bool OtherController() {
        HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);if(snapshot==INVALID_HANDLE_VALUE)return false;
        PROCESSENTRY32W entry{};entry.dwSize=sizeof(entry);bool found=false;
        if(Process32FirstW(snapshot,&entry))do {if(_wcsicmp(entry.szExeFile,L"TranslucentTB.exe")==0) {found=true;break;}}while(Process32NextW(snapshot,&entry));
        CloseHandle(snapshot);return found;
    }
    static bool Accent(HWND window,int mode,unsigned int color=0) {
        struct Policy {int state,flags;DWORD color;int animation;};struct Data {int attribute;void* data;SIZE_T size;};
        using Set=BOOL(WINAPI*)(HWND,Data*);
        auto set=reinterpret_cast<Set>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetWindowCompositionAttribute"));if(!set)return false;
        Policy policy{mode==2?2:mode==3?4:0,mode==3?0:2,color,0};Data data{19,&policy,sizeof(policy)};
        bool result=set(window,&data)!=FALSE;if(mode==0)SendNotifyMessageW(window,WM_DWMCOMPOSITIONCHANGED,0,0);return result;
    }
public:
    bool Apply(HWND window,const TaskbarTarget& target) {
        int mode=target.mode;
        HWND main=FindWindowW(L"Shell_TrayWnd",nullptr);DWORD pid=0;GetWindowThreadProcessId(main,&pid);if(!pid)return false;
        if(pid!=explorer) {service.Reset();explorer=pid;taskbarType=-1;nextRetry=0;}
        if(GetTickCount64()<nextRetry)return false;
        if(taskbarType<0)taskbarType=TaskbarType(main,pid);
        if(taskbarType<0) {nextRetry=GetTickCount64()+10000;return false;}
        if(!service&&taskbarType==1) {
            nextRetry=GetTickCount64()+10000;
            if(OtherController()) {std::cerr<<"Taskbar appearance is already controlled by TranslucentTB"<<std::endl;return false;}
            if(!LoadSupport())return false;
            auto inject=reinterpret_cast<Inject>(GetProcAddress(support,"InjectExplorerTAP"));
            HRESULT hr=inject?inject(main,__uuidof(TaskbarAppearanceService),reinterpret_cast<void**>(service.GetAddressOf())):E_NOINTERFACE;
            if(SUCCEEDED(hr))hr=service->RestoreAllTaskbarsToDefaultWhenProcessDies(GetCurrentProcessId());
            if(FAILED(hr)) {service.Reset();std::cerr<<"Taskbar XAML support failed: 0x"<<std::hex<<static_cast<unsigned long>(hr)<<std::dec<<std::endl;return false;}
            nextRetry=0;
        }
        bool result;
        if(taskbarType==1) {
            HRESULT hr=mode==3&&target.blur>0&&target.opacity<100?service->SetTaskbarBlur(window,target.ABGR(),target.blur/3.0f):service->SetTaskbarAppearance(window,1,target.ABGR());
            HRESULT border=service->SetTaskbarBorderVisibility(window,target.border);
            result=SUCCEEDED(hr)&&SUCCEEDED(border);
        } else {
            // Classic taskbars expose a fixed system blur; tint remains adjustable.
            unsigned int color=target.ABGR();if(mode==3&&(color>>24)==0)color|=0x01000000;
            result=Accent(window,mode==3&&target.blur==0?2:mode,color);
        }
        if(!result) {Restore(window);nextRetry=GetTickCount64()+10000;std::cerr<<"Taskbar appearance update failed"<<std::endl;}
        return result;
    }
    bool NeedsRefresh(ULONGLONG previous) const {return taskbarType==0&&GetTickCount64()-previous>=2000;}
    void Restore(HWND window) {if(taskbarType==1&&service){service->ReturnTaskbarToDefaultAppearance(window);service->SetTaskbarBorderVisibility(window,TRUE);}else Accent(window,0);}
    void Close() {service.Reset();}
};
