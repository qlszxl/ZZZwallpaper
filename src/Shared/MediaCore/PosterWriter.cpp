#include "MediaUtilities.h"
#include <Windows.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <stdexcept>
extern "C" {
#include <libswscale/swscale.h>
}

void SavePoster(const VideoFrame& frame,const std::wstring& path) {
    int w=std::min(960,frame.width), h=std::max(1,(int)((double)frame.height*w/frame.width));
    std::vector<uint8_t> scaled((size_t)w*h*4);
    auto* sc=sws_getContext(frame.width,frame.height,AV_PIX_FMT_BGRA,w,h,AV_PIX_FMT_BGRA,SWS_BILINEAR,nullptr,nullptr,nullptr);
    if(!sc) throw std::runtime_error("Cannot allocate thumbnail conversion buffer");
    const uint8_t* in[]={frame.bgra.data()};int is[]={frame.width*4};uint8_t* out[]={scaled.data()};int os[]={w*4};
    int converted=sws_scale(sc,in,is,0,frame.height,out,os);sws_freeContext(sc);
    if(converted<=0)throw std::runtime_error("Thumbnail conversion failed");
    BITMAPFILEHEADER header{}; header.bfType=0x4D42;header.bfOffBits=sizeof(header)+sizeof(BITMAPINFOHEADER);header.bfSize=header.bfOffBits+(DWORD)scaled.size();
    BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=w;info.biHeight=-h;info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;
    std::ofstream file(std::filesystem::path(path),std::ios::binary);
    file.write((char*)&header,sizeof(header));file.write((char*)&info,sizeof(info));file.write((char*)scaled.data(),scaled.size());
    file.flush();if(!file)throw std::runtime_error("Cannot write thumbnail file");
}
