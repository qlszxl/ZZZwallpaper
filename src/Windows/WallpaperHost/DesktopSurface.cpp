#include "DesktopSurface.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <thread>

static DesktopTarget DesktopHost() {
    HWND progman=FindWindowW(L"Progman",nullptr); if(!progman) return {};
    DWORD_PTR result;
    SendMessageTimeoutW(progman,0x052C,0xD,1,SMTO_ABORTIFHUNG,800,&result);
    // On the raised Windows 11 desktop Progman has no redirected bitmap.
    // A DX blt surface must be a layered sibling between the icons and WorkerW.
    // Nesting an ordinary child inside that WorkerW creates an invisible output.
    if(GetWindowLongPtrW(progman,GWL_EXSTYLE)&WS_EX_NOREDIRECTIONBITMAP) {
        HWND icons=FindWindowExW(progman,nullptr,L"SHELLDLL_DefView",nullptr);
        HWND background=FindWindowExW(progman,nullptr,L"WorkerW",nullptr);
        if(icons&&background) return {progman,icons,true};
        return {};
    }
    struct Search {HWND parent=nullptr,iconRoot=nullptr;} search;
    EnumWindows([](HWND hwnd,LPARAM data)->BOOL {
        auto& s=*reinterpret_cast<Search*>(data);
        if(FindWindowExW(hwnd,nullptr,L"SHELLDLL_DefView",nullptr)) {
            s.iconRoot=hwnd;
            s.parent=FindWindowExW(nullptr,hwnd,L"WorkerW",nullptr);
            return FALSE;
        }
        return TRUE;
    },reinterpret_cast<LPARAM>(&search));
    if(search.parent && IsWindowVisible(search.parent)) return {search.parent,nullptr,false};
    return {};
}

LRESULT CALLBACK DesktopSurface::WindowProc(HWND hwnd,UINT message,WPARAM wparam,LPARAM lparam) {
    auto* self=reinterpret_cast<DesktopSurface*>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));
    if(message==WM_NCCREATE) {
        self=static_cast<DesktopSurface*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
        SetWindowLongPtrW(hwnd,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));
    }
    if(self) {
        auto& state=self->state_;
        if(message==WM_KEYDOWN) {if(wparam==VK_SPACE)state.paused=!state.paused.load();if(wparam==VK_ESCAPE)state.stopped=true;}
        if(message==WM_NCHITTEST&&!self->options_.preview)return HTTRANSPARENT;
        if(message==WM_MOUSEACTIVATE&&!self->options_.preview)return MA_NOACTIVATE;
        if(message==WM_CLOSE||message==WM_DESTROY) {state.stopped=true;return 0;}
    }
    if(message==WM_ERASEBKGND)return 1;
    return DefWindowProc(hwnd,message,wparam,lparam);
}
DesktopSurface::DesktopSurface(const HostOptions& options,PlaybackControlState& state):state_(state),options_(options) {
    try {
        desktop_=(options_.smoke||options_.preview||options_.headless)?DesktopTarget{}:DesktopHost();
        HWND parent=desktop_.parent;
        if(!options_.smoke&&!options_.preview&&!options_.headless&&!parent) throw std::runtime_error("Explorer desktop host not available on this Windows build");
        WNDCLASSW wc{};wc.lpfnWndProc=WindowProc;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"LumaWall.VideoOutput";RegisterClassW(&wc);
        width_=options_.bounds.right-options_.bounds.left;height_=options_.bounds.bottom-options_.bounds.top;
        point_={options_.bounds.left,options_.bounds.top};if(parent) ScreenToClient(parent,&point_);
        DWORD style=options_.preview?(WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX):(parent?WS_CHILD:WS_POPUP);
        RECT extent{0,0,width_,height_};if(options_.preview)AdjustWindowRectEx(&extent,style,FALSE,0);
        dissolve_=options_.transitionMs>0&&parent&&options_.behind&&IsWindow(options_.behind)&&GetParent(options_.behind)==parent;
        DWORD exStyle=options_.preview?0:(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT|((desktop_.layered||dissolve_)?WS_EX_LAYERED:0));
        window_=CreateWindowExW(exStyle,wc.lpszClassName,options_.preview?L"ZZZwallpaper · 视频预览（空格暂停 / Esc 关闭）":L"ZZZwallpaper Video",style,point_.x,point_.y,extent.right-extent.left,extent.bottom-extent.top,parent,nullptr,wc.hInstance,this);
        if(!window_) throw std::runtime_error("Cannot create desktop render window");
        if((desktop_.layered||dissolve_)&&!SetLayeredWindowAttributes(window_,0,dissolve_?0:255,LWA_ALPHA))throw std::runtime_error("Cannot enable desktop video composition");
        if(parent)PositionOutput(window_,desktop_,point_,width_,height_,false,false,options_.behind);
    }catch(...) {Close();throw;}
}
void DesktopSurface::Close() {if(window_) {DestroyWindow(window_);window_=nullptr;}}
void DesktopSurface::Pump() {MSG msg;while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)) {if(msg.message==WM_QUIT)state_.stopped=true;TranslateMessage(&msg);DispatchMessageW(&msg);}}
void DesktopSurface::Maintain(bool visible) {
    if(!IsWindow(desktop_.parent)||GetParent(window_)!=desktop_.parent)throw std::runtime_error("Desktop host lost; output removed");
    if(desktop_.layered)PositionOutput(window_,desktop_,point_,width_,height_,visible,true);
}
bool DesktopSurface::Show() {
    if(options_.preview) {ShowWindow(window_,SW_SHOW);SetForegroundWindow(window_);return true;}
    if(!options_.smoke&&!options_.headless) {PositionOutput(window_,desktop_,point_,width_,height_,true,false,options_.behind);return true;}
    return false;
}
bool DesktopSurface::RaiseAbovePredecessor() {
    if(!desktop_.parent||!options_.behind)return false;
    PositionOutput(window_,desktop_,point_,width_,height_,true);return true;
}
bool DesktopSurface::Reveal() {
    using Clock=std::chrono::steady_clock;
    auto start=Clock::now();
    while(!state_.stopped) {
        Pump();
        double progress=std::clamp(std::chrono::duration<double,std::milli>(Clock::now()-start).count()/options_.transitionMs,0.0,1.0);
        BYTE alpha=static_cast<BYTE>(std::lround(255*progress*progress*(3-2*progress)));
        if(!SetLayeredWindowAttributes(window_,0,alpha,LWA_ALPHA))throw std::runtime_error("Cannot reveal desktop video output");
        if(progress>=1)break;
        std::this_thread::sleep_for(std::chrono::milliseconds(8));
    }
    return !state_.stopped;
}
