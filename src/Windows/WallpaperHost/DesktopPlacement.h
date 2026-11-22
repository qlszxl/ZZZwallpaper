#pragma once
#include <Windows.h>
#include <stdexcept>
struct DesktopTarget {HWND parent=nullptr,icons=nullptr;bool layered=false;};
static void PositionOutput(HWND window,const DesktopTarget& desktop,POINT point,int width,int height,bool visible,bool maintain=false,HWND behind=nullptr) {
    if(!IsWindow(desktop.parent)||(desktop.layered&&(!IsWindow(desktop.icons)||GetParent(desktop.icons)!=desktop.parent)))
        throw std::runtime_error("Desktop host lost; output removed");
    bool orderValid=true;
    if(desktop.layered) {
        bool iconsSeen=false,outputSeen=false;
        for(HWND child=GetWindow(desktop.parent,GW_CHILD);child;child=GetWindow(child,GW_HWNDNEXT)) {
            if(child==desktop.icons)iconsSeen=true;
            if(child==window) {outputSeen=true;if(!iconsSeen)orderValid=false;}
            wchar_t cls[64]{};GetClassNameW(child,cls,64);
            if(wcscmp(cls,L"WorkerW")==0&&IsWindowVisible(child)&&!outputSeen)orderValid=false;
        }
        orderValid&=outputSeen;
    }
    // Maintenance must not raise an older player above a newly prepared successor.
    UINT flags=SWP_NOACTIVATE|(visible?SWP_SHOWWINDOW:0)|((maintain&&orderValid)?SWP_NOZORDER:0);
    HWND insertAfter=desktop.layered?desktop.icons:HWND_TOP;
    if(behind&&IsWindow(behind)&&GetParent(behind)==desktop.parent) {
        wchar_t cls[64]{};GetClassNameW(behind,cls,64);
        if(wcscmp(cls,L"LumaWall.VideoOutput")==0)insertAfter=behind;
    }
    if(!SetWindowPos(window,insertAfter,point.x,point.y,width,height,flags))
        throw std::runtime_error("Cannot position desktop video output");
    if(GetParent(window)!=desktop.parent) throw std::runtime_error("Desktop video has an incorrect parent");
    if(visible&&!IsWindowVisible(window)) throw std::runtime_error("Desktop video window is hidden");
    if(desktop.layered) {
        // Verify ordering without moving, hiding or changing Explorer-owned windows.
        bool iconsSeen=false,outputSeen=false;
        for(HWND child=GetWindow(desktop.parent,GW_CHILD);child;child=GetWindow(child,GW_HWNDNEXT)) {
            if(child==desktop.icons)iconsSeen=true;
            if(child==window) {if(!iconsSeen)throw std::runtime_error("Video output would cover desktop icons");outputSeen=true;}
            wchar_t cls[64]{};GetClassNameW(child,cls,64);
            if(wcscmp(cls,L"WorkerW")==0&&IsWindowVisible(child)&&!outputSeen)
                throw std::runtime_error("Desktop background covers the video output");
        }
        if(!outputSeen)throw std::runtime_error("Desktop output is detached");
    }
}
