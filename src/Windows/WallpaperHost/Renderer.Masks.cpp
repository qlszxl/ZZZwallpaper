#include "Renderer.h"
#include <filesystem>
#include <fstream>

static void MaskCheck(HRESULT result,const char* label) {if(FAILED(result))throw std::runtime_error(std::string(label)+"; HRESULT="+std::to_string((unsigned long)result));}
void Renderer::EnsureParticleImage(const ParticleOptions& options,int layer) {
    auto readPixels=[](const std::string& key,size_t bytes) {
        std::vector<unsigned char> pixels(bytes);
        wchar_t temporary[MAX_PATH+1];DWORD length=GetTempPathW(MAX_PATH+1,temporary);
        if(length==0||length>MAX_PATH)throw std::runtime_error("Cannot locate particle image cache");
        auto path=std::filesystem::path(temporary)/L"ZZZwallpaper-particles"/(std::wstring(key.begin(),key.end())+L".rgba");
        std::ifstream input(path,std::ios::binary);
        if(!input.read(reinterpret_cast<char*>(pixels.data()),pixels.size())||input.peek()!=std::char_traits<char>::eof())throw std::runtime_error("Particle image cache is missing or invalid");
        return pixels;
    };
    auto create=[&](const std::string& key,unsigned size,unsigned slices,ComPtr<ID3D11ShaderResourceView>& view) {
        auto pixels=readPixels(key,size*size*4*slices);
        std::vector<D3D11_SUBRESOURCE_DATA> data(slices);
        for(unsigned i=0;i<slices;i++){data[i].pSysMem=pixels.data()+i*size*size*4;data[i].SysMemPitch=size*4;}
        D3D11_TEXTURE2D_DESC desc{};desc.Width=size;desc.Height=size;desc.MipLevels=1;desc.ArraySize=slices;
        desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;desc.Usage=D3D11_USAGE_IMMUTABLE;
        ComPtr<ID3D11Texture2D> next;ComPtr<ID3D11ShaderResourceView> nextView;
        MaskCheck(device->CreateTexture2D(&desc,data.data(),&next),"Create particle image");
        MaskCheck(device->CreateShaderResourceView(next.Get(),nullptr,&nextView),"Create particle image view");view=nextView;
    };
    if(!options.enabled||options.image=="-") {particleImage[layer].Reset();particleImageKey[layer].clear();}
    else if(particleImageKey[layer]!=options.image||!particleImage[layer]) {
        create(options.image,512,1,particleImage[layer]);particleImageKey[layer]=options.image;
    }
    if(options.enabled&&options.builtins!="-"&&(particleBuiltinsKey!=options.builtins||!particleBuiltins)) {
        create(options.builtins,ParticleOptions::BuiltinSize,ParticleOptions::BuiltinCount,particleBuiltins);particleBuiltinsKey=options.builtins;
    }
}
void Renderer::EnsureMaskImages(const VideoEffects& effects) {
    std::string key;bool any=false;
    for(const auto& m:effects.masks) {key+=(m.shape==5?m.image:"-")+"|";any|=m.shape==5&&m.image!="-";}
    if(!any) {maskImages.Reset();maskTexture.Reset();maskImageKey.clear();return;}
    if(key==maskImageKey&&maskImages)return;
    constexpr unsigned size=512;
    std::vector<unsigned char> pixels(size*size*16,0);
    wchar_t temporary[MAX_PATH+1];DWORD length=GetTempPathW(MAX_PATH+1,temporary);if(length==0||length>MAX_PATH)throw std::runtime_error("Cannot locate mask image cache");
    for(size_t i=0;i<effects.masks.size();i++) {
        const auto& m=effects.masks[i];if(m.shape!=5||m.image=="-")continue;
        auto path=std::filesystem::path(temporary)/L"ZZZwallpaper-masks"/(std::wstring(m.image.begin(),m.image.end())+L".mask");
        std::ifstream input(path,std::ios::binary);if(!input.read(reinterpret_cast<char*>(pixels.data()+i*size*size),size*size)||input.peek()!=std::char_traits<char>::eof())throw std::runtime_error("Mask image cache is missing or invalid");
    }
    std::array<D3D11_SUBRESOURCE_DATA,16> data{};for(size_t i=0;i<data.size();i++) {data[i].pSysMem=pixels.data()+i*size*size;data[i].SysMemPitch=size;}
    D3D11_TEXTURE2D_DESC desc{};desc.Width=size;desc.Height=size;desc.MipLevels=1;desc.ArraySize=16;desc.Format=DXGI_FORMAT_R8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;desc.Usage=D3D11_USAGE_IMMUTABLE;
    ComPtr<ID3D11Texture2D> next;ComPtr<ID3D11ShaderResourceView> nextView;
    MaskCheck(device->CreateTexture2D(&desc,data.data(),&next),"Create mask image array");MaskCheck(device->CreateShaderResourceView(next.Get(),nullptr,&nextView),"Create mask image view");
    maskTexture=next;maskImages=nextView;maskImageKey=key;
}

void Renderer::EnsureMaskLayers() {
    if(maskLayerTexture)return;
    D3D11_TEXTURE2D_DESC desc{};desc.Width=512;desc.Height=512;desc.MipLevels=1;desc.ArraySize=16;desc.Format=DXGI_FORMAT_R8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    ComPtr<ID3D11Texture2D> nextTexture;ComPtr<ID3D11ShaderResourceView> nextView;
    std::array<ComPtr<ID3D11RenderTargetView>,16> nextTargets;
    MaskCheck(device->CreateTexture2D(&desc,nullptr,&nextTexture),"Create shared mask layers");MaskCheck(device->CreateShaderResourceView(nextTexture.Get(),nullptr,&nextView),"Create shared mask view");
    for(unsigned i=0;i<16;i++) {D3D11_RENDER_TARGET_VIEW_DESC layerView{};layerView.Format=desc.Format;layerView.ViewDimension=D3D11_RTV_DIMENSION_TEXTURE2DARRAY;layerView.Texture2DArray.FirstArraySlice=i;layerView.Texture2DArray.ArraySize=1;MaskCheck(device->CreateRenderTargetView(nextTexture.Get(),&layerView,&nextTargets[i]),"Create mask layer target");}
    maskLayerTexture=std::move(nextTexture);maskLayerView=std::move(nextView);maskLayerTargets=std::move(nextTargets);maskValid.fill(false);
}
