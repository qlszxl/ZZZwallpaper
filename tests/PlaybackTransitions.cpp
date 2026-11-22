#include <Windows.h>
#include <iostream>
#include <stdexcept>
#include <memory>
#include "../src/Windows/WallpaperHost/DesktopPlacement.h"
#include "../src/Windows/WallpaperHost/Renderer.h"

static void Require(bool ok,const char* message) {if(!ok)throw std::runtime_error(message);}
static bool Above(HWND a,HWND b) {for(HWND w=a;w;w=GetWindow(w,GW_HWNDNEXT))if(w==b)return true;return false;}
static LRESULT CALLBACK Proc(HWND w,UINT m,WPARAM a,LPARAM b) {if(m==WM_ERASEBKGND)return 1;return DefWindowProcW(w,m,a,b);}
static HWND Child(HWND parent,const wchar_t* name,DWORD extra=0) {
    HWND w=CreateWindowExW(extra,name,L"Transition test",WS_CHILD|WS_VISIBLE,0,0,160,90,parent,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(!w)throw std::runtime_error("Cannot create owned test child: "+std::to_string(GetLastError()));if(extra&WS_EX_LAYERED)SetLayeredWindowAttributes(w,0,255,LWA_ALPHA);return w;
}
int main() {
    HWND parent=nullptr;std::string stage="create";
    try {
        for(const auto* name:{L"LumaWall.TransitionTest",L"WorkerW",L"LumaWall.VideoOutput"}) {WNDCLASSW wc{};wc.lpfnWndProc=Proc;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=name;RegisterClassW(&wc);}
        // Entirely owned windows outside the virtual screen. Never attach to or modify Explorer.
        parent=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"LumaWall.TransitionTest",L"",WS_POPUP,-32000,-32000,160,90,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        Require(parent!=nullptr,"Cannot create owned parent");ShowWindow(parent,SW_SHOWNOACTIVATE);
        HWND background=Child(parent,L"WorkerW"),icons=Child(parent,L"STATIC"),old=Child(parent,L"LumaWall.VideoOutput",WS_EX_LAYERED);
        SetWindowPos(background,HWND_BOTTOM,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        SetWindowPos(icons,HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        DesktopTarget desktop{parent,icons,true};POINT point{0,0};stage="initial predecessor";PositionOutput(old,desktop,point,160,90,true);
        stage="initial successor";HWND candidate=Child(parent,L"LumaWall.VideoOutput",WS_EX_LAYERED);PositionOutput(candidate,desktop,point,160,90,true);
        SetWindowPos(old,icons,0,0,160,90,SWP_NOACTIVATE); // Reproduce the previous 500 ms maintenance behavior.
        Require(Above(old,candidate),"Legacy ordering reproduction failed");
        PositionOutput(candidate,desktop,point,160,90,true);
        for(int i=0;i<30;++i)PositionOutput(old,desktop,point,160,90,true,true);
        Require(Above(candidate,old),"Maintenance raised predecessor over successor");
        DestroyWindow(candidate);
        VideoFrame frame;frame.width=160;frame.height=90;frame.aspect=160.0/90;frame.bgra.resize(160*90*4,255);
        auto current=std::make_unique<Renderer>();current->Open(old,160,90);current->Draw(frame,false);current->CompletePresentation(true);
        int switches=0;
        for(int i=0;i<12;++i) {
            stage="handoff "+std::to_string(i);
            candidate=Child(parent,L"LumaWall.VideoOutput",WS_EX_LAYERED);
            PositionOutput(candidate,desktop,point,160,90,true,false,old);
            Require(Above(old,candidate),"Candidate exposed before its first frame");
            auto next=std::make_unique<Renderer>();next->TraceFrames=true;next->Open(candidate,160,90);
            for(size_t n=0;n<frame.bgra.size();n+=4) {frame.bgra[n]=(unsigned char)(40+i*10);frame.bgra[n+1]=100;frame.bgra[n+2]=200;}
            next->Draw(frame,false);next->CompletePresentation(true);
            Require(next->LastMean>100,"Candidate GPU surface is empty");
            PositionOutput(candidate,desktop,point,160,90,true);next->CompletePresentation(true);
            for(int tick=0;tick<10;++tick)PositionOutput(old,desktop,point,160,90,true,true);
            Require(Above(candidate,old)&&Above(icons,candidate)&&Above(candidate,background),"Handoff ordering changed");
            DestroyWindow(old);current=std::move(next);Require(IsWindow(candidate),"Retiring predecessor lost the successor");old=candidate;++switches;
        }
        stage="cancel candidate";candidate=Child(parent,L"LumaWall.VideoOutput",WS_EX_LAYERED);
        PositionOutput(candidate,desktop,point,160,90,true,false,old);DestroyWindow(candidate);
        Require(IsWindow(old)&&Above(old,background),"Cancelled candidate lost predecessor");
        stage="repair order";SetWindowPos(old,background,0,0,160,90,SWP_NOACTIVATE);
        PositionOutput(old,desktop,point,160,90,true,true);Require(Above(icons,old)&&Above(old,background),"Damaged background order was not repaired");
        DestroyWindow(parent);
        std::cout<<"{\"passed\":true,\"legacyOrderingBugReproduced\":true,\"switches\":"<<switches<<",\"maintenanceChecks\":150,\"cancelPreservesOld\":true,\"scope\":\"owned offscreen windows, GPU readback and DWM flush; not a desktop visual acceptance\"}"<<std::endl;
        return 0;
    } catch(const std::exception& error) {if(parent)DestroyWindow(parent);std::cerr<<stage<<": "<<error.what()<<std::endl;return 1;}
}
