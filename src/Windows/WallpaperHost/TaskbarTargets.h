#pragma once
#include <map>
#include <string>
#include <algorithm>
#include <charconv>
#include <array>
struct TaskbarTarget {
    int mode=1,opacity=0,blur=20;unsigned int rgb=0x181818;bool border=false;
    bool operator==(const TaskbarTarget&) const=default;
    unsigned int ABGR() const {return (static_cast<unsigned int>((opacity*255+50)/100)<<24)|((rgb&255)<<16)|(rgb&0xff00)|((rgb>>16)&255);}
};
inline std::map<std::wstring,TaskbarTarget> ParseTaskbarTargets(const std::string& line) {
    std::map<std::wstring,TaskbarTarget> next;if(line.size()>4096)return next;size_t start=0;
    const std::string prefix=R"(\\.\DISPLAY)";
    while(start<line.size()) {
        size_t end=line.find('|',start);auto part=line.substr(start,end==std::string::npos?end:end-start);auto equal=part.find('=');
        auto display=part.substr(0,equal);TaskbarTarget target;bool valid=true;
        if(equal!=std::string::npos) {
            auto options=part.substr(equal+1);std::array<unsigned int,5> values{};size_t offset=0,count=0;
            while(offset<options.size()&&count<values.size()) {
                auto comma=options.find(',',offset);auto last=comma==std::string::npos?options.size():comma;
                auto result=std::from_chars(options.data()+offset,options.data()+last,values[count++]);
                if(result.ec!=std::errc()||result.ptr!=options.data()+last){valid=false;break;}
                offset=last+1;if(last==options.size())break;
                if(offset==options.size()){valid=false;break;}
            }
            valid=valid&&offset>=options.size()&&(count==1||count==5)&&values[0]>=1&&values[0]<=3;
            if(valid) {
                target.mode=values[0];target.opacity=target.mode==3?40:0;
                if(count==5) {
                    valid=values[1]<=100&&values[2]<=100&&values[3]<=0xffffff&&values[4]<=1;
                    if(valid){target.opacity=values[1];target.blur=values[2];target.rgb=values[3];target.border=values[4]!=0;}
                }
            }
        }
        if(valid&&display.starts_with(prefix)&&display.size()>prefix.size()&&display.size()<128&&
            std::all_of(display.begin()+prefix.size(),display.end(),[](char value){return value>='0'&&value<='9';}))next.emplace(std::wstring(display.begin(),display.end()),target);
        if(end==std::string::npos)break;start=end+1;
    }
    return next;
}
