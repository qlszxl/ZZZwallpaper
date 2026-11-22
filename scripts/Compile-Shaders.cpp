#include <Windows.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstring>
#include <fstream>
#include <filesystem>
#include <iostream>
#include "../src/Windows/WallpaperHost/PostProcess.h"

// Build-time compilation only: no D3D device, video decoding, windows or rendering.
int wmain(int argc,wchar_t** argv) {
    if(argc!=2)return 2;
    const std::filesystem::path destination(argv[1]);auto temporary=destination;temporary+=L".tmp";
    std::ofstream file(temporary,std::ios::binary);
    if(!file)return 2;
    file<<"#pragma once\nnamespace CompiledEffects {\n";
    for(const char* entry:{"vs","particleVS","particlePS","original","stackComposite","geometry","maskLayer","styled","transform","extract","blur","videoBlurHorizontal","videoBlurVertical","alternateBlur"}) {
        Microsoft::WRL::ComPtr<ID3DBlob> blob,error;
        auto hr=D3DCompile(PostProcessShader,std::strlen(PostProcessShader),"PostProcess.h",nullptr,nullptr,entry,
            (std::strcmp(entry,"vs")==0||std::strcmp(entry,"particleVS")==0)?"vs_4_0":"ps_4_0",D3DCOMPILE_OPTIMIZATION_LEVEL3|D3DCOMPILE_ENABLE_STRICTNESS,0,&blob,&error);
        if(FAILED(hr)) {std::cerr<<entry<<": "<<(error?static_cast<const char*>(error->GetBufferPointer()):"Shader compilation failed")<<std::endl;return 1;}
        file<<"inline constexpr unsigned char "<<entry<<"[]={\n";
        auto* bytes=static_cast<const unsigned char*>(blob->GetBufferPointer());
        for(size_t i=0;i<blob->GetBufferSize();++i) {file<<(unsigned int)bytes[i]<<',';if(i%32==31)file<<'\n';}
        file<<"\n};\n";std::cout<<"Compiled "<<entry<<" ("<<blob->GetBufferSize()<<" bytes)"<<std::endl;
    }
    file<<"}\n";file.close();if(!file)return 2;
    return MoveFileExW(temporary.c_str(),destination.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)?0:2;
}
