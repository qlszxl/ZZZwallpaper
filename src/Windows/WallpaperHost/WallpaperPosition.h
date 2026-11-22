#pragma once
#include <cstdint>
// Position and scale reach the render thread as one atomic snapshot.
inline uint64_t PackWallpaperPosition(int x,int y,int scale=100) {return (uint64_t(scale)<<32)|(uint64_t(x+100)<<16)|uint64_t(y+100);}
inline int WallpaperPositionX(uint64_t value) {return int((value>>16)&0xffff)-100;}
inline int WallpaperPositionY(uint64_t value) {return int(value&0xffff)-100;}
inline int WallpaperPositionScale(uint64_t value) {return int(value>>32);}
