#include "AudioCapture.h"
#include "AudioSessionIdentity.h"
#include "AudioProcessingConfig.h"
#include "AdaptiveAudioAnalysis.h"
#include "SharedAudioCapture.h"
#include <iostream>
#include <Windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <mmreg.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace {
using Clock=std::chrono::steady_clock;
constexpr GUID PcmSubtype={0x00000001,0,0x0010,{0x80,0,0,0xaa,0,0x38,0x9b,0x71}};
constexpr GUID FloatSubtype={0x00000003,0,0x0010,{0x80,0,0,0xaa,0,0x38,0x9b,0x71}};
struct CoMemory {
    void* value=nullptr;
    ~CoMemory() {CoTaskMemFree(value);}
};
struct CaptureSession {
    ComPtr<IAudioClient> client;
    ComPtr<IAudioCaptureClient> capture;
    WAVEFORMATEX format{};
    bool floating=false,started=false;
    ~CaptureSession() {if(started)client->Stop();}
};
HRESULT DefaultEndpoint(IMMDeviceEnumerator* enumerator,ComPtr<IMMDevice>& device,std::wstring& identity) {
    device.Reset();
    HRESULT result=enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&device);
    if(FAILED(result))return result;
    LPWSTR id=nullptr;result=device->GetId(&id);
    CoMemory allocated{id};
    if(SUCCEEDED(result)&&id)identity=id;
    return result;
}
HRESULT OpenSession(IMMDevice* device,CaptureSession& session) {
    HRESULT result=device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(session.client.GetAddressOf()));
    if(FAILED(result))return result;
    WAVEFORMATEX* format=nullptr;result=session.client->GetMixFormat(&format);
    CoMemory allocated{format};
    if(FAILED(result))return result;
    if(!format)return E_UNEXPECTED;
    session.format=*format;
    WORD tag=format->wFormatTag;
    if(tag==WAVE_FORMAT_EXTENSIBLE) {
        if(format->cbSize<sizeof(WAVEFORMATEXTENSIBLE)-sizeof(WAVEFORMATEX))return AUDCLNT_E_UNSUPPORTED_FORMAT;
        auto extended=reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(format);
        if(IsEqualGUID(extended->SubFormat,FloatSubtype))tag=WAVE_FORMAT_IEEE_FLOAT;
        else if(IsEqualGUID(extended->SubFormat,PcmSubtype))tag=WAVE_FORMAT_PCM;
        else return AUDCLNT_E_UNSUPPORTED_FORMAT;
    }
    session.floating=tag==WAVE_FORMAT_IEEE_FLOAT;
    const WORD bits=format->wBitsPerSample,channels=format->nChannels;
    if(channels<1||channels>32||format->nSamplesPerSec<8000||format->nSamplesPerSec>768000||
       (session.floating?bits!=32:(tag!=WAVE_FORMAT_PCM||(bits!=8&&bits!=16&&bits!=24&&bits!=32)))||
       format->nBlockAlign<channels*(bits/8))return AUDCLNT_E_UNSUPPORTED_FORMAT;
    result=session.client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK,1000000,0,format,nullptr);
    if(FAILED(result))return result;
    SetAudioSessionIdentity(session.client.Get());
    result=session.client->GetService(__uuidof(IAudioCaptureClient),reinterpret_cast<void**>(session.capture.GetAddressOf()));
    if(FAILED(result))return result;
    result=session.client->Start();session.started=SUCCEEDED(result);return result;
}
float ReadSample(const BYTE* data,WORD bits,bool floating) {
    if(floating) {float value;std::memcpy(&value,data,sizeof(value));return value;}
    if(bits==8)return (static_cast<int>(*data)-128)/128.0f;
    if(bits==16) {int16_t value;std::memcpy(&value,data,sizeof(value));return value/32768.0f;}
    if(bits==24) {
        int32_t value=static_cast<int32_t>(data[0])|(static_cast<int32_t>(data[1])<<8)|(static_cast<int32_t>(data[2])<<16);
        if(value&0x800000)value-=0x1000000;
        return value/8388608.0f;
    }
    int32_t value;std::memcpy(&value,data,sizeof(value));return static_cast<float>(value/2147483648.0);
}
}

struct AudioCapture::Impl {
    mutable std::mutex mutex;
    std::condition_variable changed;
    bool enabled=false,stop=false;
    AudioAnalysisFrame latestFrame;
    AudioProcessingConfig config;
    bool spectrum;
    std::wstring requestedDevice,actualDevice;
    std::thread worker;
    explicit Impl(bool value):spectrum(value),worker([this] {Run();}) {}
    ~Impl() {
        {std::lock_guard lock(mutex);stop=true;enabled=false;}
        changed.notify_all();
        if(worker.joinable())worker.join();
    }
    bool Active() const {std::lock_guard lock(mutex);return enabled&&!stop;}
    bool Wait(std::chrono::milliseconds delay) {
        std::unique_lock lock(mutex);
        changed.wait_for(lock,delay,[&] {return !enabled||stop;});return enabled&&!stop;
    }
    void Publish(AudioAnalysisFrame value) {
        std::lock_guard lock(mutex);if(enabled&&!stop)latestFrame=value;
    }
    HRESULT Capture() {
        ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT result=CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator));
        if(FAILED(result))return result;
        ComPtr<IMMDevice> device;std::wstring identity;
        std::wstring requested;{std::lock_guard lock(mutex);requested=requestedDevice;actualDevice.clear();}
        if(requested.empty())result=DefaultEndpoint(enumerator.Get(),device,identity);
        else {result=enumerator->GetDevice(requested.c_str(),&device);identity=requested;}
        if(FAILED(result))return result;
        if(!Active())return S_OK;
        SharedAudioCapture shared(identity,config.Path(),spectrum);
        {std::lock_guard lock(mutex);actualDevice=identity;}
        auto followerCheck=Clock::now(),lastSharedRead=followerCheck;
        while(Active()&&!shared.TryOwn()) {
            {std::lock_guard lock(mutex);if(requestedDevice!=requested)return S_FALSE;}
            shared.Renew();AudioAnalysisFrame snapshot;
            if(shared.Read(snapshot)) {Publish(snapshot);lastSharedRead=Clock::now();}
            else if(Clock::now()-lastSharedRead>std::chrono::milliseconds(1500))Publish({});
            if(Clock::now()-followerCheck>=std::chrono::seconds(1)) {
                if(requested.empty()) {ComPtr<IMMDevice> current;std::wstring id;result=DefaultEndpoint(enumerator.Get(),current,id);if(FAILED(result))return result;if(id!=identity)return S_FALSE;}
                followerCheck=Clock::now();
            }
            if(!Wait(std::chrono::milliseconds(10)))return S_OK;
        }
        if(!Active())return S_OK;
        CaptureSession session;result=OpenSession(device.Get(),session);if(FAILED(result))return result;
        {std::lock_guard lock(mutex);actualDevice=identity;}
        auto analysis=std::make_unique<AdaptiveAudioAnalysis>();
        analysis->Reset(static_cast<int>(session.format.nSamplesPerSec),session.format.nChannels);
        bool adaptive=config.Enabled();analysis->Configure(adaptive,shared.WantsSpectrum());
        std::cerr<<"Audio analysis producer started; adaptive="<<adaptive<<"; shared_endpoint=1"<<std::endl;
        std::vector<float> converted;
        auto deviceCheck=Clock::now(),lastPacket=deviceCheck,lastSilence=deviceCheck;
        Publish(analysis->Snapshot());
        while(Active()) {
            {std::lock_guard lock(mutex);if(requestedDevice!=requested)return S_FALSE;}
            auto now=Clock::now();
            if(now-deviceCheck>=std::chrono::seconds(1)) {
                ComPtr<IMMDevice> current;std::wstring currentIdentity;
                if(requested.empty())result=DefaultEndpoint(enumerator.Get(),current,currentIdentity);
                else {result=enumerator->GetDevice(requested.c_str(),&current);currentIdentity=requested;}
                if(FAILED(result))return result;
                if(currentIdentity!=identity)return S_FALSE;
                bool next=config.Enabled();if(next!=adaptive)std::cerr<<"Audio analysis adaptive="<<next<<std::endl;adaptive=next;
                analysis->Configure(adaptive,shared.WantsSpectrum());deviceCheck=now;
            }
            UINT32 packetFrames=0;result=session.capture->GetNextPacketSize(&packetFrames);
            if(FAILED(result))return result;
            bool received=false;
            while(packetFrames&&Active()) {
                BYTE* data=nullptr;UINT32 frameCount=0;DWORD flags=0;
                result=session.capture->GetBuffer(&data,&frameCount,&flags,nullptr,nullptr);
                if(FAILED(result))return result;
                // Every successfully acquired packet is released even if allocation fails.
                struct Packet {
                    IAudioCaptureClient* capture;UINT32 frames;
                    ~Packet() {if(capture)capture->ReleaseBuffer(frames);}
                } packet{session.capture.Get(),frameCount};
                if(flags&AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) {analysis->Reset(static_cast<int>(session.format.nSamplesPerSec),session.format.nChannels);analysis->Configure(adaptive,shared.WantsSpectrum());}
                if((flags&AUDCLNT_BUFFERFLAGS_SILENT)||!data) {analysis->ProcessInterleaved(nullptr,frameCount);}
                else {
                    const size_t channels=session.format.nChannels;
                    converted.resize(static_cast<size_t>(frameCount)*channels);
                    const size_t bytes=session.format.wBitsPerSample/8;
                    for(size_t frame=0;frame<frameCount;++frame)
                        for(size_t channel=0;channel<channels;++channel)
                            converted[frame*channels+channel]=ReadSample(data+frame*session.format.nBlockAlign+channel*bytes,session.format.wBitsPerSample,session.floating);
                    analysis->ProcessInterleaved(converted.data(),frameCount);
                }
                result=session.capture->ReleaseBuffer(frameCount);packet.capture=nullptr;
                if(FAILED(result))return result;
                received=true;result=session.capture->GetNextPacketSize(&packetFrames);if(FAILED(result))return result;
            }
            now=Clock::now();
            if(received)lastPacket=lastSilence=now;
            else if(now-lastPacket>std::chrono::milliseconds(40)) {
                // A stopped playback engine may supply no silent packets at all.
                auto elapsed=std::min(std::chrono::duration<double>(now-lastSilence).count(),.1);
                auto silentFrames=static_cast<size_t>(elapsed*session.format.nSamplesPerSec);
                analysis->ProcessInterleaved(nullptr,silentFrames);lastSilence=now;
            }
            shared.Renew();auto snapshot=analysis->Snapshot();shared.Publish(snapshot);Publish(snapshot);if(!Wait(std::chrono::milliseconds(10)))break;
        }
        return S_OK;
    }
    void Run() noexcept {
        HRESULT apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        for(;;) {
            {std::unique_lock lock(mutex);changed.wait(lock,[&] {return enabled||stop;});if(stop)break;}
            HRESULT result=apartment;
            if(SUCCEEDED(apartment)) {
                try {result=Capture();}
                catch(const std::bad_alloc&) {result=E_OUTOFMEMORY;}
                catch(...) {result=E_FAIL;}
            }
            Publish({{0,0,0,0,false,FAILED(result)?static_cast<int32_t>(result):0},{}});
            if(result!=S_FALSE)Wait(std::chrono::seconds(1));
        }
        if(SUCCEEDED(apartment))CoUninitialize();
    }
};

AudioCapture::AudioCapture(bool spectrum):impl_(std::make_unique<Impl>(spectrum)) {}
AudioCapture::~AudioCapture()=default;
void AudioCapture::SetEnabled(bool enabled) {
    {std::lock_guard lock(impl_->mutex);if(impl_->enabled==enabled)return;impl_->enabled=enabled;impl_->latestFrame={};if(!enabled)impl_->actualDevice.clear();}
    impl_->changed.notify_all();
}
AudioLevels AudioCapture::Snapshot() const {std::lock_guard lock(impl_->mutex);return impl_->latestFrame.levels;}
AudioAnalysisFrame AudioCapture::SnapshotFrame() const {std::lock_guard lock(impl_->mutex);return impl_->latestFrame;}
void AudioCapture::SetDevice(const std::wstring& id) {std::lock_guard lock(impl_->mutex);if(impl_->requestedDevice==id)return;impl_->requestedDevice=id;impl_->actualDevice.clear();impl_->latestFrame={};impl_->changed.notify_all();}
std::wstring AudioCapture::Device() const {std::lock_guard lock(impl_->mutex);return impl_->actualDevice;}
