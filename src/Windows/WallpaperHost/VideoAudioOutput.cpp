#include "VideoAudioOutput.h"
#include "AudioSessionIdentity.h"
#include <Windows.h>
#include <audioclient.h>
#include <audiopolicy.h>
#include <avrt.h>
#include <mmdeviceapi.h>
#include <mmreg.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstring>
#include <iostream>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswresample/swresample.h>
#include <libavutil/opt.h>
}

using Microsoft::WRL::ComPtr;
namespace {
using Clock=std::chrono::steady_clock;
constexpr int Rate=48000,Channels=2;
constexpr int FadeFrames=Rate/200; // 5 ms after starting/recovering an output.
int64_t SamplePosition(double seconds) {
    if(!std::isfinite(seconds)||std::abs(seconds)>1e9)throw std::runtime_error("Invalid audio timestamp");
    return static_cast<int64_t>(std::llround(seconds*Rate));
}
int64_t Millis() {return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now().time_since_epoch()).count();}
struct AudioError {int code;};
void Check(int result) {if(result<0)throw AudioError{result};}

class AudioFile {
    AVFormatContext* format=nullptr;
    AVCodecContext* codec=nullptr;
    AVPacket* packet=nullptr;
    AVFrame* frame=nullptr;
    SwrContext* resampler=nullptr;
    AVChannelLayout inputLayout{};
    AVSampleFormat inputFormat=AV_SAMPLE_FMT_NONE;
    int stream=-1,inputRate=0;
    bool packetPending=false,draining=false,ended=false,timelineReady=false;
    std::atomic_bool& cancel;
    std::function<bool()> permitted;
    int64_t deadline=0;
    double streamStart=0,nextTime=0;
    int64_t pcmStart=0,nextPcm=0;
    std::vector<float> pcm;
    static int Interrupt(void* opaque) {
        auto* self=static_cast<AudioFile*>(opaque);
        return self->cancel.load()||!self->permitted()||(self->deadline&&Millis()>=self->deadline);
    }
    bool Decode() {
        deadline=Millis()+1500;
        for(int operations=0;operations<8192&&!Interrupt(this);++operations) {
            int result=avcodec_receive_frame(codec,frame);
            if(result==0) {
                const int rate=frame->sample_rate;
                if(rate<8000||rate>768000||frame->ch_layout.nb_channels<1||frame->ch_layout.nb_channels>32||frame->nb_samples<0||frame->nb_samples>262144)
                    throw AudioError{AVERROR_INVALIDDATA};
                const auto sampleFormat=static_cast<AVSampleFormat>(frame->format);
                const auto timeBase=format->streams[stream]->time_base;
                const auto timestamp=frame->best_effort_timestamp!=AV_NOPTS_VALUE?frame->best_effort_timestamp:frame->pts;
                const double frameTime=timestamp==AV_NOPTS_VALUE?nextTime:timestamp*av_q2d(timeBase);
                if(!std::isfinite(frameTime))throw AudioError{AVERROR_INVALIDDATA};
                // Container PTS (notably millisecond MKV timestamps) is coarser
                // than audio samples. Quantisation must not insert silence or
                // repeat samples at every decoded packet boundary.
                const double tolerance=std::max(.002,2*av_q2d(timeBase));
                const bool discontinuity=timelineReady&&std::abs(frameTime-nextTime)>tolerance;
                if(!resampler||discontinuity||rate!=inputRate||sampleFormat!=inputFormat||av_channel_layout_compare(&inputLayout,&frame->ch_layout)!=0) {
                    swr_free(&resampler);av_channel_layout_uninit(&inputLayout);
                    Check(av_channel_layout_copy(&inputLayout,&frame->ch_layout));
                    AVChannelLayout stereo=AV_CHANNEL_LAYOUT_STEREO;
                    Check(swr_alloc_set_opts2(&resampler,&stereo,AV_SAMPLE_FMT_FLT,Rate,&inputLayout,sampleFormat,rate,0,nullptr));
                    Check(av_opt_set_double(resampler,"rematrix_maxval",1.0,0));
                    Check(swr_init(resampler));inputRate=rate;inputFormat=sampleFormat;
                }
                pcmStart=timelineReady&&!discontinuity?nextPcm:
                    SamplePosition(frameTime-swr_get_delay(resampler,rate)/static_cast<double>(rate));
                nextTime=(timelineReady&&!discontinuity?nextTime:frameTime)+frame->nb_samples/static_cast<double>(rate);
                timelineReady=true;
                int maximum=swr_get_out_samples(resampler,frame->nb_samples);
                if(maximum<0||maximum>2097152)throw AudioError{AVERROR_INVALIDDATA};
                pcm.resize(static_cast<size_t>(maximum)*Channels);
                uint8_t* output=reinterpret_cast<uint8_t*>(pcm.data());
                result=swr_convert(resampler,&output,maximum,const_cast<const uint8_t**>(frame->extended_data),frame->nb_samples);
                av_frame_unref(frame);Check(result);pcm.resize(static_cast<size_t>(result)*Channels);
                nextPcm=pcmStart+result;
                if(!pcm.empty())return true;
                continue;
            }
            if(result==AVERROR_EOF) {
                if(resampler) {
                    int maximum=swr_get_out_samples(resampler,0);
                    if(maximum<0||maximum>2097152)throw AudioError{AVERROR_INVALIDDATA};
                    pcm.resize(static_cast<size_t>(maximum)*Channels);
                    uint8_t* output=reinterpret_cast<uint8_t*>(pcm.data());
                    pcmStart=nextPcm;
                    result=swr_convert(resampler,&output,maximum,nullptr,0);Check(result);
                    nextPcm=pcmStart+result;
                    pcm.resize(static_cast<size_t>(result)*Channels);if(result>0)return true;
                }
                ended=true;pcm.clear();return false;
            }
            if(result!=AVERROR(EAGAIN))Check(result);
            if(draining)throw AudioError{AVERROR_INVALIDDATA};
            if(!packetPending) {
                result=av_read_frame(format,packet);
                if(result==AVERROR_EOF) {Check(avcodec_send_packet(codec,nullptr));draining=true;continue;}
                if(result==AVERROR(EAGAIN))continue;
                Check(result);
                if(packet->stream_index!=stream) {av_packet_unref(packet);continue;}
                if(packet->size>32*1024*1024)throw AudioError{AVERROR_INVALIDDATA};
                packetPending=true;
            }
            result=avcodec_send_packet(codec,packet);
            if(result==AVERROR(EAGAIN))continue;
            av_packet_unref(packet);packetPending=false;Check(result);
        }
        throw AudioError{AVERROR_EXIT};
    }
public:
    AudioFile(std::atomic_bool& cancelled,std::function<bool()> allowed):cancel(cancelled),permitted(std::move(allowed)) {}
    ~AudioFile() {
        swr_free(&resampler);av_channel_layout_uninit(&inputLayout);av_frame_free(&frame);av_packet_free(&packet);
        avcodec_free_context(&codec);avformat_close_input(&format);
    }
    bool Open(const std::string& path) {
        format=avformat_alloc_context();if(!format)throw std::bad_alloc();
        format->interrupt_callback={Interrupt,this};deadline=Millis()+10000;
        AVDictionary* options=nullptr;av_dict_set(&options,"protocol_whitelist","file,crypto,data",0);
        int result=avformat_open_input(&format,path.c_str(),nullptr,&options);av_dict_free(&options);Check(result);
        Check(avformat_find_stream_info(format,nullptr));
        stream=av_find_best_stream(format,AVMEDIA_TYPE_AUDIO,-1,-1,nullptr,0);
        if(stream==AVERROR_STREAM_NOT_FOUND)return false;
        Check(stream);
        auto* audio=format->streams[stream];auto* implementation=avcodec_find_decoder(audio->codecpar->codec_id);
        if(!implementation)throw AudioError{AVERROR_DECODER_NOT_FOUND};
        codec=avcodec_alloc_context3(implementation);if(!codec)throw std::bad_alloc();
        Check(avcodec_parameters_to_context(codec,audio->codecpar));codec->thread_count=1;codec->pkt_timebase=audio->time_base;
        Check(avcodec_open2(codec,implementation,nullptr));
        packet=av_packet_alloc();frame=av_frame_alloc();if(!packet||!frame)throw std::bad_alloc();
        streamStart=audio->start_time!=AV_NOPTS_VALUE?audio->start_time*av_q2d(audio->time_base):
            (format->start_time!=AV_NOPTS_VALUE?format->start_time/static_cast<double>(AV_TIME_BASE):0);
        nextTime=streamStart;return true;
    }
    void Seek(double target) {
        deadline=Millis()+3000;
        const double ticks=std::max(streamStart,target-.15)/av_q2d(format->streams[stream]->time_base);
        if(!std::isfinite(ticks)||std::abs(ticks)>9e18)throw AudioError{AVERROR_INVALIDDATA};
        Check(av_seek_frame(format,stream,static_cast<int64_t>(std::floor(ticks)),AVSEEK_FLAG_BACKWARD));
        avcodec_flush_buffers(codec);av_packet_unref(packet);av_frame_unref(frame);
        swr_free(&resampler);av_channel_layout_uninit(&inputLayout);
        pcm.clear();packetPending=draining=ended=timelineReady=false;pcmStart=nextPcm=0;nextTime=std::max(streamStart,target-.15);
    }
    void Render(std::vector<float>& output,int frames,int64_t start) {
        output.assign(static_cast<size_t>(frames)*Channels,0);
        int written=0,decoded=0;
        while(written<frames&&!cancel.load()) {
            const int64_t position=start+written;
            const int available=static_cast<int>(pcm.size()/Channels);
            if(available==0||position>=pcmStart+available) {
                if(ended)break;
                if(++decoded>2048)throw AudioError{AVERROR_INVALIDDATA};
                if(!Decode())break;
                continue;
            }
            if(position<pcmStart) {
                written+=static_cast<int>(std::min<int64_t>(frames-written,pcmStart-position));
                continue;
            }
            int offset=static_cast<int>(position-pcmStart);
            int count=std::min(frames-written,available-offset);
            std::copy_n(pcm.data()+static_cast<size_t>(offset)*Channels,static_cast<size_t>(count)*Channels,output.data()+static_cast<size_t>(written)*Channels);
            written+=count;
        }
        for(auto& sample:output)sample=std::isfinite(sample)?std::clamp(sample,-1.f,1.f):0;
    }
};

struct Device {
    ComPtr<IAudioClient> client;
    ComPtr<IAudioRenderClient> render;
    UINT32 frames=0;
    bool started=false;
    ~Device() {if(started)client->Stop();}
    void Reset() {if(started)Check(client->Stop());started=false;Check(client->Reset());}
};
struct AudioThreadScheduling {
    HANDLE task=nullptr;
    AudioThreadScheduling() {DWORD index=0;task=AvSetMmThreadCharacteristicsW(L"Audio",&index);}
    ~AudioThreadScheduling() {if(task)AvRevertMmThreadCharacteristics(task);}
};
std::wstring Endpoint(IMMDeviceEnumerator* enumerator,ComPtr<IMMDevice>& endpoint) {
    endpoint.Reset();Check(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&endpoint));
    LPWSTR id=nullptr;Check(endpoint->GetId(&id));
    std::wstring result;try {if(id)result=id;}catch(...) {CoTaskMemFree(id);throw;}
    CoTaskMemFree(id);return result;
}
void OpenDevice(IMMDevice* endpoint,Device& device) {
    Check(endpoint->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(device.client.GetAddressOf())));
    WAVEFORMATEX format{};format.wFormatTag=WAVE_FORMAT_IEEE_FLOAT;format.nChannels=Channels;
    format.nSamplesPerSec=Rate;format.wBitsPerSample=32;format.nBlockAlign=Channels*4;format.nAvgBytesPerSec=Rate*format.nBlockAlign;
    // Every wallpaper host joins the same persistent application session. A new
    // media process must inherit Sndvol's volume/mute, including while the old
    // process is still alive. Per-screen gain is applied to PCM separately.
    static constexpr GUID sessionId={0x0e36dba9,0x9f87,0x4a43,{0xb6,0x0d,0x62,0xbb,0xb9,0xc7,0x72,0xd1}};
    Check(device.client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY|AUDCLNT_STREAMFLAGS_CROSSPROCESS,2000000,0,&format,&sessionId));
    SetAudioSessionIdentity(device.client.Get());
    Check(device.client->GetBufferSize(&device.frames));
    Check(device.client->GetService(__uuidof(IAudioRenderClient),reinterpret_cast<void**>(device.render.GetAddressOf())));
}
}

struct VideoAudioOutput::Impl {
    struct Snapshot {double media=0;bool playing=false;uint64_t generation=0;Clock::time_point updated=Clock::now();};
    struct OutputClock {double media=0,buffered=0;uint64_t generation=0;Clock::time_point updated=Clock::now();bool valid=false;};
    std::string path,lastStatus;
    int lastError=0;
    std::atomic_bool& stopped;
    std::atomic_bool stop=false,cancelIo=true;
    Status status;
    std::function<bool()> permitted;
    std::function<float()> volume;
    std::mutex mutex;
    std::condition_variable changed;
    Snapshot state;
    OutputClock outputClock;
    std::thread worker;
    Impl(std::string source,std::atomic_bool& cancelled,std::function<bool()> allowed,Status callback,std::function<float()> gain):path(std::move(source)),stopped(cancelled),status(std::move(callback)),permitted(std::move(allowed)),volume(std::move(gain)),worker([this]{Run();}) {}
    ~Impl() {stop=true;cancelIo=true;changed.notify_all();if(worker.joinable())worker.join();}
    Snapshot Read() {std::lock_guard lock(mutex);return state;}
    void ClearClock() {std::lock_guard lock(mutex);outputClock.valid=false;}
    void PublishClock(int64_t next,UINT32 padding,uint64_t generation) {
        std::lock_guard lock(mutex);
        outputClock={(next-padding)/static_cast<double>(Rate),padding/static_cast<double>(Rate),generation,Clock::now(),true};
    }
    std::optional<double> Position(uint64_t generation) {
        if(stop.load()||stopped.load()||!permitted())return std::nullopt;
        std::lock_guard lock(mutex);
        if(!state.playing||!outputClock.valid||outputClock.generation!=generation||state.generation!=generation)return std::nullopt;
        // Once all submitted samples have elapsed, hold the clock until more
        // audio is actually queued. A decoder stall must not invent playback.
        return outputClock.media+std::clamp(std::chrono::duration<double>(Clock::now()-outputClock.updated).count(),0.0,outputClock.buffered);
    }
    bool Active() {return !stop.load()&&!stopped.load()&&permitted()&&Read().playing;}
    void Publish(const char* value,int error=0) {
        if(lastStatus==value&&lastError==error)return;
        lastStatus=value;lastError=error;if(status)status(value,error);
    }
    bool Play() {
        ClearClock();
        struct ClockLifetime {Impl* owner;~ClockLifetime() {owner->ClearClock();}} clockLifetime{this};
        AudioFile file(cancelIo,[this]{return !stop.load()&&!stopped.load()&&permitted();});
        if(!file.Open(path)) {Publish("no-track");return false;}
        if(!Active())return true;
        ComPtr<IMMDeviceEnumerator> enumerator;
        Check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)));
        ComPtr<IMMDevice> endpoint;auto identity=Endpoint(enumerator.Get(),endpoint);
        Device device;OpenDevice(endpoint.Get(),device);
        AudioThreadScheduling scheduling;
        int64_t next=0;uint64_t generation=0;bool aligned=false;
        int fade=0;UINT32 desired=std::min(device.frames,static_cast<UINT32>(Rate*.08));
        uint64_t underruns=0,reportedUnderruns=0;
        std::vector<float> samples;auto deviceCheck=Clock::now();
        float screenGain=std::clamp(volume(),0.f,1.f);
        auto lastDiagnostic=Clock::time_point{};
        auto recovered=[&]() {
            ++underruns;fade=0;
            desired=std::min({device.frames,static_cast<UINT32>(Rate*.16),desired+static_cast<UINT32>(Rate*.04)});
        };
        while(Active()) {
            auto snapshot=Read();auto now=Clock::now();
            double target=snapshot.media+std::max(0.0,std::chrono::duration<double>(now-snapshot.updated).count());
            UINT32 padding=0;Check(device.client->GetCurrentPadding(&padding));
            bool empty=device.started&&padding==0;
            if(empty)recovered();
            const bool changedSource=!aligned||snapshot.generation!=generation;
            // Audio is the playback clock. Only a new playback generation (a
            // loop/restart), startup or device reopen may seek; video lateness
            // must never rewind and replay samples the listener already heard.
            if(changedSource) {
                ClearClock();device.Reset();file.Seek(target);next=SamplePosition(target);padding=0;generation=snapshot.generation;aligned=true;fade=0;
            }
            if(device.started)PublishClock(next,padding,generation);
            // Start with 80 ms of decoded audio; grow to 160 ms if the endpoint
            // empties. Stop/reset still handles pause immediately, not by draining.
            if(padding<desired) {
                int frames=static_cast<int>(desired-padding);file.Render(samples,frames,next);
                if(!Active())break;
                if(Read().generation!=generation)continue;
                UINT32 afterDecode=0;Check(device.client->GetCurrentPadding(&afterDecode));
                if(device.started&&afterDecode==0&&!empty)recovered();
                const float targetGain=std::clamp(volume(),0.f,1.f);
                const float gainStep=(targetGain-screenGain)/std::max(1,std::min(frames,FadeFrames));
                for(int index=0;index<frames;++index) {
                    // Smooth level changes without restarting audio or its clock.
                    screenGain=index<FadeFrames?screenGain+gainStep:targetGain;
                    float gain=screenGain*(fade<FadeFrames?static_cast<float>(++fade)/FadeFrames:1.f);
                    for(int channel=0;channel<Channels;++channel)samples[static_cast<size_t>(index)*Channels+channel]*=gain;
                }
                screenGain=targetGain;
                BYTE* data=nullptr;Check(device.render->GetBuffer(static_cast<UINT32>(frames),&data));
                std::memcpy(data,samples.data(),samples.size()*sizeof(float));
                Check(device.render->ReleaseBuffer(static_cast<UINT32>(frames),0));next+=frames;
                if(!device.started) {Check(device.client->Start());device.started=true;Publish("playing");}
                UINT32 queued=0;Check(device.client->GetCurrentPadding(&queued));PublishClock(next,queued,generation);
            }
            if(underruns!=reportedUnderruns&&
               (lastDiagnostic==Clock::time_point{}||now-lastDiagnostic>=std::chrono::seconds(10))) {
                std::cerr<<"Audio recovery: emptyBuffers="<<underruns<<"; queueMs="<<desired*1000/Rate<<"; clock=audio"<<std::endl;
                lastDiagnostic=now;reportedUnderruns=underruns;
            }
            if(now-deviceCheck>=std::chrono::seconds(1)) {
                ComPtr<IMMDevice> current;if(Endpoint(enumerator.Get(),current)!=identity)return true;
                deviceCheck=now;
            }
            std::unique_lock lock(mutex);changed.wait_for(lock,std::chrono::milliseconds(8),[&]{return stop.load()||stopped.load()||!state.playing;});
        }
        return true;
    }
    void Run() noexcept {
        HRESULT apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        try {
            while(!stop.load()&&!stopped.load()) {
                {std::unique_lock lock(mutex);changed.wait_for(lock,std::chrono::milliseconds(100),[&]{return stop.load()||stopped.load()||(state.playing&&permitted());});}
                if(stop.load()||stopped.load())break;
                if(!Active())continue;
                bool failed=false;
                try {Check(apartment);if(!Play())break;}
                catch(const AudioError& error) {if(Active()) {Publish("unavailable",error.code);failed=true;}}
                catch(...) {if(Active()) {Publish("unavailable",E_FAIL);failed=true;}}
                if(!Active())Publish("paused");
                if(failed) {std::unique_lock lock(mutex);changed.wait_for(lock,std::chrono::seconds(1),[&]{return stop.load()||stopped.load()||!state.playing;});}
            }
        } catch(...) { }
        if(SUCCEEDED(apartment))CoUninitialize();
    }
};

VideoAudioOutput::VideoAudioOutput(std::string path,std::atomic_bool& stopped,std::function<bool()> permitted,Status status,std::function<float()> volume):impl_(std::make_unique<Impl>(std::move(path),stopped,std::move(permitted),std::move(status),std::move(volume))) {}
VideoAudioOutput::~VideoAudioOutput()=default;
std::optional<double> VideoAudioOutput::Position(uint64_t generation) {return impl_->Position(generation);}
void VideoAudioOutput::Update(double mediaSeconds,bool playing,uint64_t generation) {
    if(!std::isfinite(mediaSeconds)||std::abs(mediaSeconds)>1e9)playing=false;
    bool notify=false;
    {std::lock_guard lock(impl_->mutex);notify=impl_->state.playing!=playing;
        impl_->state={mediaSeconds,playing,generation,Clock::now()};impl_->cancelIo=!playing;}
    if(notify)impl_->changed.notify_all();
}
