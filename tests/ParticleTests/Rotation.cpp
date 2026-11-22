#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <d3d11shader.h>
#include <wrl/client.h>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include "../../src/Windows/WallpaperHost/Particles.h"
#include "../../src/Windows/WallpaperHost/PostProcess.h"
#include "../../src/Windows/WallpaperHost/RenderSettings.h"

using Microsoft::WRL::ComPtr;
constexpr unsigned Count=1600;
constexpr float Pi=3.14159265358979323846f,Speed=Pi/4;
struct Sample {float x,y,angle,padding;};
using Settings=RenderSettings;
void HR(HRESULT result){if(FAILED(result))throw std::runtime_error("D3D rotation probe failed: "+std::to_string(result));}
int checks=0;
void Check(bool condition,const char* name){if(!condition)throw std::runtime_error(name);++checks;std::cout<<"PASS: "<<name<<'\n';}
float Delta(float a,float b){return std::remainder(b-a,2*Pi);}

// Run the production particle vertex shader in an off-screen compute probe.
// Reading its transformed corners measures real positions and orientations,
// without duplicating the random generator or rotation formula in the test.
class Probe {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11ComputeShader> shader;
    ComPtr<ID3D11Buffer> constants,output,staging;
    ComPtr<ID3D11UnorderedAccessView> view;
public:
    Probe(){
        D3D_FEATURE_LEVEL feature;
        HR(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,&feature,&context));
        std::string source=std::string(PostProcessShader)+R"(
RWStructuredBuffer<float4> samples:register(u0);
[numthreads(64,1,1)]
void rotationProbe(uint3 id:SV_DispatchThreadID) {
    if(id.x>=(uint)particleCount)return;
    ParticleVertex a=particleVS(0,id.x),b=particleVS(1,id.x),c=particleVS(5,id.x);
    float2 edge=(b.pos.xy-a.pos.xy)*float2(pixelSize.y/pixelSize.x,-1);
    float2 mid=(a.pos.xy+c.pos.xy)*.5;
    samples[id.x]=float4(mid.x*.5+.5,.5-mid.y*.5,atan2(edge.y,edge.x),0);
})";
        ComPtr<ID3DBlob> code,errors;
        HRESULT compiled=D3DCompile(source.data(),source.size(),"PostProcess.h",nullptr,nullptr,"rotationProbe","cs_5_0",D3DCOMPILE_ENABLE_STRICTNESS,0,&code,&errors);
        if(FAILED(compiled)&&errors)throw std::runtime_error(std::string((char*)errors->GetBufferPointer(),errors->GetBufferSize()));
        HR(compiled);
        ComPtr<ID3D11ShaderReflection> reflection;HR(D3DReflect(code->GetBufferPointer(),code->GetBufferSize(),IID_PPV_ARGS(&reflection)));
        D3D11_SHADER_VARIABLE_DESC description{};
        HR(reflection->GetConstantBufferByName("Settings")->GetVariableByName("particleMix")->GetDesc(&description));
        Check(description.StartOffset==offsetof(Settings,particles),"Probe constant layout matches production shader");
        HR(device->CreateComputeShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader));
        D3D11_BUFFER_DESC desc{};desc.ByteWidth=sizeof(Settings);desc.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        HR(device->CreateBuffer(&desc,nullptr,&constants));
        desc={};desc.ByteWidth=Count*sizeof(Sample);desc.BindFlags=D3D11_BIND_UNORDERED_ACCESS;desc.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;desc.StructureByteStride=sizeof(Sample);
        HR(device->CreateBuffer(&desc,nullptr,&output));
        HR(device->CreateUnorderedAccessView(output.Get(),nullptr,&view));
        desc.BindFlags=0;desc.MiscFlags=0;desc.StructureByteStride=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        HR(device->CreateBuffer(&desc,nullptr,&staging));
    }
    std::vector<Sample> Run(ParticleConstants particles){
        Settings settings;settings.px=1.f/1280;settings.py=1.f/720;settings.particles=particles;
        context->UpdateSubresource(constants.Get(),0,nullptr,&settings,0,0);
        context->CSSetShader(shader.Get(),nullptr,0);
        ID3D11Buffer* buffer=constants.Get();context->CSSetConstantBuffers(0,1,&buffer);
        ID3D11UnorderedAccessView* uav=view.Get();context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);
        context->Dispatch((Count+63)/64,1,1);
        uav=nullptr;context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);
        context->CopyResource(staging.Get(),output.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};HR(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
        std::vector<Sample> result(Count);std::memcpy(result.data(),mapped.pData,Count*sizeof(Sample));context->Unmap(staging.Get(),0);return result;
    }
};

void SpatialChecks(const std::vector<Sample>& values){
    std::array<std::array<int,16>,8> bands{};std::array<double,8> sinSum{},cosSum{};std::array<int,8> totals{};
    double sumY=0,sumS=0,sumC=0,yy=0,ss=0,cc=0,ys=0,yc=0;bool finite=true;
    for(auto value:values){
        finite&=std::isfinite(value.angle)&&value.x>.09f&&value.x<.91f&&value.y>.09f&&value.y<.91f;
        int band=std::clamp((int)((value.y-.1f)/.8f*8),0,7);
        int bin=std::clamp((int)((value.angle+Pi)/(2*Pi)*16),0,15);
        double s=std::sin(value.angle),c=std::cos(value.angle),y=value.y;
        ++bands[band][bin];++totals[band];sinSum[band]+=s;cosSum[band]+=c;
        sumY+=y;sumS+=s;sumC+=c;yy+=y*y;ss+=s*s;cc+=c*c;ys+=y*s;yc+=y*c;
    }
    Check(finite,"Finite orientation and expected spawn region");
    auto corr=[&](double b,double bb,double yb){return (Count*yb-sumY*b)/std::sqrt((Count*yy-sumY*sumY)*(Count*bb-b*b));};
    double sine=corr(sumS,ss,ys),cosine=corr(sumC,cc,yc);
    std::cout<<"Position/angle correlation: sine="<<sine<<", cosine="<<cosine<<'\n';
    Check(std::abs(sine)<.1&&std::abs(cosine)<.1,"Initial rotation is independent of vertical position");
    for(int band=0;band<8;++band){
        Check(std::count_if(bands[band].begin(),bands[band].end(),[](int n){return n>0;})>=14,"Each horizontal band contains broadly distributed angles");
        Check(std::hypot(sinSum[band],cosSum[band])/totals[band]<.2,"No common orientation within a horizontal band");
    }
}

int main(){try{
    Probe probe;ParticleConstants p;
    p.mix=1;p.style=14;p.count=Count;p.size=16;p.width=.8f;p.height=.8f;
    p.speed=0;p.sway=0;p.twinkle=0;p.fade=0;p.sizeRandom=0;p.speedRandom=0;p.emission=1;p.rotation=Speed;
    p.randomRotation=0;auto uniform0=probe.Run(p);
    SpatialChecks(uniform0);
    p.time=.1f;auto uniform1=probe.Run(p);
    bool uniform=true;for(unsigned i=0;i<Count;++i)uniform&=std::abs(Delta(uniform0[i].angle,uniform1[i].angle)-Speed*.1f)<.0001f;
    Check(uniform,"Switch off keeps configured signed angular speed");
    p.randomRotation=1;p.time=0;auto random0=probe.Run(p);
    auto repeated=probe.Run(p);Check(std::memcmp(random0.data(),repeated.data(),Count*sizeof(Sample))==0,"Repeated frame is deterministic");
    p.time=.1f;auto random1=probe.Run(p);p.time=.2f;auto random2=probe.Run(p);
    int positive=0;float slowest=Speed,fastest=0;bool bounded=true,stable=true,initial=true;std::array<bool,16> speedBins{};
    for(unsigned i=0;i<Count;++i){
        float delta=Delta(random0[i].angle,random1[i].angle),speed=delta/.1f;
        positive+=speed>0;slowest=std::min(slowest,std::abs(speed));fastest=std::max(fastest,std::abs(speed));
        bounded&=std::abs(speed)>=Speed*.35f-.001f&&std::abs(speed)<=Speed+.001f;
        stable&=std::abs(delta-Delta(random1[i].angle,random2[i].angle))<.0001f;
        initial&=std::abs(Delta(uniform0[i].angle,random0[i].angle))<.0001f;
        speedBins[std::clamp((int)((std::abs(speed)/Speed-.35f)/.65f*16),0,15)]=true;
    }
    std::cout<<"Clockwise="<<positive<<", counterclockwise="<<(Count-positive)<<", speed range="<<slowest/Speed<<".."<<fastest/Speed<<'\n';
    Check(positive>Count*.4&&positive<Count*.6,"Random mode mixes clockwise and counterclockwise directions");
    Check(bounded&&std::count(speedBins.begin(),speedBins.end(),true)==16,"Random angular speeds vary throughout 35-100% of configured speed");
    Check(stable,"Living particles keep their angular speed between frames");
    Check(initial,"Both switch modes preserve the same dispersed birth angles");
    p.rotation=0;p.time=0;auto still0=probe.Run(p);p.time=.2f;auto still1=probe.Run(p);
    bool still=true;for(unsigned i=0;i<Count;++i)still&=std::abs(Delta(still0[i].angle,still1[i].angle))<.0001f;
    Check(still,"Zero angular speed stays still with random rotation enabled");
    p.rotation=Speed;p.time=0;p.seedOffset=13007;auto secondary=probe.Run(p);SpatialChecks(secondary);
    int distinct=0;for(unsigned i=0;i<Count;++i)distinct+=std::abs(Delta(random0[i].angle,secondary[i].angle))>.1f;
    Check(distinct>Count*.9,"Secondary layer has its own independent rotation samples");
    p.seedOffset=0;p.padding=1;auto nextBirth=probe.Run(p);distinct=0;
    for(unsigned i=0;i<Count;++i)distinct+=std::abs(Delta(random0[i].angle,nextBirth[i].angle))>.1f;
    Check(distinct>Count*.9,"A new burst samples fresh orientations");
    p.emission=0;
    for(unsigned epoch:{0u,65535u}){
        p.padding=(float)epoch;p.time=7.99f;auto before=probe.Run(p);
        p.padding=(float)((epoch+1)&65535u);p.time=.01f;auto after=probe.Run(p);
        bool continuous=true;int alive=0;
        for(unsigned i=0;i<Count;++i){
            float offset=(float)i/Count*p.lifetime;
            float ageBefore=std::fmod(7.99f+offset,p.lifetime),ageAfter=std::fmod(.01f+offset,p.lifetime);
            if(ageAfter>ageBefore){++alive;continuous&=std::abs(Delta(before[i].angle,after[i].angle))<=Speed*.0201f+.0001f;}
        }
        Check(alive>Count*.99&&continuous,"Living rotations stay continuous across clock/epoch wrap");
    }
    std::cout<<"GPU rotation checks passed: "<<checks<<'\n';return 0;
}catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}}
