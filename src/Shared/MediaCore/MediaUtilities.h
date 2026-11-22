#pragma once
#include "VideoFrame.h"
#include <string>

std::string Utf8(const std::wstring& text);
std::string JsonString(const std::string& text);
void SavePoster(const VideoFrame& frame,const std::wstring& path);
