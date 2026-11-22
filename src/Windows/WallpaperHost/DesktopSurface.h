#pragma once
#include "DesktopPlacement.h"
#include "HostOptions.h"
#include "PlaybackControlState.h"

// Owns the Win32 output window and Explorer attachment. Rendering receives only
// its HWND and dimensions, while the session decides when a frame is ready.
class DesktopSurface {
    PlaybackControlState& state_;
    HostOptions options_;
    DesktopTarget desktop_;
    HWND window_=nullptr;
    POINT point_{};
    int width_=0,height_=0;
    bool dissolve_=false;
    static LRESULT CALLBACK WindowProc(HWND,UINT,WPARAM,LPARAM);
public:
    DesktopSurface(const HostOptions& options,PlaybackControlState& state);
    ~DesktopSurface() {Close();}
    DesktopSurface(const DesktopSurface&)=delete;
    DesktopSurface& operator=(const DesktopSurface&)=delete;
    HWND Handle() const {return window_;}
    HWND Parent() const {return desktop_.parent;}
    int Width() const {return width_;}
    int Height() const {return height_;}
    bool Layered() const {return desktop_.layered||dissolve_;}
    bool Dissolving() const {return dissolve_;}
    void Pump();
    void Maintain(bool visible);
    bool Show();
    bool RaiseAbovePredecessor();
    bool Reveal();
    void Close();
};
