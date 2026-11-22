#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
#include "../src/Windows/WallpaperHost/Renderer.h"

// Only an owned, never-shown HWND and synthetic video frames; no desktop capture.
int wmain(int argc,wchar_t** argv) {
    HWND window=nullptr;
    try {
        if(argc!=2)throw std::runtime_error("Pass a fixture output directory");
        std::filesystem::path folder(argv[1]);std::filesystem::create_directories(folder);
        window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Position pixel fixture",WS_POPUP,-32000,-32000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        if(!window||IsWindowVisible(window))throw std::runtime_error("Isolation failed");
        Renderer renderer;renderer.Open(window,640,360);
        VideoFrame frame;frame.width=1280;frame.height=360;frame.aspect=1280./360;frame.bgra.resize(1280*360*4);
        for(int y=0;y<360;y++)for(int x=0;x<1280;x++) {auto p=frame.bgra.data()+(y*1280+x)*4;p[0]=40;p[1]=100;p[2]=static_cast<unsigned char>(20+x*200/1280);p[3]=255;}
        unsigned checks=0;auto check=[&](bool result,const char* message) {checks++;if(!result)throw std::runtime_error(message);};
        auto render=[&](const char* name,int x,int y,bool contain,VideoEffects effects=VideoEffects{},int scale=100) {
            renderer.Draw(frame,contain,effects,false,false,{},true,x,y,scale);
            auto path=folder/(std::string(name)+".bmp");renderer.SaveFrame(path.wstring());
            std::ifstream in(path,std::ios::binary);BITMAPFILEHEADER file{};BITMAPINFOHEADER info{};in.read(reinterpret_cast<char*>(&file),sizeof(file));in.read(reinterpret_cast<char*>(&info),sizeof(info));
            check(info.biWidth==640&&info.biHeight==-360&&info.biBitCount==32,"Capture dimensions");in.seekg(file.bfOffBits);std::vector<unsigned char> pixels(640*360*4);in.read(reinterpret_cast<char*>(pixels.data()),pixels.size());check(bool(in),"Complete render readback");return pixels;
        };
        auto red=[](const auto& image,int x=320,int y=180) {return int(image[(y*640+x)*4+2]);};
        auto center=render("center",0,0,false),right=render("right",100,0,false),left=render("left",-100,0,false);
        check(red(center)-red(right)>35&&red(left)-red(center)>35,"Cover shifts toward the requested source edge");
        check(renderer.LastPasses==1,"Position does not add a render pass");
        auto top=render("contain-top",0,-100,true),bottom=render("contain-bottom",0,100,true);
        check(red(top,320,30)>30&&red(top,320,330)==0&&red(bottom,320,30)==0&&red(bottom,320,330)>30,"Contain moves content within letterbox");
        VideoEffects fx;fx.brightness=.15f;fx.effectOrder={0};
        auto full=render("ordered-right",100,0,false,fx);fx.intensity=.5f;auto half=render("ordered-half",100,0,false,fx);
        check(std::abs(red(half)-(red(full)+red(right))/2)<=3,"Ordered global mix samples the positioned original exactly once");
        fx.intensity=1;fx.pixelate.enabled=true;fx.pixelate.strength=1;fx.effectOrder={19,0};
        auto orderedGeometry=render("ordered-geometry",100,0,false,fx);
        fx.effectOrder.clear();auto fixedGeometry=render("legacy-geometry",100,0,false,fx);
        check(std::abs(red(orderedGeometry)-red(full))<8&&std::abs(red(fixedGeometry)-red(full))<8,"Geometry and subsequent stages do not apply the position offset twice");
        auto zoomedOut=render("scale-small",0,0,false,{},25);
        check(red(zoomedOut,320,180)>30&&red(zoomedOut,50,180)==0&&red(zoomedOut,320,30)==0,"Zoom out produces centered letterbox on both axes");
        auto shiftedSmall=render("scale-small-right",100,100,false,{},25);
        check(red(shiftedSmall,600,340)>30&&red(shiftedSmall,100,180)==0,"Zoomed-out content aligns to requested edges");
        for(bool ordered : {false,true}) {
            VideoEffects bright;bright.brightness=.25f;if(ordered)bright.effectOrder={0};
            auto brightSmall=render(ordered?"ordered-full-canvas":"fixed-full-canvas",100,100,false,bright,25);
            check(red(brightSmall,60,30)>10 && red(brightSmall,600,340)>red(shiftedSmall,600,340),
                "Screen-space effects cover both the positioned image and the empty canvas");
            VideoEffects flare;flare.lightFlare.enabled=true;flare.lightFlare.centerX=.125f;flare.lightFlare.centerY=.125f;
            if(ordered)flare.effectOrder={23};
            auto flared=render(ordered?"ordered-flare":"fixed-flare",100,100,false,flare,25);
            check(red(flared,80,45)>100 && red(flared,20,5)<red(flared,80,45)/2,"Flare coordinates stay anchored to the screen outside the scaled media");
            bright.intensity=.5f;
            auto mixed=render(ordered?"ordered-full-mix":"fixed-full-mix",100,100,false,bright,25);
            check(std::abs(red(mixed,60,30)*2-red(brightSmall,60,30))<4,"Effect mix uses black from the same canvas");
        }
        auto large=render("scale-large",100,0,false,{},200);
        check(red(large)<red(right)-10,"Zoom and position compose before source cropping");
        fx={};fx.brightness=.15f;fx.effectOrder={0};
        auto fullLarge=render("scale-ordered",100,0,false,fx,200);fx.intensity=.5f;
        auto halfLarge=render("scale-ordered-mix",100,0,false,fx,200);
        check(std::abs(red(halfLarge)-(red(fullLarge)+red(large))/2)<=3,"Scaled original matches ordered global mix");
        check(renderer.LastPasses<=3,"Scale adds no additional render pass");
        DestroyWindow(window);window=nullptr;
        std::ofstream out(folder/L"results.json");out<<"{\"passed\":true,\"checks\":"<<checks<<",\"visibleWindow\":false,\"desktopAttachment\":false}";
        std::cout<<"Position GPU checks passed: "<<checks<<std::endl;return 0;
    }catch(const std::exception& error) {if(window)DestroyWindow(window);std::cerr<<error.what()<<std::endl;return 1;}
}
