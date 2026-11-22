#pragma once
#include <array>
#include <istream>
#include <stdexcept>
#include <vector>

// Stable EffectKind bit indices on the wire, not UI positions. Audio mapping,
// taskbar visibility and the mask library are not image-processing layers.
inline constexpr std::array<int,22> VisualEffectKinds{0,1,2,3,4,5,6,7,8,9,10,11,12,15,16,17,18,19,20,22,23,24};
inline bool IsVisualEffectKind(int kind) {return (kind>=0&&kind<=12)||(kind>=15&&kind<=20)||kind==22||kind==23||kind==24;}
inline std::vector<int> ReadEffectOrder(std::istream& in,int version=27) {
    int count=0;
    const int expected=version>=36?22:version>=35?21:version>=27?20:19;
    if(!(in>>count)||count!=expected)throw std::runtime_error("Invalid effect stack length");
    std::vector<int> order;order.reserve(count);unsigned seen=0;
    for(int i=0;i<count;++i) {
        int kind=-1;
        if(!(in>>kind)||!IsVisualEffectKind(kind)||(version<27&&kind==22)||(version<35&&kind==23)||(version<36&&kind==24)||(seen&(1u<<kind)))throw std::runtime_error("Invalid effect stack permutation");
        seen|=1u<<kind;order.push_back(kind);
    }
    if(version<27)order.push_back(22); // The newly appended layer remains disabled for old wire data.
    if(version<35)order.push_back(23); // Disabled in older payloads.
    return order;
}
