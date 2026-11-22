#pragma once
#include <Windows.h>
#include <d3d11_1.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <array>
#include <stdexcept>
#include <functional>
#include <memory>
#include "../../Shared/MediaCore/VideoFrame.h"
#include "VideoEffects.h"
#include "AudioReactive.h"
using Microsoft::WRL::ComPtr;
struct RenderSettings;
struct HardwareFrameTransferError : std::runtime_error {
    HardwareFrameTransferError():std::runtime_error("GPU frame conversion is unavailable") { }
};
class Renderer {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> chain;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11VertexShader> particleVertex;
    ComPtr<ID3D11PixelShader> particlePixel;
    ComPtr<ID3D11BlendState> particleBlend;
    ComPtr<ID3D11ShaderResourceView> particleImage[2],particleBuiltins;
    std::string particleImageKey[2],particleBuiltinsKey;
    void EnsureParticleImage(const ParticleOptions& options,int layer);
    ComPtr<ID3D11PixelShader> stylePixel,extractPixel,blurPixel,videoBlurPixel,videoBlurVerticalPixel,transformPixel,geometryPixel,alternateBlurPixel,maskPixel,stackCompositePixel;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11Buffer> constants;
    struct LoadedShader {std::string generation,file;ComPtr<ID3D11PixelShader> shader;};
    std::vector<LoadedShader> packageShaderCache;
    ComPtr<ID3D11Texture2D> packageInput;
    ComPtr<ID3D11ShaderResourceView> packageView;
    ComPtr<ID3D11Buffer> packageConstants;
    void DrawPackages(const VideoEffects& effects,float time);
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11Texture2D> imageReadback;
    ComPtr<ID3D11ShaderResourceView> view;
    ComPtr<ID3D11VideoDevice> videoDevice;
    ComPtr<ID3D11VideoContext> videoContext;
    ComPtr<ID3D11VideoProcessorEnumerator> videoEnumerator;
    ComPtr<ID3D11VideoProcessor> videoProcessor;
    ComPtr<ID3D11VideoProcessorOutputView> videoOutput;
    bool UploadHardware(const VideoFrame& frame);
    int width=0,height=0,videoW=0,videoH=0;
    struct GlowTarget {ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> target;ComPtr<ID3D11ShaderResourceView> view;};
    ComPtr<ID3D11Texture2D> maskTexture;ComPtr<ID3D11ShaderResourceView> maskImages;std::string maskImageKey;
    void EnsureMaskImages(const VideoEffects& effects);
    ComPtr<ID3D11Texture2D> maskLayerTexture;ComPtr<ID3D11ShaderResourceView> maskLayerView;
    std::array<ComPtr<ID3D11RenderTargetView>,16> maskLayerTargets;
    std::array<MaskConstants,16> maskLast{};std::array<bool,16> maskValid{};
    std::string renderedMaskImageKey;float maskLastAspect=0;
    void EnsureMaskLayers();
    GlowTarget canvasTarget;int canvasW=0,canvasH=0;
    GlowTarget geometryTarget;int geometryW=0,geometryH=0;
    void EnsureGeometry(int w,int h);
    GlowTarget glowTargets[2];int glowW=0,glowH=0;
    GlowTarget videoBlurTargets[2];int blurW=0,blurH=0;
    GlowTarget stackTargets[2];int stackW=0,stackH=0;
    void EnsureStack(int w,int h);
    // Queries are optional and never waited on; busy slots are skipped.
    struct GpuQuerySlot {
        ComPtr<ID3D11Query> disjoint;
        std::array<ComPtr<ID3D11Query>,11> stamps;
        bool pending=false,blur=false,glow=false,ordered=false;
    };
    std::array<GpuQuerySlot,4> gpuQueries;
    bool performanceEnabled=false,gpuQueriesAvailable=false;
    uint64_t performanceFrame=0;
    unsigned gpuSamples=0;
    double gpuTotalSum=0,gpuBaseSum=0,gpuGlowSum=0,gpuBlurSum=0;
    void CollectGpuTimings();
    GpuQuerySlot* BeginGpuSample();
    void DrawOrderedStack(RenderSettings& settings,const VideoEffects& effects,D3D11_VIEWPORT viewport,ID3D11ShaderResourceView* source,
        const std::function<void(int)>& drawParticles,GpuQuerySlot* sample);
    void EnsureVideoBlur(int w,int h);
    void EnsureGlow(int w,int h);
    void Pass(ID3D11RenderTargetView* output,ID3D11PixelShader* shader,ID3D11ShaderResourceView* input,ID3D11ShaderResourceView* glow,D3D11_VIEWPORT viewport,ID3D11ShaderResourceView* softened=nullptr,ID3D11ShaderResourceView* original=nullptr);
public:
    struct GpuTimings {
        unsigned samples=0;
        double totalMs=0,baseMs=0,glowMs=0,blurMs=0;
    };
    void SetPerformanceEnabled(bool enabled);
    GpuTimings TakeGpuTimings();
    double LastSubmitMs=0,LastPresentMs=0;
    bool TraceFrames=false;
    uint64_t LastHash=0;double LastMean=0;
    void CompletePresentation(bool composed);
    void InspectFrame();
    HRESULT LastPresent=S_OK;
    int LastPasses=0;
    void Open(HWND window,int w,int h,ID3D11Device* sharedDevice=nullptr);
    ID3D11Device* Device() const {return device.Get();}
    void Draw(const VideoFrame& frame,bool contain,const VideoEffects& effects={},bool compareOriginal=false,bool present=true,const AudioDrive& audio={},bool uploadFrame=true,int positionX=0,int positionY=0,int positionScale=100);
    std::vector<uint8_t> CaptureFrame();
    void SaveFrame(const std::wstring& path);
};
