#include "MediaUtilities.h"
#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include <algorithm>

std::string Utf8(const std::wstring& text) {
    if(text.empty()) return {};
    int size=WideCharToMultiByte(CP_UTF8,0,text.data(),(int)text.size(),nullptr,0,nullptr,nullptr);
    std::string result(size,'\0'); WideCharToMultiByte(CP_UTF8,0,text.data(),(int)text.size(),result.data(),size,nullptr,nullptr); return result;
}
std::string JsonString(const std::string& value) {
    std::string s="\"";
    for(unsigned char c:value) { if(c=='\\'||c=='\"') {s+='\\';s+=c;} else if(c=='\n') s+="\\n"; else if(c=='\r') s+="\\r"; else if(c=='\t') s+="\\t"; else if(c>=32) s+=c; }
    return s+'"';
}
