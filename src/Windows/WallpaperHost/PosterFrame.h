#pragma once
#include "../../Shared/MediaCore/MediaSession.h"
#include <chrono>
#include <algorithm>
#include <cmath>

struct PosterQuality {
    double brightness=0, visible=0, contrast=0, clipped=0, sharpness=0;
    bool Readable() const {return brightness>=45&&visible>=.40&&contrast>=14&&clipped<=.55;}
    double Score() const {return visible+.35*std::min(contrast/60,1.0)+.20*std::min(brightness/110,1.0)-.70*clipped+1.8*sharpness;}
};
inline PosterQuality MeasurePosterFrame(const VideoFrame& frame) {
    if(frame.width<=0||frame.height<=0||frame.bgra.size()<static_cast<size_t>(frame.width)*frame.height*4)return {};
    const int w=320,h=std::clamp(static_cast<int>(320.0*frame.height/frame.width),32,320);
    std::vector<double> luma(static_cast<size_t>(w)*h);
    auto pixel=[&](int x,int y) {
        auto* p=frame.bgra.data()+(static_cast<size_t>(std::clamp(y,0,frame.height-1))*frame.width+std::clamp(x,0,frame.width-1))*4;
        return (54.0*p[2]+183.0*p[1]+19.0*p[0])/256.0;
    };
    for(int y=0;y<h;++y)for(int x=0;x<w;++x) {
        double sx=(x+.5)*frame.width/w-.5,sy=(y+.5)*frame.height/h-.5;
        int ix=static_cast<int>(std::floor(sx)),iy=static_cast<int>(std::floor(sy));double dx=sx-ix,dy=sy-iy;
        luma[static_cast<size_t>(y)*w+x]=(1-dy)*((1-dx)*pixel(ix,iy)+dx*pixel(ix+1,iy))+dy*((1-dx)*pixel(ix,iy+1)+dx*pixel(ix+1,iy+1));
    }
    double sum=0,squares=0,visible=0,clipped=0,count=0,detail=0,weightSum=0;
    for(int y=std::max(1,h/20);y<h-h/20-1;++y)for(int x=w/20;x<w-w/20-1;++x) {
        int i=y*w+x;double value=luma[i];sum+=value;squares+=value*value;++count;
        if(value>=40)++visible;if(value>=245)++clipped;
        // Fixed size, clipped edge spikes and central weighting reduce noise/subtitle bias.
        double lap=4*value-luma[i-1]-luma[i+1]-luma[i-w]-luma[i+w];
        double weight=(x>w/4&&x<3*w/4&&y>h/4&&y<3*h/4)?2:1;
        detail+=weight*std::min(lap*lap,1600.0);weightSum+=weight;
    }
    double mean=sum/count,contrast=std::sqrt(std::max(0.0,squares/count-mean*mean));
    return {mean,visible/count,contrast,clipped/count,std::clamp(std::sqrt(detail/weightSum)/(contrast+12.0),0.0,1.0)};
}
inline void FindPosterFrame(MediaSession& media,VideoFrame& frame) {
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(18);
    VideoFrame best=frame;auto quality=MeasurePosterFrame(frame);
    auto consider=[&]() {
        auto candidate=MeasurePosterFrame(frame);
        if((candidate.Readable()&&!quality.Readable())||(candidate.Readable()==quality.Readable()&&candidate.Score()>quality.Score())) {quality=candidate;best=frame;}
    };
    auto hasTime=[&]() {return std::chrono::steady_clock::now()<deadline;};
    auto scan=[&](double target,double span,int rate) {
        media.Seek(std::max(0.0,target));VideoFrame candidate;
        while(hasTime()&&media.Next(candidate,false,rate,-1,std::max(0.0,target))) {
            frame=std::move(candidate);consider();candidate.pts=frame.pts;candidate.duration=frame.duration;
            candidate.effectTime=frame.effectTime;candidate.frameIndex=frame.frameIndex;
            if(frame.pts-media.startTime>=target+span)break;
        }
    };
    if(std::isfinite(media.duration)&&media.duration>0) {
        for(double fraction:{0.0,.10,.50,.84,.28,.68,.94}) {
            if(!hasTime())break;
            try {scan(media.duration*fraction,.35,12);}catch(const std::exception&) { }
        }
        if(hasTime())try {scan(std::max(0.0,best.pts-media.startTime-.25),.5,30);}catch(const std::exception&) { }
    } else {
        try {for(int i=0;i<48&&hasTime();++i) {if(!media.Next(frame,false,12))break;consider();}}catch(const std::exception&) { }
    }
    frame=std::move(best);
}
// Seek from a keyframe and retain preroll so EOF requests return the last real frame.
// Exact PTS selection; direction 0: at/after, -1/+1: strictly previous/next.
inline VideoFrame PosterFrameAt(MediaSession& media,double seconds,int direction=0) {
    if(!std::isfinite(seconds)||seconds<0||seconds>1e7)throw std::runtime_error("Invalid poster time");
    double target=media.duration>0?std::min(seconds,std::max(0.0,media.duration-.000001)):seconds;
    media.Seek(direction<0?std::max(0.0,target-1):target);
    VideoFrame frame,previous;bool havePrevious=false;
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(12);
    for(int i=0;i<10000&&std::chrono::steady_clock::now()<deadline;++i) {
        if(!media.Next(frame,false)) {
            if(havePrevious)return previous;
            throw std::runtime_error("No frame at requested time");
        }
        double time=frame.pts-media.startTime;
        if(direction<0&&time>=target-1e-7)return havePrevious?previous:frame;
        if(direction==0&&time>=target-1e-7)return frame;
        if(direction>0&&time>target+1e-7)return frame;
        previous=frame;havePrevious=true;
    }
    throw std::runtime_error("Poster frame seek timed out");
}
