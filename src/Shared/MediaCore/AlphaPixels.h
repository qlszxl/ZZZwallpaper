#pragma once
#include <algorithm>
#include <cstdint>
#include <vector>

// RGBA and BGRA share the alpha position. Interpolate associated colors, then
// return straight alpha for the existing texture/blending contract.
inline void PremultiplyAlpha(std::vector<uint8_t>& pixels) {
    for(size_t i=0;i+3<pixels.size();i+=4)for(size_t c=0;c<3;++c)pixels[i+c]=uint8_t((unsigned(pixels[i+c])*pixels[i+3]+127)/255);
}
inline void UnpremultiplyAlpha(std::vector<uint8_t>& pixels) {
    for(size_t i=0;i+3<pixels.size();i+=4)for(size_t c=0;c<3;++c)pixels[i+c]=pixels[i+3]?uint8_t(std::min(255u,(unsigned(pixels[i+c])*255+pixels[i+3]/2)/pixels[i+3])):0;
}
