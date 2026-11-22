#include "VideoAudioReader.h"
#include <algorithm>
#include <cerrno>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <new>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/samplefmt.h>
}

namespace {
int64_t Millis() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}
constexpr double AnalysisPreroll=.18;
constexpr int MaxOperations=256;
constexpr int ChunkFrames=2048;
template<class T> T Load(const uint8_t* source) {
    T value;std::memcpy(&value,source,sizeof(value));return value;
}
float ReadSample(const uint8_t* source,AVSampleFormat packed) {
    double value=0;
    switch(packed) {
        case AV_SAMPLE_FMT_U8:value=(*source-128)/128.0;break;
        case AV_SAMPLE_FMT_S16:value=Load<int16_t>(source)/32768.0;break;
        case AV_SAMPLE_FMT_S32:value=Load<int32_t>(source)/2147483648.0;break;
        case AV_SAMPLE_FMT_S64:value=static_cast<double>(Load<int64_t>(source))/9223372036854775808.0;break;
        case AV_SAMPLE_FMT_FLT:value=Load<float>(source);break;
        case AV_SAMPLE_FMT_DBL:value=Load<double>(source);break;
        default:break;
    }
    return std::isfinite(value)?static_cast<float>(std::clamp(value,-1.0,1.0)):0;
}
}

int VideoAudioReader::Interrupt(void* opaque) {
    auto* self=static_cast<VideoAudioReader*>(opaque);
    const auto deadline=self->deadline_.load(std::memory_order_relaxed);
    return (self->stopped_&&self->stopped_->load(std::memory_order_relaxed))||
        (deadline>0&&Millis()>=deadline);
}

void VideoAudioReader::Close() noexcept {
    av_frame_free(&frame_);av_packet_free(&packet_);
    avcodec_free_context(&codec_);avformat_close_input(&format_);
    stopped_=nullptr;deadline_=0;stream_=-1;sampleRate_=channels_=frameOffset_=0;
    packetPending_=frameReady_=draining_=eof_=sampled_=false;
    streamStart_=frameStart_=nextFrameTime_=cursor_=lastTarget_=0;
    samples_.clear();levels_={};meter_.reset();
}

void VideoAudioReader::Fail(int error) noexcept {
    Close();levels_.error=error<0?error:AVERROR(EIO);
}

void VideoAudioReader::Open(const std::string& path,std::atomic_bool& stopped) noexcept {
    Close();stopped_=&stopped;
    try {
        if(stopped.load()) {Fail(AVERROR_EXIT);return;}
        format_=avformat_alloc_context();
        if(!format_) {Fail(AVERROR(ENOMEM));return;}
        format_->interrupt_callback={Interrupt,this};
        format_->probesize=2*1024*1024;
        format_->max_analyze_duration=2*AV_TIME_BASE;
        deadline_=Millis()+3000;
        AVDictionary* options=nullptr;
        // Match MediaSession's local-media boundary, including nested resources.
        av_dict_set(&options,"protocol_whitelist","file,crypto,data",0);
        int result=avformat_open_input(&format_,path.c_str(),nullptr,&options);
        av_dict_free(&options);
        if(result<0) {Fail(result);return;}
        result=avformat_find_stream_info(format_,nullptr);
        if(result<0) {Fail(result);return;}
        stream_=av_find_best_stream(format_,AVMEDIA_TYPE_AUDIO,-1,-1,nullptr,0);
        if(stream_<0) {Fail(stream_);return;}
        auto* stream=format_->streams[stream_];
        auto* implementation=avcodec_find_decoder(stream->codecpar->codec_id);
        if(!implementation) {Fail(AVERROR_DECODER_NOT_FOUND);return;}
        codec_=avcodec_alloc_context3(implementation);
        if(!codec_) {Fail(AVERROR(ENOMEM));return;}
        result=avcodec_parameters_to_context(codec_,stream->codecpar);
        if(result<0) {Fail(result);return;}
        codec_->pkt_timebase=stream->time_base;
        codec_->thread_count=1;
        result=avcodec_open2(codec_,implementation,nullptr);
        if(result<0) {Fail(result);return;}
        sampleRate_=codec_->sample_rate;channels_=codec_->ch_layout.nb_channels;
        if(sampleRate_<8000||sampleRate_>768000||channels_<1||channels_>32) {
            Fail(AVERROR(EINVAL));return;
        }
        frame_=av_frame_alloc();packet_=av_packet_alloc();
        if(!frame_||!packet_) {Fail(AVERROR(ENOMEM));return;}
        // Do not subtract start_time: video PTS uses this same absolute timeline.
        streamStart_=stream->start_time!=AV_NOPTS_VALUE?stream->start_time*av_q2d(stream->time_base):
            (format_->start_time!=AV_NOPTS_VALUE?format_->start_time/static_cast<double>(AV_TIME_BASE):0);
        nextFrameTime_=streamStart_;cursor_=streamStart_;
        meter_=std::make_unique<AdaptiveAudioAnalysis>();
        meter_->Reset(sampleRate_,channels_);levels_=meter_->Levels();
        deadline_=0;
    } catch(const std::bad_alloc&) {Fail(AVERROR(ENOMEM));}
      catch(...) {Fail(AVERROR(EIO));}
}

bool VideoAudioReader::Seek(double target) {
    auto* stream=format_->streams[stream_];
    const double preroll=std::max(streamStart_,target-AnalysisPreroll);
    const double ticks=preroll/av_q2d(stream->time_base);
    if(!std::isfinite(ticks)||ticks<static_cast<double>(INT64_MIN)+1024||
        ticks>static_cast<double>(INT64_MAX)-1024) {Fail(AVERROR(EINVAL));return false;}
    const int result=av_seek_frame(format_,stream_,static_cast<int64_t>(std::floor(ticks)),AVSEEK_FLAG_BACKWARD);
    if(result<0) {Fail(result);return false;}
    avcodec_flush_buffers(codec_);av_frame_unref(frame_);av_packet_unref(packet_);
    frameReady_=packetPending_=draining_=eof_=false;frameOffset_=0;
    meter_->Reset(sampleRate_,channels_);
    cursor_=target-AnalysisPreroll;nextFrameTime_=preroll;
    return true;
}

bool VideoAudioReader::AcceptFrame() {
    const int rate=frame_->sample_rate>0?frame_->sample_rate:codec_->sample_rate;
    const int channels=frame_->ch_layout.nb_channels>0?frame_->ch_layout.nb_channels:codec_->ch_layout.nb_channels;
    const auto format=static_cast<AVSampleFormat>(frame_->format);
    const auto packed=av_get_packed_sample_fmt(format);
    if(rate<8000||rate>768000||channels<1||channels>32||frame_->nb_samples<0||
        frame_->nb_samples>262144||!frame_->extended_data||
        (packed!=AV_SAMPLE_FMT_U8&&packed!=AV_SAMPLE_FMT_S16&&packed!=AV_SAMPLE_FMT_S32&&
         packed!=AV_SAMPLE_FMT_S64&&packed!=AV_SAMPLE_FMT_FLT&&packed!=AV_SAMPLE_FMT_DBL)) {
        Fail(AVERROR(EINVAL));return false;
    }
    const int planes=av_sample_fmt_is_planar(format)?channels:1;
    for(int i=0;i<planes;++i)if(!frame_->extended_data[i]) {Fail(AVERROR_INVALIDDATA);return false;}
    if(rate!=sampleRate_||channels!=channels_) {
        sampleRate_=rate;channels_=channels;meter_->Reset(rate,channels);
    }
    const auto timestamp=frame_->best_effort_timestamp!=AV_NOPTS_VALUE?frame_->best_effort_timestamp:frame_->pts;
    frameStart_=timestamp!=AV_NOPTS_VALUE?timestamp*av_q2d(format_->streams[stream_]->time_base):nextFrameTime_;
    if(!std::isfinite(frameStart_)) {Fail(AVERROR_INVALIDDATA);return false;}
    nextFrameTime_=frameStart_+frame_->nb_samples/static_cast<double>(sampleRate_);
    frameOffset_=0;frameReady_=true;
    return true;
}

VideoAudioReader::DecodeResult VideoAudioReader::Decode(int& operations,int64_t workDeadline) {
    while(operations<MaxOperations&&Millis()<workDeadline&&!Interrupt(this)) {
        ++operations;
        int result=avcodec_receive_frame(codec_,frame_);
        if(result==0)return AcceptFrame()?DecodeResult::Frame:DecodeResult::Failed;
        if(result==AVERROR_EOF) {eof_=true;return DecodeResult::End;}
        if(result!=AVERROR(EAGAIN)) {Fail(result);return DecodeResult::Failed;}
        if(draining_) {Fail(AVERROR_INVALIDDATA);return DecodeResult::Failed;}
        if(!packetPending_) {
            result=av_read_frame(format_,packet_);
            if(result==AVERROR_EOF) {
                result=avcodec_send_packet(codec_,nullptr);
                if(result<0&&result!=AVERROR_EOF) {Fail(result);return DecodeResult::Failed;}
                draining_=true;continue;
            }
            if(result==AVERROR(EAGAIN))return DecodeResult::Yield;
            if(result<0) {Fail(result);return DecodeResult::Failed;}
            if(packet_->stream_index!=stream_) {av_packet_unref(packet_);continue;}
            if(packet_->size>32*1024*1024) {Fail(AVERROR_INVALIDDATA);return DecodeResult::Failed;}
            packetPending_=true;
        }
        result=avcodec_send_packet(codec_,packet_);
        if(result==AVERROR(EAGAIN))continue;
        av_packet_unref(packet_);packetPending_=false;
        if(result<0) {Fail(result);return DecodeResult::Failed;}
    }
    return DecodeResult::Yield;
}

void VideoAudioReader::Convert(int count) {
    const auto format=static_cast<AVSampleFormat>(frame_->format);
    const auto packed=av_get_packed_sample_fmt(format);
    const bool planar=av_sample_fmt_is_planar(format)!=0;
    const int bytes=av_get_bytes_per_sample(format);
    samples_.resize(static_cast<size_t>(count)*channels_);
    for(int f=0;f<count;++f)for(int c=0;c<channels_;++c) {
        const size_t source=planar?static_cast<size_t>(frameOffset_+f):static_cast<size_t>(frameOffset_+f)*channels_+c;
        samples_[static_cast<size_t>(f)*channels_+c]=ReadSample(frame_->extended_data[planar?c:0]+source*bytes,packed);
    }
    meter_->ProcessInterleaved(samples_.data(),count);
}

void VideoAudioReader::SilenceUntil(double target) {
    if(target<=cursor_)return;
    // Avoid scanning long silent tails/gaps. A reset is equivalent after enough
    // silence, and keeps early audio ending before the video from holding a beat.
    if(target-cursor_>.5) {meter_->Reset(sampleRate_,channels_);cursor_=target;return;}
    const auto frames=static_cast<size_t>(std::floor((target-cursor_)*sampleRate_+1e-6));
    meter_->ProcessInterleaved(nullptr,frames);
    cursor_+=frames/static_cast<double>(sampleRate_);
}

AudioLevels VideoAudioReader::Sample(double mediaSeconds) noexcept {
    if(!format_||!codec_)return levels_;
    meter_->Configure(analysisConfig_.Enabled());
    try {
        if(stopped_->load(std::memory_order_relaxed)) {Fail(AVERROR_EXIT);return levels_;}
        if(!std::isfinite(mediaSeconds)||std::abs(mediaSeconds)>1e9) {Fail(AVERROR(EINVAL));return levels_;}
        const auto now=Millis();
        // Read/seek interrupt bound and a smaller cooperative per-frame budget.
        deadline_=now+25;
        const auto workDeadline=now+5;
        const bool rewind=sampled_&&mediaSeconds<lastTarget_-1e-6;
        const bool jumped=sampled_&&mediaSeconds-lastTarget_>.75;
        const bool behind=sampled_&&mediaSeconds-cursor_>.75;
        if(rewind||jumped||behind||(!sampled_&&mediaSeconds>streamStart_+.75)) {
            if(!Seek(mediaSeconds))return levels_;
        } else if(!sampled_)cursor_=std::min(streamStart_,mediaSeconds-AnalysisPreroll);
        sampled_=true;lastTarget_=mediaSeconds;
        int operations=0;
        while(cursor_<mediaSeconds&&operations<MaxOperations&&Millis()<workDeadline) {
            if(!frameReady_) {
                if(eof_) {SilenceUntil(mediaSeconds);break;}
                const auto result=Decode(operations,workDeadline);
                if(result==DecodeResult::Failed)return levels_;
                if(result==DecodeResult::Yield)break;
                if(result==DecodeResult::End) {SilenceUntil(mediaSeconds);break;}
            }
            ++operations;
            // Skip obsolete samples after a seek, but never process samples from
            // beyond the displayed frame. The unconsumed suffix stays decoded.
            if(frameStart_+frameOffset_/static_cast<double>(sampleRate_)<cursor_) {
                const double skip=std::ceil((cursor_-frameStart_)*sampleRate_-1e-6);
                frameOffset_=static_cast<int>(std::min(skip,static_cast<double>(frame_->nb_samples)));
            }
            if(frameOffset_>=frame_->nb_samples) {
                av_frame_unref(frame_);frameReady_=false;continue;
            }
            const double start=frameStart_+frameOffset_/static_cast<double>(sampleRate_);
            if(start>cursor_)SilenceUntil(std::min(start,mediaSeconds));
            if(start>=mediaSeconds)break;
            const double available=std::floor((mediaSeconds-frameStart_)*sampleRate_+1e-6)-frameOffset_;
            const int count=static_cast<int>(std::max(0.0,std::min(available,
                static_cast<double>(std::min(ChunkFrames,frame_->nb_samples-frameOffset_)))));
            if(count==0)break;
            Convert(count);frameOffset_+=count;
            cursor_=frameStart_+frameOffset_/static_cast<double>(sampleRate_);
        }
        levels_=meter_->Levels();deadline_=0;return levels_;
    } catch(const std::bad_alloc&) {Fail(AVERROR(ENOMEM));}
      catch(...) {Fail(AVERROR(EIO));}
    return levels_;
}
