#include "BlurSampling.h"
#include "LargeGlitchVariation.h"
#include <dwmapi.h>
#include <chrono>
#include <thread>
#include "Renderer.h"
#include "EffectStack.h"
#include "AudioParameters.h"
#include "PostProcess.generated.h"
#include <cmath>
#include <algorithm>
#include <fstream>
#include <filesystem>
#include <cstring>
#include <d3d10.h>
extern "C" {
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

static void Check(HRESULT result,const char* label) {if(FAILED(result))throw std::runtime_error(std::string(label)+" (HRESULT "+std::to_string((unsigned long)result)+")");}
void Renderer::SetPerformanceEnabled(bool enabled) {
    if(performanceEnabled==enabled)return;
    performanceEnabled=enabled;gpuQueriesAvailable=false;performanceFrame=0;
    gpuSamples=0;gpuTotalSum=gpuBaseSum=gpuGlowSum=gpuBlurSum=0;
    LastSubmitMs=LastPresentMs=0;
    for(auto& slot:gpuQueries)slot={};
    if(!enabled)return;
    // Optional diagnostics must not prevent wallpaper playback on an adapter
    // that cannot create timestamp queries.
    for(auto& slot:gpuQueries) {
        D3D11_QUERY_DESC desc{D3D11_QUERY_TIMESTAMP_DISJOINT,0};
        if(FAILED(device->CreateQuery(&desc,&slot.disjoint)))return;
        desc.Query=D3D11_QUERY_TIMESTAMP;
        for(auto& stamp:slot.stamps)if(FAILED(device->CreateQuery(&desc,&stamp)))return;
    }
    gpuQueriesAvailable=true;
}
void Renderer::CollectGpuTimings() {
    if(!performanceEnabled||!gpuQueriesAvailable)return;
    for(auto& slot:gpuQueries) {
        if(!slot.pending)continue;
        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT disjoint{};
        auto result=context->GetData(slot.disjoint.Get(),&disjoint,sizeof(disjoint),D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if(result==S_FALSE)continue;
        if(FAILED(result)||disjoint.Disjoint||!disjoint.Frequency) {slot.pending=false;continue;}
        std::array<UINT64,11> stamps{};bool ready=true,valid=true;
        for(size_t i=0;i<stamps.size();++i) {
            if(slot.ordered?(i>=3&&i<=5)||(i>=7&&i<=8&&!slot.blur)||(i>=9&&!slot.glow):i>=7)continue;
            result=context->GetData(slot.stamps[i].Get(),&stamps[i],sizeof(stamps[i]),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if(result==S_FALSE) {ready=false;break;}
            if(FAILED(result)) {valid=false;break;}
        }
        if(!ready)continue;
        slot.pending=false;if(!valid)continue;
        if(slot.ordered) {
            if(stamps[1]<stamps[0]||stamps[6]<stamps[2]||(slot.blur&&stamps[8]<stamps[7])||(slot.glow&&stamps[10]<stamps[9]))valid=false;
        } else for(size_t i=1;i<7;++i)if(stamps[i]<stamps[i-1])valid=false;
        if(!valid)continue;
        double toMs=1000.0/static_cast<double>(disjoint.Frequency);
        double blur=slot.blur?(slot.ordered?stamps[8]-stamps[7]:stamps[4]-stamps[3])*toMs:0;
        double glow=slot.glow?(slot.ordered?stamps[10]-stamps[9]:stamps[5]-stamps[4])*toMs:0;
        // Exclude CPU-side effect-constant construction between upload and passes.
        double total=((stamps[1]-stamps[0])+(stamps[6]-stamps[2]))*toMs;
        gpuTotalSum+=total;gpuBlurSum+=blur;gpuGlowSum+=glow;
        gpuBaseSum+=std::max(0.0,total-blur-glow);++gpuSamples;
    }
}
Renderer::GpuQuerySlot* Renderer::BeginGpuSample() {
    if(!performanceEnabled||!gpuQueriesAvailable)return nullptr;
    CollectGpuTimings();
    // One sampled frame per ten draws, at most four outstanding frames.
    if(performanceFrame++%10!=0)return nullptr;
    for(auto& slot:gpuQueries)if(!slot.pending) {
        slot.blur=slot.glow=slot.ordered=false;
        context->Begin(slot.disjoint.Get());context->End(slot.stamps[0].Get());return &slot;
    }
    return nullptr;
}
Renderer::GpuTimings Renderer::TakeGpuTimings() {
    CollectGpuTimings();GpuTimings result;result.samples=gpuSamples;
    if(gpuSamples) {
        result.totalMs=gpuTotalSum/gpuSamples;result.baseMs=gpuBaseSum/gpuSamples;
        result.glowMs=gpuGlowSum/gpuSamples;result.blurMs=gpuBlurSum/gpuSamples;
    }
    gpuSamples=0;gpuTotalSum=gpuBaseSum=gpuGlowSum=gpuBlurSum=0;return result;
}
void Renderer::Open(HWND window,int w,int h,ID3D11Device* sharedDevice) {
    width=w;height=h;
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=w;desc.BufferDesc.Height=h;desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.OutputWindow=window;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    if(sharedDevice) {
        ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIFactory> factory;
        if(SUCCEEDED(sharedDevice->QueryInterface(IID_PPV_ARGS(&dxgi)))&&SUCCEEDED(dxgi->GetAdapter(&adapter))&&
            SUCCEEDED(adapter->GetParent(IID_PPV_ARGS(&factory)))&&SUCCEEDED(factory->CreateSwapChain(sharedDevice,&desc,&chain))) {
            device=sharedDevice;device->GetImmediateContext(&context);
        }
    }
    if(!chain) {
        device.Reset();context.Reset();
        auto result=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,&chain,&device,nullptr,&context);
        if(FAILED(result))Check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,&chain,&device,nullptr,&context),"Create D3D11 output");
    }
    ComPtr<ID3D10Multithread> multithread;
    if(SUCCEEDED(context.As(&multithread)))multithread->SetMultithreadProtected(TRUE);
    ComPtr<ID3D11Texture2D> back;Check(chain->GetBuffer(0,IID_PPV_ARGS(&back)),"Get backbuffer");Check(device->CreateRenderTargetView(back.Get(),nullptr,&target),"Create render target");
    Check(device->CreateVertexShader(CompiledEffects::vs,sizeof(CompiledEffects::vs),nullptr,&vertex),"Create vertex shader");
    auto createPixel=[&](const auto& bytes,ComPtr<ID3D11PixelShader>& output) {Check(device->CreatePixelShader(bytes,sizeof(bytes),nullptr,&output),"Create pixel shader");};
    Check(device->CreateVertexShader(CompiledEffects::particleVS,sizeof(CompiledEffects::particleVS),nullptr,&particleVertex),"Create particle vertex shader");
    createPixel(CompiledEffects::particlePS,particlePixel);
    D3D11_BLEND_DESC blend{};auto& rt=blend.RenderTarget[0];rt.BlendEnable=TRUE;rt.SrcBlend=D3D11_BLEND_SRC_ALPHA;rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOp=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=D3D11_BLEND_ONE;rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    Check(device->CreateBlendState(&blend,&particleBlend),"Create particle blending");
    createPixel(CompiledEffects::transform,transformPixel);
    createPixel(CompiledEffects::stackComposite,stackCompositePixel);
    createPixel(CompiledEffects::maskLayer,maskPixel);createPixel(CompiledEffects::alternateBlur,alternateBlurPixel);createPixel(CompiledEffects::geometry,geometryPixel);createPixel(CompiledEffects::original,pixel);createPixel(CompiledEffects::styled,stylePixel);createPixel(CompiledEffects::extract,extractPixel);createPixel(CompiledEffects::blur,blurPixel);createPixel(CompiledEffects::videoBlurHorizontal,videoBlurPixel);createPixel(CompiledEffects::videoBlurVertical,videoBlurVerticalPixel);
    D3D11_SAMPLER_DESC sd{};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;
    Check(device->CreateSamplerState(&sd,&sampler),"Create sampler");
    D3D11_BUFFER_DESC cb{};cb.ByteWidth=sizeof(RenderSettings);cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;Check(device->CreateBuffer(&cb,nullptr,&constants),"Create constants");
}
void Renderer::EnsureGeometry(int w,int h) {
    if(w==geometryW&&h==geometryH)return;
    geometryTarget={};D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    Check(device->CreateTexture2D(&desc,nullptr,&geometryTarget.texture),"Create effect geometry surface");
    Check(device->CreateRenderTargetView(geometryTarget.texture.Get(),nullptr,&geometryTarget.target),"Create effect geometry target");
    Check(device->CreateShaderResourceView(geometryTarget.texture.Get(),nullptr,&geometryTarget.view),"Create effect geometry view");
    geometryW=w;geometryH=h;
}
bool Renderer::UploadHardware(const VideoFrame& frame) {
    auto* decoded=frame.hardware.get();
    if(!decoded||decoded->format!=AV_PIX_FMT_D3D11||!decoded->data[0])return false;
    auto* input=reinterpret_cast<ID3D11Texture2D*>(decoded->data[0]);
    ComPtr<ID3D11Device> owner;input->GetDevice(&owner);
    if(owner.Get()!=device.Get())return false;
    D3D11_TEXTURE2D_DESC inputDesc{};input->GetDesc(&inputDesc);
    if(inputDesc.Format!=DXGI_FORMAT_NV12&&inputDesc.Format!=DXGI_FORMAT_P010)return false;
    // Constant-luminance BT.2020 and other uncommon matrices keep swscale's
    // established conversion. Never silently substitute a different matrix.
    if(frame.colorSpace!=AVCOL_SPC_UNSPECIFIED&&frame.colorSpace!=AVCOL_SPC_BT709&&
        frame.colorSpace!=AVCOL_SPC_BT470BG&&frame.colorSpace!=AVCOL_SPC_SMPTE170M&&frame.colorSpace!=AVCOL_SPC_BT2020_NCL)return false;
    if(!videoDevice&&FAILED(device.As(&videoDevice)))return false;
    if(!videoContext&&FAILED(context.As(&videoContext)))return false;
    if(!videoProcessor) {
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
        content.InputFrameFormat=D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content.InputWidth=content.OutputWidth=frame.width;content.InputHeight=content.OutputHeight=frame.height;
        content.InputFrameRate=content.OutputFrameRate={30,1};content.Usage=D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        if(FAILED(videoDevice->CreateVideoProcessorEnumerator(&content,&videoEnumerator)))return false;
        UINT flags=0;
        if(FAILED(videoEnumerator->CheckVideoProcessorFormat(DXGI_FORMAT_B8G8R8A8_UNORM,&flags))||!(flags&D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT))return false;
        if(FAILED(videoDevice->CreateVideoProcessor(videoEnumerator.Get(),0,&videoProcessor)))return false;
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputDesc{};
        outputDesc.ViewDimension=D3D11_VPOV_DIMENSION_TEXTURE2D;
        if(FAILED(videoDevice->CreateVideoProcessorOutputView(texture.Get(),videoEnumerator.Get(),&outputDesc,&videoOutput))) {videoProcessor.Reset();return false;}
    }
    UINT flags=0;
    if(FAILED(videoEnumerator->CheckVideoProcessorFormat(inputDesc.Format,&flags))||!(flags&D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT))return false;
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputViewDesc{};
    inputViewDesc.ViewDimension=D3D11_VPIV_DIMENSION_TEXTURE2D;
    inputViewDesc.Texture2D.ArraySlice=static_cast<UINT>(reinterpret_cast<uintptr_t>(decoded->data[1]));
    ComPtr<ID3D11VideoProcessorInputView> inputView;
    if(FAILED(videoDevice->CreateVideoProcessorInputView(input,videoEnumerator.Get(),&inputViewDesc,&inputView)))return false;
    const bool bt709=frame.colorSpace==AVCOL_SPC_BT709,bt2020=frame.colorSpace==AVCOL_SPC_BT2020_NCL;
    ComPtr<ID3D11VideoContext1> colors;
    if(SUCCEEDED(videoContext.As(&colors))) {
        DXGI_COLOR_SPACE_TYPE source=bt2020
            ?(frame.fullRange?DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P2020:DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P2020)
            :bt709?(frame.fullRange?DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709:DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709)
            :(frame.fullRange?DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601:DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601);
        auto destination=bt2020?DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P2020:DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709;
        ComPtr<ID3D11VideoProcessorEnumerator1> conversion;BOOL supported=FALSE;
        if(FAILED(videoEnumerator.As(&conversion))||FAILED(conversion->CheckVideoProcessorFormatConversion(inputDesc.Format,source,DXGI_FORMAT_B8G8R8A8_UNORM,destination,&supported))||!supported)return false;
        colors->VideoProcessorSetStreamColorSpace1(videoProcessor.Get(),0,source);
        colors->VideoProcessorSetOutputColorSpace1(videoProcessor.Get(),destination);
    } else {
        if(bt2020)return false;
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE source{},destination{};
        source.YCbCr_Matrix=bt709?1:0;source.YCbCr_xvYCC=0;
        source.Nominal_Range=frame.fullRange?D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255:D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
        destination.RGB_Range=0;
        videoContext->VideoProcessorSetStreamColorSpace(videoProcessor.Get(),0,&source);
        videoContext->VideoProcessorSetOutputColorSpace(videoProcessor.Get(),&destination);
    }
    ID3D11ShaderResourceView* unbound[16]{};context->PSSetShaderResources(0,16,unbound);context->OMSetRenderTargets(0,nullptr,nullptr);
    RECT bounds{0,0,frame.width,frame.height};
    videoContext->VideoProcessorSetStreamFrameFormat(videoProcessor.Get(),0,D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    videoContext->VideoProcessorSetStreamSourceRect(videoProcessor.Get(),0,TRUE,&bounds);
    videoContext->VideoProcessorSetStreamDestRect(videoProcessor.Get(),0,TRUE,&bounds);
    videoContext->VideoProcessorSetOutputTargetRect(videoProcessor.Get(),TRUE,&bounds);
    videoContext->VideoProcessorSetStreamAutoProcessingMode(videoProcessor.Get(),0,FALSE);
    D3D11_VIDEO_PROCESSOR_STREAM stream{};stream.Enable=TRUE;stream.pInputSurface=inputView.Get();
    return SUCCEEDED(videoContext->VideoProcessorBlt(videoProcessor.Get(),videoOutput.Get(),0,1,&stream));
}
void Renderer::EnsureGlow(int w,int h) {
    if(w==glowW&&h==glowH)return;
    for(auto& output:glowTargets) {
        output={};D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        Check(device->CreateTexture2D(&desc,nullptr,&output.texture),"Create glow surface");
        Check(device->CreateRenderTargetView(output.texture.Get(),nullptr,&output.target),"Create glow target");
        Check(device->CreateShaderResourceView(output.texture.Get(),nullptr,&output.view),"Create glow view");
    }
    glowW=w;glowH=h;
}
void Renderer::EnsureVideoBlur(int w,int h) {
    if(w==blurW&&h==blurH)return;
    for(auto& output:videoBlurTargets) {
        output={};D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        Check(device->CreateTexture2D(&desc,nullptr,&output.texture),"Create video blur surface");
        Check(device->CreateRenderTargetView(output.texture.Get(),nullptr,&output.target),"Create video blur target");
        Check(device->CreateShaderResourceView(output.texture.Get(),nullptr,&output.view),"Create video blur view");
    }
    blurW=w;blurH=h;
}
void Renderer::Pass(ID3D11RenderTargetView* output,ID3D11PixelShader* shader,ID3D11ShaderResourceView* input,ID3D11ShaderResourceView* glow,D3D11_VIEWPORT viewport,ID3D11ShaderResourceView* softened,ID3D11ShaderResourceView* original) {

    ID3D11ShaderResourceView* empty[7]={nullptr,nullptr,nullptr,nullptr,nullptr,nullptr,nullptr};context->PSSetShaderResources(0,7,empty);
    context->OMSetRenderTargets(1,&output,nullptr);context->RSSetViewports(1,&viewport);context->PSSetShader(shader,nullptr,0);
    ID3D11ShaderResourceView* views[7]={input,glow,softened,original?original:input,maskImages.Get(),nullptr,shader==maskPixel.Get()?nullptr:maskLayerView.Get()};context->PSSetShaderResources(0,7,views);context->Draw(3,0);++LastPasses;
}
void Renderer::EnsureStack(int w,int h) {
    if(w==stackW&&h==stackH)return;
    for(auto& output:stackTargets) {
        output={};D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        Check(device->CreateTexture2D(&desc,nullptr,&output.texture),"Create effect stack surface");
        Check(device->CreateRenderTargetView(output.texture.Get(),nullptr,&output.target),"Create effect stack target");
        Check(device->CreateShaderResourceView(output.texture.Get(),nullptr,&output.view),"Create effect stack view");
    }
    stackW=w;stackH=h;
}
void Renderer::DrawOrderedStack(RenderSettings& settings,const VideoEffects& fx,D3D11_VIEWPORT viewport,ID3D11ShaderResourceView* source,
    const std::function<void(int)>& drawParticles,GpuQuerySlot* sample) {
    auto full=settings;
    const auto stages=BuildEffectStack(full,fx);
    if(sample) {sample->ordered=true;sample->blur=sample->glow=false;}
    auto upload=[&] {context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);};
    float black[4]={0,0,0,1};context->ClearRenderTargetView(target.Get(),black);
    const auto gridX=EffectStackRasterAxis(viewport.TopLeftX,viewport.Width);
    const auto gridY=EffectStackRasterAxis(viewport.TopLeftY,viewport.Height);
    EnsureStack(gridX.count,gridY.count);
    D3D11_VIEWPORT prepared{gridX.offset,gridY.offset,viewport.Width,viewport.Height,0,1};
    auto mapStackInput=[&] {
        settings.inputScaleX=gridX.scale;settings.inputScaleY=gridY.scale;
        settings.inputBiasX=gridX.bias;settings.inputBiasY=gridY.bias;
        settings.rasterOriginX=(float)gridX.first;settings.rasterOriginY=(float)gridY.first;
    };
    // Normalize fit and rotation exactly once. Every subsequent input is the
    // complete encoded image from the preceding stage, including its sprites.
    Pass(stackTargets[0].target.Get(),pixel.Get(),source,nullptr,prepared);
    int current=0;
    for(const auto& stage:stages) {
        settings=EffectStageSettings(full,stage);mapStackInput();upload();
        auto& a=settings.advanced;
        auto* input=stackTargets[current].view.Get();
        ID3D11ShaderResourceView* softened=nullptr;ID3D11ShaderResourceView* bloom=nullptr;
        if(sample&&stage.Contains(11)) {sample->blur=true;context->End(sample->stamps[7].Get());}
        if(sample&&stage.Contains(3)) {sample->glow=true;context->End(sample->stamps[9].Get());}
        if(stage.Contains(11)) {
            // Full-resolution scratch surfaces: quality is controlled by sampling count.
            float reduction=1.f;
            EnsureVideoBlur(std::max(1,(int)std::ceil(viewport.Width/reduction)),std::max(1,(int)std::ceil(viewport.Height/reduction)));
            D3D11_VIEWPORT reduced{0,0,(float)blurW,(float)blurH,0,1};float radius=fx.blurRadius*height/1080.f;
            a.filterMethod=(float)fx.blurMethod;settings.bx=radius/viewport.Width;settings.by=0;upload();
            Pass(videoBlurTargets[0].target.Get(),fx.blurMethod<=1?videoBlurPixel.Get():alternateBlurPixel.Get(),input,nullptr,reduced);
            if(fx.blurMethod<=1) {
                settings.bx=0;settings.by=radius/viewport.Height;upload();
                Pass(videoBlurTargets[1].target.Get(),videoBlurVerticalPixel.Get(),videoBlurTargets[0].view.Get(),nullptr,reduced);softened=videoBlurTargets[1].view.Get();
            } else softened=videoBlurTargets[0].view.Get();
        }
        a.filterMethod=0;upload();
        if(stage.Contains(3)) {
            EnsureGlow(std::max(1,(int)std::ceil(viewport.Width/4)),std::max(1,(int)std::ceil(viewport.Height/4)));
            D3D11_VIEWPORT reduced{0,0,(float)glowW,(float)glowH,0,1};
            Pass(glowTargets[0].target.Get(),extractPixel.Get(),input,nullptr,reduced);
            settings.bx=1.f/glowW;settings.by=0;upload();
            Pass(glowTargets[1].target.Get(),blurPixel.Get(),glowTargets[0].view.Get(),nullptr,reduced);
            settings.bx=0;settings.by=1.f/glowH;upload();
            Pass(glowTargets[0].target.Get(),blurPixel.Get(),glowTargets[1].view.Get(),nullptr,reduced);bloom=glowTargets[0].view.Get();
        }
        bool geometry=stage.Contains(15)||stage.Contains(17)||stage.Contains(19)||settings.rainMix>0||a.rainOutlineMix>0;
        auto* shader=stage.Contains(24)?transformPixel.Get():geometry?geometryPixel.Get():stage.Contains(16)?pixel.Get():stylePixel.Get();
        int next=1-current;
        Pass(stackTargets[next].target.Get(),shader,input,bloom,prepared,softened,source);
        if(stage.Contains(16)) {drawParticles(0);drawParticles(1);}
        current=next;
        if(sample&&stage.Contains(11))context->End(sample->stamps[8].Get());
        if(sample&&stage.Contains(3))context->End(sample->stamps[10].Get());
    }
    settings=full;mapStackInput();upload();
    Pass(target.Get(),stackCompositePixel.Get(),stackTargets[current].view.Get(),nullptr,viewport,nullptr,source);
}
void Renderer::Draw(const VideoFrame& frame,bool contain,const VideoEffects& requested,bool compareOriginal,bool present,const AudioDrive& audio,bool uploadFrame,int positionX,int positionY,int positionScale) {
    auto submitStart=performanceEnabled?std::chrono::steady_clock::now():std::chrono::steady_clock::time_point{};
    LastPasses=0;
    if(videoW!=frame.width||videoH!=frame.height) {
        uploadFrame=true;
        videoOutput.Reset();videoProcessor.Reset();videoEnumerator.Reset();
        view.Reset();texture.Reset();D3D11_TEXTURE2D_DESC desc{};
        desc.Width=frame.width;desc.Height=frame.height;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
        Check(device->CreateTexture2D(&desc,nullptr,&texture),"Create video texture");Check(device->CreateShaderResourceView(texture.Get(),nullptr,&view),"Create texture view");videoW=frame.width;videoH=frame.height;
    }
    if(uploadFrame) {
        if(frame.hardware) {if(!UploadHardware(frame))throw HardwareFrameTransferError();}
        else context->UpdateSubresource(texture.Get(),0,nullptr,frame.bgra.data(),frame.width*4,0);
    }
    auto* gpuSample=BeginGpuSample();
    if(gpuSample)context->End(gpuSample->stamps[1].Get());
    VideoEffects fx=compareOriginal||!requested.BaseActive()?VideoEffects{}:requested;
    bool audioActive=audio.active&&fx.audioReactive&&fx.audioAmount>0;
    float energy=audioActive?std::clamp(audio.level,0.0f,1.0f):0;
    float pulse=audioActive?std::clamp(audio.pulse,0.0f,1.0f):0;
    float impact=audioActive?std::clamp(audio.impact,0.0f,1.0f):0;
    // Give each beat a visible accent followed by a quiet interval, rather than
    // mostly raising a steady amplitude. Blend with the original at amount=0.
    // This local copy never enables effects or turns a zero setting into nonzero.
    if(audioActive&&fx.audioLegacy) {
        auto react=[&](float value,float quiet,float body,float accent,float maximum) {
            float envelope=quiet+body*energy+accent*pulse+2.8f*impact;
            return std::clamp(value*(1-fx.audioAmount+fx.audioAmount*envelope),0.0f,maximum);
        };
        if(fx.audioScanlines) {
            fx.scanlineStrength=react(fx.scanlineStrength,.12f,.35f,1.7f,.35f);
            fx.scanlineWidth=react(fx.scanlineWidth,.45f,.3f,1.1f,12);
            fx.scanBendAmplitude=react(fx.scanBendAmplitude,.08f,.4f,2.2f,80);
        }
        if(fx.audioGlitch) {
            fx.glitchStrength=react(fx.glitchStrength,.04f,.25f,2.3f,1);
            fx.glitchRgbOffset=react(fx.glitchRgbOffset,.08f,.3f,2.0f,80);
            fx.clusterBlend=react(fx.clusterBlend,.18f,.3f,1.2f,1);
            fx.largeGlitchStrength=react(fx.largeGlitchStrength,.35f,.35f,1.5f,1);
            fx.largeGlitchRgbOffset=react(fx.largeGlitchRgbOffset,.2f,.4f,1.7f,80);
            for(auto& layer:fx.layers) {
                layer.strength=react(layer.strength,.06f,.25f,2.2f,1);
                layer.rgbOffset=react(layer.rgbOffset,.08f,.3f,2.0f,80);
            }
        }
        if(fx.audioDots) {
            fx.dotSize=react(fx.dotSize,.4f,.35f,.85f,1);
            fx.dotStrength=react(fx.dotStrength,.3f,.4f,.7f,1);
            fx.dotGlowStrength=react(fx.dotGlowStrength,.05f,.35f,2.5f,1);
            fx.dotPulse=react(fx.dotPulse,.15f,.3f,1.1f,1);
            fx.rippleDotSize=react(fx.rippleDotSize,.4f,.35f,.9f,1);
            fx.rippleDotStrength=react(fx.rippleDotStrength,.18f,.35f,1.1f,1);
            fx.dotRippleStrength=react(fx.dotRippleStrength,.03f,.4f,2.4f,2);
        }
        if(fx.audioBlur) {
            fx.blurRadius=react(fx.blurRadius,.08f,.55f,1.8f,100);
            fx.blurStrength=react(fx.blurStrength,.2f,.45f,.85f,1);
            fx.blurCoverage=react(fx.blurCoverage,.45f,.35f,.8f,1);
        }
        if(fx.audioDistortion) {
            float sign=fx.distortionStrength<0?-1.0f:1.0f;
            fx.distortionStrength=sign*react(std::abs(fx.distortionStrength),.05f,.5f,2.1f,1);
        }
        if(fx.audioGlow)fx.glowStrength=react(fx.glowStrength,.06f,.35f,2.3f,3);
        if(fx.audioNoise)fx.noiseStrength=react(fx.noiseStrength,.04f,.25f,2.3f,.3f);
    }
    for(const auto& rule:fx.audioRules) {
        auto mapped=audio.Mapping(rule.parameter);
        double value=audioActive&&rule.enabled&&mapped?mapped->value:AudioParameterValue(requested,rule.parameter);
        SetAudioParameter(fx,rule.parameter,value);
    }
    struct Trigger {bool linked=false;const AudioMappedValue* beat=nullptr;double duration=.35;
        bool Playing(double seconds) const {return beat&&beat->beats>0&&beat->beatAge>=0&&beat->beatAge<seconds;}
        float Progress(double seconds) const {return beat?static_cast<float>(std::clamp(beat->beatAge/seconds,0.0,1.0)):1.f;}
    };
    auto trigger=[&](AudioParameterId id) {
        Trigger result;
        if(!fx.audioReactive||fx.audioAmount<=0)return result;
        for(const auto& rule:fx.audioRules)if(rule.parameter==static_cast<int>(id)&&rule.enabled) {
            result.linked=true;result.duration=rule.release;
            if(audioActive)result.beat=audio.Mapping(rule.parameter);
            break;
        }
        return result;
    };
    const auto glitchTrigger=trigger(AudioParameterId::Glitch_Trigger),largeTrigger=trigger(AudioParameterId::LargeGlitch_Trigger),
        rippleTrigger=trigger(AudioParameterId::DotRipple_Trigger),zoomTrigger=trigger(AudioParameterId::ZoomTunnel_Trigger),particleTrigger=trigger(AudioParameterId::Particles_Trigger);
    // The integrated motion includes beat pulses, so speed accelerates briefly
    // with a beat without multiplying absolute time by an instantaneous value.
    auto mappedPhase=[&](AudioParameterId id,double fallback) {
        const int key=static_cast<int>(id);
        auto rule=std::find_if(fx.audioRules.begin(),fx.audioRules.end(),[&](const AudioRule& r){return r.parameter==key;});
        if(rule==fx.audioRules.end())return fallback;
        auto mapped=audio.Mapping(key);
        return audioActive&&rule->enabled&&mapped?mapped->phase:frame.effectTime*AudioParameterValue(requested,key);
    };
    double extraTime=audioActive&&fx.audioLegacy?audio.motion*fx.audioAmount*3:0;
    double scanTime=frame.effectTime+(fx.audioScanlines?extraTime:0),glitchTime=frame.effectTime+(fx.audioGlitch?extraTime:0);
    double dotTime=frame.effectTime+(fx.audioDots?extraTime:0),blurTime=frame.effectTime+(fx.audioBlur?extraTime:0);
    double distortionTime=frame.effectTime+(fx.audioDistortion?extraTime:0);
    double aspect=frame.aspect;if(std::abs(std::sin(frame.rotation*3.141592653589793/180))>.5)aspect=1/aspect;
    double targetAspect=(double)width/height;float sx=1,sy=1;
    RenderSettings settings;
    settings.angle=(float)(frame.rotation*3.141592653589793/180);
    if(contain) {if(aspect>targetAspect)sy=(float)(targetAspect/aspect);else sx=(float)(aspect/targetAspect);}
    else {if(aspect>targetAspect)settings.cx=(float)(targetAspect/aspect);else settings.cy=(float)(aspect/targetAspect);}
    const float wallpaperScale=std::clamp(positionScale,10,400)/100.f;
    const float imageWidth=(contain?sx:1/settings.cx)*wallpaperScale,imageHeight=(contain?sy:1/settings.cy)*wallpaperScale;
    sx=std::min(1.f,imageWidth);sy=std::min(1.f,imageHeight);
    settings.cx=std::min(1.f,1/imageWidth);settings.cy=std::min(1.f,1/imageHeight);
    const float alignX=std::clamp(positionX,-100,100)/100.f,alignY=std::clamp(positionY,-100,100)/100.f;
    settings.sourceOffsetX=settings.originalOffsetX=-(1-settings.cx)*.5f*alignX;
    settings.sourceOffsetY=settings.originalOffsetY=-(1-settings.cy)*.5f*alignY;
    auto zoomMapping=audio.Mapping(static_cast<int>(AudioParameterId::AudioZoomAmount));
    bool explicitZoom=std::any_of(fx.audioRules.begin(),fx.audioRules.end(),[](const AudioRule& r){return r.parameter==static_cast<int>(AudioParameterId::AudioZoomAmount);});
    const bool orderedRequested=!fx.effectOrder.empty()&&fx.maskPreview<0;
    float plainCropX=settings.cx,plainCropY=settings.cy;
    const float zoomIntensity=orderedRequested?1.f:fx.intensity;
    float audioZoom=explicitZoom?(1+(audioActive&&zoomMapping&&zoomMapping->enabled?float(zoomMapping->value)*zoomIntensity:0)):1+(audioActive&&fx.audioLegacy&&fx.audioZoom?fx.audioZoomAmount*fx.audioAmount*zoomIntensity*std::clamp(.35f*energy+.65f*pulse+impact,0.0f,1.0f):0);
    if(!fx.transformProtocol) {settings.cx/=audioZoom;settings.cy/=audioZoom;}
    D3D11_VIEWPORT mediaViewport{width*(1-sx)/2*(1+alignX),height*(1-sy)/2*(1+alignY),width*sx,height*sy,0,1};
    const bool composeMargins=fx.Active()&&(sx<1.f||sy<1.f);
    D3D11_VIEWPORT viewport=composeMargins?D3D11_VIEWPORT{0,0,(float)width,(float)height,0,1}:mediaViewport;
    RenderSettings placement;
    placement.cx=plainCropX;placement.cy=plainCropY;placement.angle=settings.angle;
    placement.sourceOffsetX=settings.sourceOffsetX;placement.sourceOffsetY=settings.sourceOffsetY;
    if(composeMargins) {
        settings.cx=settings.cy=fx.transformProtocol?1.f:1.f/audioZoom;
        settings.angle=0;settings.sourceOffsetX=settings.sourceOffsetY=settings.originalOffsetX=settings.originalOffsetY=0;
        plainCropX=plainCropY=1;
    }
    settings.intensity=fx.intensity;settings.brightness=fx.brightness;settings.contrast=fx.contrast;settings.saturation=fx.saturation;
    settings.vignette=fx.vignette?fx.vignetteStrength:0;settings.glow=fx.glow?fx.glowStrength:0;
    settings.px=1/viewport.Width;settings.py=1/viewport.Height;
    settings.threshold=fx.glowThreshold;settings.rgb=fx.rgbSplit?std::min(fx.rgbOffset,(width*.1f)*1080.f/std::max(1,height)):0;settings.soft=0;
    if(fx.blur&&fx.blurRadius>0&&fx.blurStrength>0)ConfigureBlurSampling(settings,fx.blurSamples,fx.blurMethod);
    settings.scan=fx.scanlines?fx.scanlineStrength:0;settings.density=fx.scanlineDensity;settings.noise=fx.noise?fx.noiseStrength:0;
    settings.transfer=(float)frame.transfer;settings.frameIndex=frame.frameIndex;settings.outputHeight=(float)height;
    // Reduce animation phase in double precision so long-running wallpapers retain subpixel motion.
    settings.scanPhase=fx.scanlines&&fx.scanlineMotion?(float)std::fmod(mappedPhase(AudioParameterId::ScanlineSpeed,scanTime*fx.scanlineSpeed),1.0):0;
    settings.glitch=fx.glitch?fx.glitchStrength:0;settings.coverage=fx.glitchCoverage;
    settings.glitchTick=(uint32_t)std::fmod(std::floor(glitchTime*fx.glitchRate),4294967296.0);
    settings.dotShape=(float)fx.dotShape;settings.rippleShape=(float)fx.rippleDotShape;
    settings.dotRotation=fx.dotRotation*3.14159265359f/180;settings.rippleRotation=fx.rippleDotRotation*3.14159265359f/180;
    settings.dots=fx.dots?fx.dotStrength:0;settings.spacing=fx.dotSpacing;settings.size=fx.dotSize;settings.dotGlow=fx.dots&&fx.dotGlow?fx.dotGlowStrength:0;
    settings.dotPhase=fx.dots&&fx.dotMotion?(float)std::fmod(mappedPhase(AudioParameterId::DotSpeed,dotTime*fx.dotSpeed),1.0):0;settings.pulse=fx.dots&&fx.dotMotion?fx.dotPulse:0;
    settings.glitchRgb=fx.glitch&&fx.glitchRgbSplit?fx.glitchRgbOffset:0;
    settings.groupCount=fx.glitch&&fx.glitchCluster?(uint32_t)(fx.clusterSmallCount+1):0;
    settings.clusterBlend=fx.clusterBlend;
    if(glitchTrigger.linked) {
        const float gain=glitchTrigger.Playing(glitchTrigger.duration)?fx.audioAmount:0;
        settings.glitch*=gain;settings.clusterBlend*=gain;
        if(gain==0)settings.groupCount=0;
        glitchTime=glitchTrigger.beat?glitchTrigger.beat->beatAge+static_cast<double>(glitchTrigger.beat->beats%65536)*1024:0;
        settings.glitchTick=glitchTrigger.beat?static_cast<uint32_t>(glitchTrigger.beat->beats):0;
    }
    auto hash=[](uint32_t seed) {seed=(seed^(seed>>16u))*2246822519u;seed=(seed^(seed>>13u))*3266489917u;seed^=seed>>16u;return (seed&65535u)/65536.0f;};
    auto hue=[&](bool enabled,bool random,float degrees,uint32_t seed) {
        return enabled?(random?hash(seed)-.5f:degrees/360.0f):0.0f;
    };
    settings.glitchHue=hue(fx.glitchColorShift,false,fx.glitchHueShift,0);
    settings.smallRandomHue=fx.glitchColorShift&&fx.glitchRandomHue?1u:0u;
    // Burst starts share the continuous video clock. Pause holds the current
    // phase and video loops never restart this schedule.
    auto burst=GlitchBurstAt(frame.effectTime,fx.largeGlitchInterval,fx.largeGlitchRandomize,fx.largeGlitchVariation);
    double burstCycle=burst.cycle,burstPhase=burst.phase;
    uint32_t burstId=(uint32_t)std::fmod(burstCycle,4294967296.0);
    bool bursting=false;
    if(largeTrigger.linked) {
        burstId=largeTrigger.beat?static_cast<uint32_t>(largeTrigger.beat->beats)^0xA511E9B3u:0;
        burstCycle=largeTrigger.beat?static_cast<double>(largeTrigger.beat->beats):0;
        burstPhase=largeTrigger.beat?largeTrigger.beat->beatAge:1e9;
        VaryLargeGlitch(fx,burstId);bursting=largeTrigger.Playing(fx.largeGlitchDuration);
        fx.largeGlitchBlend*=fx.audioAmount;
    } else {
    VideoEffects timedFx=fx;VaryLargeGlitch(timedFx,burstId);
    // Keep the periodic effect visible even with a silent live audio source.
    // Audio events have a separate seed namespace and can add an earlier burst.
    const uint32_t audioBurstId=(uint32_t)audio.burst^0xA511E9B3u;
    VideoEffects audioFx=fx;VaryLargeGlitch(audioFx,audioBurstId);
    auto burstSource=ChooseGlitchBurst(burstPhase,timedFx.largeGlitchDuration,
        audioActive&&fx.audioLegacy&&fx.audioGlitch,audio.burst,audio.largePhase,audioFx.largeGlitchDuration);
    if(burstSource==GlitchBurstSource::Audio) {
        fx=audioFx;burstId=audioBurstId;burstCycle=(double)audio.burst;burstPhase=audio.largePhase;
    } else fx=timedFx;
    bursting=burstSource!=GlitchBurstSource::None;
    // A parameter beat can start a burst outside the ordinary periodic window.
    if(audioActive)for(const auto& rule:fx.audioRules)if(rule.enabled&&rule.response==1&&(rule.parameter==12||(rule.parameter>=29&&rule.parameter<=32))) {
        auto mapped=audio.Mapping(rule.parameter);
        if(mapped&&mapped->beats>0&&(mapped->beatAge<fx.largeGlitchDuration||mapped->signal>.001)) {
            bursting=true;burstId=static_cast<uint32_t>(mapped->beats)^0xA511E9B3u;burstCycle=static_cast<double>(mapped->beats);burstPhase=mapped->beatAge;break;
        }
    }
    }
    settings.largeStrength=fx.largeGlitch&&fx.largeGlitchBlend>0&&bursting?fx.largeGlitchStrength:0;
    settings.largeBlend=fx.largeGlitchBlend;settings.largeRgb=fx.largeGlitchRgbOffset;settings.largeMode=(uint32_t)fx.largeGlitchMode;
    settings.largeTick=burstId*131u+(uint32_t)std::fmod(std::floor(burstPhase*fx.largeGlitchRate),4294967296.0);
    settings.largeSatelliteCount=settings.largeStrength>0&&fx.largeGlitchSmallStrength>0?(uint32_t)fx.largeGlitchSmallCount:0;
    // Traverse every region of the frame rather than sampling only inset centers.
    // The coprime stride covers all nine anchors, with eight on the perimeter.
    uint32_t zone=(uint32_t)std::fmod(burstCycle*5.0+2.0,9.0),column=zone%3,row=zone/3;
    uint32_t largeSeed=burstId*1973u+17027u;
    uint32_t regionMode=(uint32_t)fx.largeGlitchMode;
    if(regionMode==3)regionMode=row!=1&&column!=1?2u:(row!=1?0u:(column!=1?1u:(uint32_t)(hash(largeSeed+29u)*3)));
    settings.largeMode=regionMode;
    settings.largeHue=hue(fx.largeGlitchColorShift,fx.largeGlitchRandomHue,fx.largeGlitchHueShift,largeSeed+491u);
    // Width * height remains the main region's requested 20..60% visible area.
    float largeW=1,largeH=fx.largeGlitchArea;
    if(regionMode==1) {largeW=fx.largeGlitchArea;largeH=1;}
    else if(regionMode==2) {largeW=std::sqrt(fx.largeGlitchArea);largeH=largeW;}
    settings.largeHalfX=largeW*.5f;settings.largeHalfY=largeH*.5f;
    auto place=[&](float extent,uint32_t anchor,uint32_t seed) {
        if(anchor==0)return extent*.5f;
        if(anchor==2)return 1-extent*.5f;
        return .5f+(hash(seed)-.5f)*std::min(.10f,(1-extent)*.35f);
    };
    settings.largeCenterX=place(largeW,column,largeSeed+97u);
    settings.largeCenterY=place(largeH,row,largeSeed+331u);
    float jump=(.04f+.10f*hash(largeSeed+71u))*fx.largeGlitchStrength;
    settings.largeShiftX=(hash(largeSeed+13u)<.5f?-1:1)*jump;
    settings.largeShiftY=(hash(largeSeed+19u)*2-1)*jump*.35f;
    if(regionMode==1)std::swap(settings.largeShiftX,settings.largeShiftY);
    // Near an edge/corner put companions only on sides with visible room.
    uint32_t availableSides[4]{},sideCount=0;
    if(settings.largeCenterY-settings.largeHalfY>=.045f)availableSides[sideCount++]=0;
    if(1-settings.largeCenterY-settings.largeHalfY>=.045f)availableSides[sideCount++]=1;
    if(settings.largeCenterX-settings.largeHalfX>=.055f)availableSides[sideCount++]=2;
    if(1-settings.largeCenterX-settings.largeHalfX>=.055f)availableSides[sideCount++]=3;
    if(sideCount==0)settings.largeSatelliteCount=0;
    for(uint32_t i=0;i<settings.largeSatelliteCount;++i) {
        auto& patch=settings.largeSatellites[i];uint32_t seed=largeSeed+i*104729u;
        uint32_t slot=i%sideCount,side=availableSides[slot]; // top, bottom, left, right
        uint32_t along=i/sideCount,onSide=(settings.largeSatelliteCount-1-slot)/sideCount+1;
        float position=((float)along+.5f+(hash(seed+13u)-.5f)*.5f)/(float)onSide;
        bool horizontal=side<2;
        float w=horizontal?.045f+.075f*hash(seed+29u):.012f+.022f*hash(seed+29u);
        float h=horizontal?.006f+.014f*hash(seed+53u):.035f+.055f*hash(seed+53u);
        float gap=.006f+.012f*hash(seed+83u);patch.hx=w*.5f;patch.hy=h*.5f;
        if(horizontal) {
            patch.cx=settings.largeCenterX+(position-.5f)*largeW;
            patch.cy=settings.largeCenterY+(side==0?-1:1)*(settings.largeHalfY+gap+patch.hy);
        } else {
            patch.cx=settings.largeCenterX+(side==2?-1:1)*(settings.largeHalfX+gap+patch.hx);
            patch.cy=settings.largeCenterY+(position-.5f)*largeH;
        }
        patch.cx=std::clamp(patch.cx,patch.hx,1-patch.hx);patch.cy=std::clamp(patch.cy,patch.hy,1-patch.hy);
        uint32_t motionSeed=seed+settings.largeTick*26699u;
        patch.shiftX=(hash(motionSeed+71u)*2-1)*.035f*fx.largeGlitchStrength;
        patch.shiftY=(hash(motionSeed+113u)*2-1)*.01f*fx.largeGlitchStrength;
        patch.rgb=fx.largeGlitchRgbOffset*(.35f+.5f*hash(seed+151u))*fx.largeGlitchStrength;
        patch.weight=(.55f+.45f*hash(motionSeed+197u))*fx.largeGlitchSmallStrength;
        // Color is stable throughout a burst; different pieces use independent seeds.
        patch.hue=hue(fx.glitchColorShift,fx.glitchRandomHue,fx.glitchHueShift,seed+709u);
    }
    uint32_t anchor=(uint32_t)std::fmod(std::floor(glitchTime*(fx.layers[0].rate>0?.4:0)),4294967296.0)*1973u;
    float mainX=.18f+.64f*hash(anchor+97u),mainY=.18f+.64f*hash(anchor+331u);
    if(std::abs(mainX-.5f)<.18f&&std::abs(mainY-.5f)<.22f)mainX=mainX<.5f?.24f:.76f;
    for(uint32_t i=0;i<settings.groupCount;++i) {
        const auto& layer=fx.layers[i];auto& group=settings.groups[i];
        group.strength=layer.Active()?layer.strength:0;group.coverage=layer.coverage;group.size=layer.size;group.rgb=layer.rgbOffset;
        float regionWidth=fx.clusterSize*(i==0?1.0f:(.14f+.13f*hash(i*113u)));
        group.hx=regionWidth*.5f;group.hy=regionWidth*(i==0?.18f:.25f);
        group.cx=std::clamp(mainX+(i==0?0:(hash(i*391u+anchor)*2-1)*.27f),group.hx+.01f, .99f-group.hx);
        group.cy=std::clamp(mainY+(i==0?0:(hash(i*719u+anchor)*2-1)*.2f),group.hy+.01f,.99f-group.hy);
        group.tick=(uint32_t)std::fmod(std::floor(glitchTime*layer.rate),4294967296.0);group.mode=(uint32_t)layer.mode;group.seed=911u+i*104729u;
        group.hue=hue(fx.glitchColorShift,fx.glitchRandomHue,fx.glitchHueShift,group.tick*26699u+group.seed+491u);
    }
    settings.ripple=fx.dotRipple&&fx.rippleDotGlow?fx.dotRippleStrength:0;
    settings.rippleDots=fx.dotRipple?fx.rippleDotStrength:0;settings.rippleSpacing=fx.rippleDotSpacing;settings.rippleSize=fx.rippleDotSize;
    settings.bendAmplitude=fx.scanlines&&fx.scanlineBends?fx.scanBendAmplitude:0;settings.bendSpan=fx.scanBendSpan;settings.bendDensity=fx.scanBendDensity;settings.rippleWidth=fx.dotRippleWidth;settings.rippleCount=(float)fx.dotRippleCount;
    settings.ripplePhase=(float)std::fmod(mappedPhase(AudioParameterId::DotRippleSpeed,dotTime*fx.dotRippleSpeed)*fx.dotRippleCount,1.0);
    if(rippleTrigger.linked) {
        settings.rippleDots*=rippleTrigger.Playing(rippleTrigger.duration)?fx.audioAmount:0;
        // Negative phase denotes one outward wave; ordinary periodic phases remain unchanged.
        settings.ripplePhase=-(1+rippleTrigger.Progress(rippleTrigger.duration)*(1+settings.rippleWidth));
    }
    if(fx.scanlines&&fx.scanlineMotion&&fx.scanlineSpeed!=0) {
        settings.scanExposure=(float)(fx.scanlineSpeed*(1+(audioActive&&fx.audioLegacy&&fx.audioScanlines?(.25f*energy+1.5f*pulse)*fx.audioAmount*3:0))*std::clamp(frame.duration,0.0,1.0));
        double sweep=mappedPhase(AudioParameterId::ScanlineSpeed,scanTime*fx.scanlineSpeed)*1080.0/fx.scanlineDensity/height;
        settings.scanSweep=(float)(sweep-std::floor(sweep));
    }
    settings.blurMix=fx.blur&&fx.blurRadius>0&&(!fx.blurMotion||fx.blurCoverage>0)?fx.blurStrength:0;
    settings.blurMotion=fx.blurMotion?1u:0u;settings.blurMotionMode=(uint32_t)fx.blurMotionMode;
    settings.blurMotionPhase=(float)(std::fmod(blurTime*fx.blurMotionSpeed,1.0)*6.283185307179586);
    settings.blurCoverage=fx.blurCoverage;settings.blurScale=fx.blurScale;settings.blurFeather=fx.blurFeather;settings.blurAngle=(float)(fx.blurAngle*3.141592653589793/180);
    settings.distortionStrength=fx.distortion?fx.distortionStrength:0;settings.distortionScale=fx.distortionScale;settings.distortionMode=(uint32_t)fx.distortionMode;
    settings.distortionCenterX=fx.distortionCenterX;settings.distortionCenterY=fx.distortionCenterY;
    settings.distortionPhase=fx.distortion&&fx.distortionMotion?(float)(std::fmod(mappedPhase(AudioParameterId::DistortionSpeed,distortionTime*fx.distortionSpeed),1.0)*6.283185307179586):0;
    settings.scanWidth=fx.scanlineWidth;settings.scanSoftness=fx.scanlineSoftness;settings.scanAngle=(float)(fx.scanlineAngle*3.141592653589793/180);
    auto effectPhase=[&](double rate) {return (float)std::fmod(frame.effectTime*rate,1.0);};
    constexpr float tau=6.28318530718f,radians=3.14159265359f/180;
    settings.zoomMix=fx.zoomTunnel.enabled?fx.zoomTunnel.strength:0;settings.zoomPhase=effectPhase(1.0/fx.zoomTunnel.period);
    if(zoomTrigger.linked) {settings.zoomMix*=zoomTrigger.Playing(zoomTrigger.duration)?fx.audioAmount:0;settings.zoomPhase=zoomTrigger.Progress(zoomTrigger.duration);}
    settings.zoomScale=fx.zoomTunnel.zoom;settings.zoomCenterX=fx.zoomTunnel.centerX;settings.zoomCenterY=fx.zoomTunnel.centerY;
    settings.rainMix=fx.rainRipples.enabled?fx.rainRipples.strength:0;settings.rainPhase=(float)std::fmod(mappedPhase(AudioParameterId::RainRipples_Speed,frame.effectTime*fx.rainRipples.speed),1.0);
    const double rainDuration=1/std::max(.05,static_cast<double>(fx.rainRipples.speed));
    const float rainTriggerGain=!particleTrigger.linked?1.f:particleTrigger.Playing(rainDuration)?fx.audioAmount:0.f;
    if(particleTrigger.linked) {settings.rainMix*=rainTriggerGain;settings.rainPhase=-(1+particleTrigger.Progress(rainDuration));}
    settings.rainDensity=fx.rainRipples.density;settings.rainRadius=fx.rainRipples.radius;settings.rainWidth=fx.rainRipples.width;
    settings.waterMix=fx.waterWave.enabled?fx.waterWave.strength:0;settings.waterPhase=(float)std::fmod(mappedPhase(AudioParameterId::WaterWave_Speed,frame.effectTime*fx.waterWave.speed),1.0)*tau;
    settings.waterAmplitude=fx.waterWave.amplitude;settings.waterFrequency=fx.waterWave.frequency;settings.waterAngle=fx.waterWave.angle*radians;
    settings.edgeMix=fx.edgeOutline.enabled?fx.edgeOutline.strength:0;settings.edgeWidth=fx.edgeOutline.width;
    settings.edgeThreshold=fx.edgeOutline.threshold;settings.edgeMode=(float)fx.edgeOutline.mode;
    settings.edgeR=((fx.edgeOutline.color>>16)&255)/255.0f;settings.edgeG=((fx.edgeOutline.color>>8)&255)/255.0f;settings.edgeB=(fx.edgeOutline.color&255)/255.0f;
    settings.pixelMix=fx.pixelate.enabled?fx.pixelate.strength:0;settings.pixelBlock=fx.pixelate.blockSize;
    settings.sunMix=fx.sunPrism.enabled?fx.sunPrism.strength:0;settings.sunPhase=(float)std::fmod(mappedPhase(AudioParameterId::SunPrism_Speed,frame.effectTime*fx.sunPrism.speed),1.0)*tau;
    settings.sunAngle=fx.sunPrism.angle*radians+effectPhase(fx.sunRotationSpeed/360.f)*tau;settings.sunWidth=fx.sunPrism.width;settings.sunPosition=fx.sunPrism.position;
    settings.lightFlare=PrepareLightFlare(fx.lightFlare,frame.effectTime);
    settings.transform=PrepareTransform(fx.transform,audioZoom,settings.py/settings.px);
    EnsureMaskImages(fx);
    auto& a=settings.advanced;
    auto color=[](unsigned hex,float* rgb) {rgb[0]=((hex>>16)&255)/255.f;rgb[1]=((hex>>8)&255)/255.f;rgb[2]=(hex&255)/255.f;};
    auto& particles=settings.particles;
    auto prepareParticles=[&](const ParticleOptions& particle,int layer) {
    particles.mix=particle.enabled?particle.opacity:0;particles.style=(float)particle.style;
    particles.count=(float)std::min(particle.count,particle.quality==0?128:particle.quality==1?512:2000);
    particles.size=particle.size;particles.centerX=particle.emitter==0&&!particle.localRegion?.5f:particle.centerX;particles.centerY=particle.emitter==0&&!particle.localRegion?.5f:particle.centerY;
    particles.width=particle.emitter==0&&!particle.localRegion?1:particle.width;particles.height=particle.emitter==0&&!particle.localRegion?1:particle.height;
    double period=particle.lifetime+(particle.emission==1?particle.burstInterval:0);
    particles.time=(float)std::fmod(std::max(0.0,frame.effectTime),period);particles.lifetime=particle.lifetime;
    particles.padding=(float)std::fmod(std::floor(std::max(0.0,frame.effectTime)/period),65536.0);
    particles.speed=particle.speed;particles.direction=particle.direction*radians;color(particle.color,particles.color);particles.spread=particle.spread*radians;
    particles.sway=particle.sway;particles.rotation=particle.rotation*radians;particles.twinkle=particle.twinkle;particles.fade=(float)particle.fade;
    particles.emission=(float)particle.emission;particles.emitter=(float)particle.emitter;particles.gravity=particle.gravity;particles.sizeRandom=particle.sizeRandom;
    particles.speedRandom=particle.speedRandom;particles.burstInterval=particle.burstInterval;particles.randomRotation=particle.randomRotation?1.f:0.f;
    EnsureParticleImage(particle,layer);particles.customImage=particleImage[layer]?1.f:0.f;
    particles.randomMask=(float)particle.randomMask;particles.builtinImage=particleBuiltins?1.f:0.f;particles.seedOffset=(float)(layer*13007);
    };prepareParticles(fx.particles,0);
    a.noiseColor=(float)fx.coloredNoise;a.rippleColorMode=(float)fx.rippleColorMode;color(fx.rippleColor,a.rippleColor);
    a.rippleHueMin=fx.rippleHueMin/360;a.rippleHueSpan=(fx.rippleHueMax>=fx.rippleHueMin?fx.rippleHueMax-fx.rippleHueMin:360-fx.rippleHueMin+fx.rippleHueMax)/360;
    a.rippleSatMin=fx.rippleSatMin;a.rippleSatMax=fx.rippleSatMax;a.rippleValueMin=fx.rippleValueMin;a.rippleValueMax=fx.rippleValueMax;
    a.rippleColorTick=(float)std::fmod(std::floor(frame.effectTime*fx.rippleColorRate),65536.0);
    a.blurMethod=(float)fx.blurMethod;a.blurDirection=fx.blurDirection*radians;a.blurCenterX=fx.blurCenterX;a.blurCenterY=fx.blurCenterY;
    a.rainOutlineMix=fx.rainRipples.enabled&&fx.rainOutline?fx.rainOutlineStrength*rainTriggerGain:0;a.rainOutlineWidth=fx.rainOutlineWidth;color(fx.rainColor,a.rainColor);
    a.sunShape=(float)fx.sunShape;a.sunCurvature=fx.sunCurvature;a.sunArc=fx.sunArc*radians;a.sunRadius=fx.sunRadius;a.sunRepeat=fx.sunRepeat;a.sunColorCount=(float)fx.sunColors.size();
    for(size_t i=0;i<fx.sunColors.size();i++) {color(fx.sunColors[i],a.sunColors[i]);a.sunColors[i][3]=fx.sunPositions.size()==fx.sunColors.size()?static_cast<float>(fx.sunPositions[i]):(float)i/(fx.sunColors.size()-1);}
    a.sunCenterX=fx.sunCenterX+fx.sunMoveX*std::sin(effectPhase(fx.sunMoveSpeedX)*tau);
    a.sunCenterY=fx.sunCenterY+(fx.sunMoveSpeedY==0?0:fx.sunMoveY*std::sin(effectPhase(fx.sunMoveSpeedY)*tau+fx.sunMovePhase*radians));
    a.sunConeAngle=fx.sunConeAngle*radians;a.maskCount=(float)fx.masks.size();
    a.sunConeCorner=(float)fx.sunConeCorner;a.sunConeDirection=fx.sunConeDirection*radians;a.sunConeLength=fx.sunConeLength;a.sunConeSwing=fx.sunConeSwing*radians;
    a.maskPreview=(float)(fx.maskPreview+1);
    for(size_t i=0;i<fx.masks.size();i++) {
        const auto& m=fx.masks[i];auto& c=a.masks[i];c.rect[0]=m.x;c.rect[1]=m.y;c.rect[2]=m.width;c.rect[3]=m.height;
        c.params[0]=(float)m.shape;c.params[1]=m.rotation*radians;c.params[2]=m.feather;c.params[3]=(float)m.invert;
        c.dynamics[0]=m.corner;c.dynamics[1]=m.shape>=6?effectPhase(m.speed)*tau:0;c.dynamics[2]=m.coverage;c.dynamics[3]=m.scale;
    }
    for(size_t i=0;i<fx.effectMasks.size();i++)a.maskIndices[i/4][i%4]=(float)(fx.effectMasks[i]+1);
    settings.originalCropX=orderedRequested?plainCropX:settings.cx;settings.originalCropY=orderedRequested?plainCropY:settings.cy;settings.originalAngle=settings.angle;
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->VSSetShader(vertex.Get(),nullptr,0);
    auto* cb=constants.Get();context->VSSetConstantBuffers(0,1,&cb);context->PSSetConstantBuffers(0,1,&cb);auto* smp=sampler.Get();context->PSSetSamplers(0,1,&smp);
    auto* source=view.Get();
    if(composeMargins) {
        if(!canvasTarget.texture||canvasW!=width||canvasH!=height) {
            canvasTarget={};
            D3D11_TEXTURE2D_DESC desc{};
            desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=desc.SampleDesc.Count=1;
            desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
            desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
            Check(device->CreateTexture2D(&desc,nullptr,&canvasTarget.texture),"Create wallpaper canvas");
            Check(device->CreateRenderTargetView(canvasTarget.texture.Get(),nullptr,&canvasTarget.target),"Create wallpaper canvas target");
            Check(device->CreateShaderResourceView(canvasTarget.texture.Get(),nullptr,&canvasTarget.view),"Create wallpaper canvas view");
            canvasW=width;canvasH=height;
        }
        const float black[4]={0,0,0,1};
        context->ClearRenderTargetView(canvasTarget.target.Get(),black);
        context->UpdateSubresource(constants.Get(),0,nullptr,&placement,0,0);
        Pass(canvasTarget.target.Get(),pixel.Get(),source,nullptr,mediaViewport);
        source=canvasTarget.view.Get();
    } else if(canvasTarget.texture) {canvasTarget={};canvasW=canvasH=0;}
    context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
    ID3D11ShaderResourceView* softened=nullptr;
    if(gpuSample) {gpuSample->blur=settings.blurMix>0;context->End(gpuSample->stamps[2].Get());}
    if(!fx.masks.empty()) {
        EnsureMaskLayers();
        float maskAspect=settings.py/settings.px;
        if(renderedMaskImageKey!=maskImageKey||maskLastAspect!=maskAspect) {maskValid.fill(false);renderedMaskImageKey=maskImageKey;maskLastAspect=maskAspect;}
        D3D11_VIEWPORT maskViewport{0,0,512,512,0,1};
        for(size_t i=0;i<fx.masks.size();i++) {
            if(maskValid[i]&&std::memcmp(&maskLast[i],&a.masks[i],sizeof(MaskConstants))==0)continue;
            settings.originalPadding=(float)i;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
            Pass(maskLayerTargets[i].Get(),maskPixel.Get(),view.Get(),nullptr,maskViewport);
            maskLast[i]=a.masks[i];maskValid[i]=true;
        }
        settings.originalPadding=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
    }
    auto drawParticleLayer=[&](int layer) {
        const auto& options=layer==0?fx.particles:fx.subParticles;
        prepareParticles(options,layer);
        if(particles.mix<=0||fx.intensity<=0||fx.maskPreview>=0)return;
        if(particleTrigger.linked&&(!audioActive||!particleTrigger.beat||audio.particleTriggers.count==0))return;
        context->VSSetShader(particleVertex.Get(),nullptr,0);context->PSSetShader(particlePixel.Get(),nullptr,0);
        ID3D11ShaderResourceView* sprites[2]={particleImage[layer].Get(),particleBuiltins.Get()};context->PSSetShaderResources(7,2,sprites);context->OMSetBlendState(particleBlend.Get(),nullptr,0xffffffff);
        auto drawBatch=[&] {context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);context->DrawInstanced(6,(UINT)particles.count,0,0);++LastPasses;};
        if(!particleTrigger.linked)drawBatch();
        else {
            // Keep up to eight living bursts, within the existing quality budget.
            // A fixed per-burst cap avoids changing living particles as batches expire.
            const auto& history=audio.particleTriggers;
            const int budget=options.quality==0?128:options.quality==1?512:2000;
            particles.count=static_cast<float>(std::min(options.count,budget/static_cast<int>(AudioTriggerHistory::Capacity)));
            particles.mix*=fx.audioAmount;particles.emission=1;particles.burstInterval=0;
            const auto count=std::min(history.count,static_cast<uint64_t>(AudioTriggerHistory::Capacity));
            for(uint64_t offset=count;offset>0;--offset) {
                const auto birth=history.count-offset;const double age=history.ages[birth%AudioTriggerHistory::Capacity];
                if(age>=options.lifetime)continue;
                particles.time=static_cast<float>(age);particles.padding=static_cast<float>(birth%65536);
                particles.seedOffset=static_cast<float>(layer*13007+(birth%65536)*17);
                drawBatch();
            }
        }
        context->OMSetBlendState(nullptr,nullptr,0xffffffff);context->VSSetShader(vertex.Get(),nullptr,0);
        ID3D11ShaderResourceView* noSprites[2]={nullptr,nullptr};context->PSSetShaderResources(7,2,noSprites);
    };
    bool ordered=orderedRequested;
    bool geometryActive=false;
    if(ordered)DrawOrderedStack(settings,fx,viewport,source,drawParticleLayer,gpuSample);
    else {
    ID3D11ShaderResourceView* effectInput=source;
    geometryActive=settings.zoomMix>0||settings.rainMix>0||(settings.waterMix>0&&settings.waterAmplitude>0)||settings.pixelMix>0||a.rainOutlineMix>0;
    if(geometryActive) {
        EnsureGeometry(std::max(1,(int)std::ceil(viewport.Width)),std::max(1,(int)std::ceil(viewport.Height)));
        D3D11_VIEWPORT prepared{0,0,(float)geometryW,(float)geometryH,0,1};
        Pass(geometryTarget.target.Get(),geometryPixel.Get(),source,nullptr,prepared);
        effectInput=geometryTarget.view.Get();
        // Geometry already applied source crop/rotation. Subsequent effects work
        // in viewport space; the final blend still samples the untouched source.
        settings.cx=settings.cy=1;settings.angle=0;settings.sourceOffsetX=settings.sourceOffsetY=0;
        context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
    }
    if(gpuSample)context->End(gpuSample->stamps[3].Get());
    if(settings.blurMix>0) {
        float reduction=1.0f;EnsureVideoBlur(std::max(1,(int)std::ceil(viewport.Width/reduction)),std::max(1,(int)std::ceil(viewport.Height/reduction)));
        D3D11_VIEWPORT reduced{0,0,(float)blurW,(float)blurH,0,1};float radius=fx.blurRadius*height/1080.0f;a.filterMethod=(float)fx.blurMethod;
        settings.bx=radius/viewport.Width;settings.by=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
        Pass(videoBlurTargets[0].target.Get(),fx.blurMethod<=1?videoBlurPixel.Get():alternateBlurPixel.Get(),effectInput,nullptr,reduced);
        settings.bx=0;settings.by=radius/viewport.Height;settings.blurMaskPass=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
        if(fx.blurMethod<=1) {Pass(videoBlurTargets[1].target.Get(),videoBlurVerticalPixel.Get(),videoBlurTargets[0].view.Get(),nullptr,reduced);softened=videoBlurTargets[1].view.Get();} else softened=videoBlurTargets[0].view.Get();
        settings.blurMaskPass=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
    }
    a.filterMethod=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
    if(gpuSample) {gpuSample->glow=settings.glow>0;context->End(gpuSample->stamps[4].Get());}
    if(settings.glow>0) {
        EnsureGlow(std::max(1,(int)std::ceil(viewport.Width/4)),std::max(1,(int)std::ceil(viewport.Height/4)));
        D3D11_VIEWPORT reducedViewport{0,0,(float)glowW,(float)glowH,0,1};
        Pass(glowTargets[0].target.Get(),extractPixel.Get(),effectInput,nullptr,reducedViewport,softened,nullptr);
        settings.bx=1.0f/glowW;settings.by=0;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
        Pass(glowTargets[1].target.Get(),blurPixel.Get(),glowTargets[0].view.Get(),nullptr,reducedViewport);
        settings.bx=0;settings.by=1.0f/glowH;context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
        Pass(glowTargets[0].target.Get(),blurPixel.Get(),glowTargets[1].view.Get(),nullptr,reducedViewport);
    }
    if(gpuSample)context->End(gpuSample->stamps[5].Get());
    float black[4]={0,0,0,1};context->ClearRenderTargetView(target.Get(),black);
    Pass(target.Get(),fx.Active()?stylePixel.Get():pixel.Get(),effectInput,settings.glow>0?glowTargets[0].view.Get():nullptr,viewport,softened,source);
    drawParticleLayer(0);drawParticleLayer(1);
    }
    if((!fx.particles.enabled||fx.particles.builtins=="-")&&(!fx.subParticles.enabled||fx.subParticles.builtins=="-")){particleBuiltins.Reset();particleBuiltinsKey.clear();}
    ID3D11ShaderResourceView* empty[7]={nullptr,nullptr,nullptr,nullptr,nullptr,nullptr,nullptr};context->PSSetShaderResources(0,7,empty);
    if(fx.masks.empty()&&maskLayerTexture) {for(auto& layer:maskLayerTargets)layer.Reset();maskLayerView.Reset();maskLayerTexture.Reset();maskValid.fill(false);}
    if(!ordered&&stackW!=0) {for(auto& output:stackTargets)output={};stackW=stackH=0;}
    if(!geometryActive&&geometryW!=0) {geometryTarget={};geometryW=geometryH=0;}
    if(settings.glow==0&&glowW!=0) {for(auto& output:glowTargets)output={};glowW=glowH=0;}
    if(settings.blurMix==0&&blurW!=0) {for(auto& output:videoBlurTargets)output={};blurW=blurH=0;}
    if(gpuSample) {
        context->End(gpuSample->stamps[6].Get());context->End(gpuSample->disjoint.Get());gpuSample->pending=true;
    }
    if(!requested.packageShaders.empty()||packageInput||!packageShaderCache.empty())DrawPackages(compareOriginal?fx:requested,static_cast<float>(frame.effectTime));
    if(TraceFrames)InspectFrame();
    auto presentStart=performanceEnabled?std::chrono::steady_clock::now():std::chrono::steady_clock::time_point{};
    if(performanceEnabled)LastSubmitMs=std::chrono::duration<double,std::milli>(presentStart-submitStart).count();
    if(present) {LastPresent=chain->Present(1,0);Check(LastPresent,"Present video frame");}
    if(performanceEnabled)LastPresentMs=present?std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-presentStart).count():0;
}
// Package ABI v1: source t0/s0, strength, seconds and inverse output size at b1.
void Renderer::DrawPackages(const VideoEffects& fx,float seconds) {
    if(fx.intensity<=0||fx.packageShaders.empty()){
        packageShaderCache.clear();packageInput.Reset();packageView.Reset();packageConstants.Reset();return;
    }
    std::erase_if(packageShaderCache,[&](const LoadedShader& loaded){
        return std::none_of(fx.packageShaders.begin(),fx.packageShaders.end(),[&](const auto& item){
            return loaded.file==item.file&&loaded.generation==item.generation;
        });
    });
    ComPtr<ID3D11Texture2D> back;
    Check(chain->GetBuffer(0,IID_PPV_ARGS(&back)),"Open package input");
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    if(packageInput){D3D11_TEXTURE2D_DESC old{};packageInput->GetDesc(&old);if(old.Width!=desc.Width||old.Height!=desc.Height){packageInput.Reset();packageView.Reset();}}
    for(const auto& item:fx.packageShaders){
        auto it=std::find_if(packageShaderCache.begin(),packageShaderCache.end(),[&](const auto& l){return l.generation==item.generation&&l.file==item.file;});
        if(it==packageShaderCache.end()){
            LoadedShader load{item.generation,item.file,{}};
            wchar_t root[32768];auto n=GetEnvironmentVariableW(item.generation=="builtin"?L"ZZZWALLPAPER_BASE_DIRECTORY":L"ZZZWALLPAPER_FEATURE_PACK_ROOT",root,32768);
            if(n>0&&n<32768){
                auto folder=item.generation=="builtin"?std::filesystem::path(root):std::filesystem::path(root)/L"versions"/item.generation;
                std::ifstream stream(folder/item.file,std::ios::binary|std::ios::ate);
                auto size=stream?static_cast<std::streamoff>(stream.tellg()):0;
                if(size>0&&size<=16*1024*1024){
                    std::vector<char> code(static_cast<size_t>(size));stream.seekg(0);
                    if(stream.read(code.data(),size))device->CreatePixelShader(code.data(),code.size(),nullptr,&load.shader);
                }
            }
            packageShaderCache.push_back(std::move(load));it=std::prev(packageShaderCache.end());
        }
        if(!it->shader)continue;
        if(!packageInput){
            desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;desc.MiscFlags=0;desc.CPUAccessFlags=0;desc.Usage=D3D11_USAGE_DEFAULT;
            Check(device->CreateTexture2D(&desc,nullptr,&packageInput),"Create package input");
            Check(device->CreateShaderResourceView(packageInput.Get(),nullptr,&packageView),"Create package view");
        }
        if(!packageConstants){
            D3D11_BUFFER_DESC b{};b.ByteWidth=16;b.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
            Check(device->CreateBuffer(&b,nullptr,&packageConstants),"Create package constants");
        }
        ID3D11ShaderResourceView* empty[8]{};context->PSSetShaderResources(0,8,empty);
        context->OMSetRenderTargets(0,nullptr,nullptr);context->CopyResource(packageInput.Get(),back.Get());
        float data[]={item.strength*fx.intensity,seconds,1.f/desc.Width,1.f/desc.Height};
        context->UpdateSubresource(packageConstants.Get(),0,nullptr,data,0,0);
        context->PSSetConstantBuffers(1,1,packageConstants.GetAddressOf());
        context->VSSetShader(vertex.Get(),nullptr,0);context->PSSetSamplers(0,1,sampler.GetAddressOf());
        context->OMSetBlendState(nullptr,nullptr,0xffffffff);
        Pass(target.Get(),it->shader.Get(),packageView.Get(),nullptr,{0,0,(float)desc.Width,(float)desc.Height,0,1});
    }
}
std::vector<uint8_t> Renderer::CaptureFrame() {
    // Read only our own image target. Reuse the staging texture for parameter previews.
    ComPtr<ID3D11Texture2D> back;Check(chain->GetBuffer(0,IID_PPV_ARGS(&back)),"Read render target");D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.MiscFlags=0;
    if(!imageReadback)Check(device->CreateTexture2D(&desc,nullptr,&imageReadback),"Create readback");
    context->CopyResource(imageReadback.Get(),back.Get());D3D11_MAPPED_SUBRESOURCE mapped{};Check(context->Map(imageReadback.Get(),0,D3D11_MAP_READ,0,&mapped),"Map render readback");
    std::vector<uint8_t> pixels(size_t(width)*height*4);
    for(int y=0;y<height;++y)std::memcpy(pixels.data()+size_t(y)*width*4,static_cast<const uint8_t*>(mapped.pData)+size_t(y)*mapped.RowPitch,size_t(width)*4);
    context->Unmap(imageReadback.Get(),0);return pixels;
}
void Renderer::SaveFrame(const std::wstring& path) {
    const auto pixels=CaptureFrame();
    BITMAPFILEHEADER header{};header.bfType=0x4D42;header.bfOffBits=sizeof(header)+sizeof(BITMAPINFOHEADER);header.bfSize=header.bfOffBits+width*height*4;
    BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=width;info.biHeight=-height;info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;
    std::ofstream file(std::filesystem::path(path),std::ios::binary);file.write((char*)&header,sizeof(header));file.write((char*)&info,sizeof(info));
    file.write(reinterpret_cast<const char*>(pixels.data()),pixels.size());if(!file)throw std::runtime_error("Cannot save rendered image frame");
}

void Renderer::CompletePresentation(bool composed) {
    ComPtr<ID3D11Query> complete;D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT,0};
    Check(device->CreateQuery(&desc,&complete),"Create presentation completion query");
    context->End(complete.Get());context->Flush();
    auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(3);
    HRESULT status;
    while((status=context->GetData(complete.Get(),nullptr,0,0))==S_FALSE) {
        if(std::chrono::steady_clock::now()>=deadline)throw std::runtime_error("First presentation timed out; keeping previous wallpaper");
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    Check(status,"Wait for first presentation");
    if(composed)Check(DwmFlush(),"Wait for desktop composition");
}
void Renderer::InspectFrame() {
    // Diagnostic readback of our own target before DISCARD Present, never the user's desktop.
    ComPtr<ID3D11Texture2D> back,copy;Check(chain->GetBuffer(0,IID_PPV_ARGS(&back)),"Read trace target");
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.MiscFlags=0;
    Check(device->CreateTexture2D(&desc,nullptr,&copy),"Create trace readback");context->CopyResource(copy.Get(),back.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};Check(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&mapped),"Map trace frame");
    uint64_t hash=14695981039346656037ull,sum=0;
    for(int y=0;y<height;++y) {
        auto* row=static_cast<const unsigned char*>(mapped.pData)+(size_t)y*mapped.RowPitch;
        for(int x=0;x<width;++x)for(int c=0;c<3;++c) {unsigned char value=row[x*4+c];hash=(hash^value)*1099511628211ull;sum+=value;}
    }
    context->Unmap(copy.Get(),0);LastHash=hash;LastMean=(double)sum/((double)width*height*3);
}
