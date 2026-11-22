#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <istream>
#include <stdexcept>
#include <string>
#include <vector>

struct ScanCardsOptions {
    static constexpr int MaxImages=76,MaxSlices=MaxImages+1;
    bool enabled=false,localRegion=false,loop=true,centerEnabled=true,randomImages=true,changeOnCycle=false;
    int rows=6,columns=10,centerArea=12,imageFit=0,seed=1,quality=-1,centerQuality=-1;
    float gap=.08f,centerX=.5f,centerY=.5f,width=1,height=1;
    float backgroundOpacity=1,opacity=1;
    unsigned backgroundColor=0x030303,frameColor=0x303030,scanColor=0xA4F3FF;
    float scanDuration=2,durationRandom=.45f,startSpread=4,holdDuration=6,holdRandom=.35f,resetDuration=.4f;
    float centerDuration=4,centerDelay=1,centerHold=8,waitDuration=2,revealDuration=2.4f,tailLength=2.5f;
    // Protocol-local palette size preserves deterministic legacy random colors.
    int qualityCount=6;
    bool qualityScanTiming=true;
    std::string atlas="-";int regularCount=7,centerIndex=7;
    std::array<float,MaxSlices> aspects=[] {std::array<float,MaxSlices> a{};a.fill(1);return a;}();
    std::array<int,MaxSlices> qualities=[] {std::array<int,MaxSlices> a{};a.fill(-1);return a;}();
    int quantityMin=1,quantityMax=99;
    std::array<int,MaxSlices> quantities=[] {std::array<int,MaxSlices> a{};a.fill(-1);return a;}();
    float backgroundWidth=0,backgroundHeight=0; // zero: historical card region
    float cardScale=1;
    bool areaLayout=true;
    struct QuantityRule {int mode=0,minimum=1,maximum=99;std::vector<int> values;};
    std::array<QuantityRule,MaxSlices> quantityRules{};
    bool Active() const {return enabled&&opacity>0;}
    void Read(std::istream& in,int version=27) {
        int on,local,repeat,central,random,change,count;
        if(!(in>>on>>rows>>columns>>gap>>local>>centerX>>centerY>>width>>height>>backgroundOpacity>>backgroundColor>>opacity
            >>frameColor>>scanColor>>scanDuration>>durationRandom>>startSpread>>holdDuration>>holdRandom>>resetDuration
            >>repeat>>central>>centerArea>>centerDuration>>centerDelay>>centerHold>>imageFit>>random>>change>>seed
            >>atlas>>regularCount>>centerIndex>>count))throw std::runtime_error("Incomplete scan card parameters");
        auto range=[](double v,double a,double b) {if(!std::isfinite(v)||v<a||v>b)throw std::runtime_error("Invalid scan card parameter");};
        for(int flag:{on,local,repeat,central,random,change})range(flag,0,1);
        range(rows,1,40);range(columns,1,40);range(gap,0,.35);range(centerX,0,1);range(centerY,0,1);range(width,.05,1);range(height,.05,1);
        range(backgroundOpacity,0,1);range(opacity,0,1);range(backgroundColor,0,0xFFFFFF);range(frameColor,0,0xFFFFFF);range(scanColor,0,0xFFFFFF);
        range(scanDuration,.2,20);range(durationRandom,0,1);range(startSpread,0,60);range(holdDuration,0,120);range(holdRandom,0,1);range(resetDuration,.05,5);
        range(centerArea,version>=32?12:4,version>=32?12:6);range(centerDuration,.2,30);range(centerDelay,0,60);range(centerHold,0,120);range(imageFit,0,1);range(seed,0,65535);
        range(regularCount,1,version>=33?MaxImages:16);
        if(centerIndex!=regularCount||count!=regularCount+1)throw std::runtime_error("Invalid scan card atlas layout");
        if(atlas!="-"&&(atlas.size()!=64||atlas.find_first_not_of("0123456789ABCDEF")!=std::string::npos))throw std::runtime_error("Invalid scan card image key");
        for(int i=0;i<count;++i) {if(!(in>>aspects[i]))throw std::runtime_error("Incomplete scan card aspects");range(aspects[i],1.0/32,32);}
        waitDuration=0;revealDuration=0;tailLength=2.5f;quality=centerQuality=-1;qualities.fill(-1);qualityCount=version>=31?6:5;qualityScanTiming=version>=31;
        if(version>=28) {if(!(in>>waitDuration))throw std::runtime_error("Missing scan card wait duration");range(waitDuration,0,120);}
        if(version>=29) {
            if(!(in>>revealDuration>>tailLength>>quality>>centerQuality))throw std::runtime_error("Incomplete scan card reveal appearance");
            range(revealDuration,.3,version>=31?6:3);range(tailLength,2,3);range(quality,-1,qualityCount-1);range(centerQuality,-1,qualityCount-1);
            for(int i=0;i<count;++i) {if(!(in>>qualities[i]))throw std::runtime_error("Incomplete scan card image qualities");range(qualities[i],-1,qualityCount-1);}
        }
        quantities.fill(1);quantityMin=quantityMax=1;
        if(version>=30) {
            if(!(in>>quantityMin>>quantityMax))throw std::runtime_error("Missing scan card quantity range");
            range(quantityMin,0,999999);range(quantityMax,quantityMin,999999);
            for(int i=0;i<count;++i) {if(!(in>>quantities[i]))throw std::runtime_error("Incomplete scan card quantities");range(quantities[i],-1,999999);}
        }
        backgroundWidth=backgroundHeight=0;quantityRules={};
        if(version>=32) {
            if(!(in>>backgroundWidth>>backgroundHeight))throw std::runtime_error("Missing backdrop dimensions");
            range(backgroundWidth,.05,1);range(backgroundHeight,.05,1);
            for(int i=0;i<count;++i) {
                auto& rule=quantityRules[i];int length;
                if(!(in>>rule.mode>>rule.minimum>>rule.maximum>>length))throw std::runtime_error("Missing quantity rule");
                range(rule.mode,0,2);range(rule.minimum,0,999999);range(rule.maximum,rule.minimum,999999);range(length,0,64);
                if(rule.mode==2&&length==0)throw std::runtime_error("Empty quantity list");
                rule.values.resize(length);
                for(auto& value:rule.values) {if(!(in>>value))throw std::runtime_error("Missing quantity list value");range(value,0,999999);}
            }
        }
        cardScale=1;areaLayout=version>=33;
        if(areaLayout) {if(!(in>>cardScale))throw std::runtime_error("Missing scan card scale");range(cardScale,.25,3);}
        enabled=on!=0;localRegion=local!=0;loop=repeat!=0;centerEnabled=!areaLayout&&central!=0;randomImages=random!=0;changeOnCycle=change!=0;
    }
};
