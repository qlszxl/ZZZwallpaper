#include "MediaSession.h"
#include "AlphaPixels.h"
#include <Windows.h>
#include <d3d11.h>
extern "C" {
#include <libavutil/hwcontext_d3d11va.h>
}
#include <algorithm>
#include <cmath>
#include <fstream>
#include <filesystem>
#include <chrono>
#include <thread>
#include <sstream>
#include <locale>

static long long Millis() { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count(); }
static std::runtime_error Error(const char* operation,int code) {
    char message[AV_ERROR_MAX_STRING_SIZE]{}; av_strerror(code,message,sizeof(message));
    return std::runtime_error(std::string(operation)+": "+message);
}
int MediaSession::Interrupt(void* opaque) {
    auto* self=static_cast<MediaSession*>(opaque);
    return self->cancelled || (self->deadline.load()>0 && Millis()>self->deadline.load());
}
AVPixelFormat MediaSession::SelectFormat(AVCodecContext*,const AVPixelFormat* formats) {
    for(auto p=formats;*p!=AV_PIX_FMT_NONE;++p) if(*p==AV_PIX_FMT_D3D11) return *p;
    return formats[0];
}
void MediaSession::Close() {
    avfilter_graph_free(&filterGraph); filterSource=nullptr; filterSink=nullptr;
    sws_freeContext(scaler); scaler=nullptr;
    sws_freeContext(alphaScaler);alphaScaler=nullptr;
    av_frame_free(&decoded); av_frame_free(&downloaded); av_frame_free(&filtered); av_packet_free(&packet);
    avcodec_free_context(&codec); av_buffer_unref(&hardware); avformat_close_input(&format);
}
void MediaSession::Open(const std::string& file,bool software,int maxFrameDimension,MediaDecodeLimits sourceLimits,ID3D11Device* sharedDevice) {
    Close(); draining=false; nextLoop=false; useHardware=false;hasAudio=false;
    exportHardwareFrames=false;
    selectionRate=0;nextSelectionTime=0;decodedFrames=selectedFrames=0;
    maximumFrameDimension=std::clamp(maxFrameDimension,0,4096);
    limits=sourceLimits;
    format=avformat_alloc_context(); if(!format) throw std::bad_alloc();
    format->interrupt_callback={Interrupt,this}; deadline=Millis()+15000;
    AVDictionary* options=nullptr;
    av_dict_set(&options,"protocol_whitelist","file,crypto,data",0);
    if(limits.exactGifTiming)av_dict_set(&options,"min_delay","1",0);
    int result=avformat_open_input(&format,file.c_str(),nullptr,&options); av_dict_free(&options);
    if(result<0) throw Error("Open source",result);
    result=avformat_find_stream_info(format,nullptr); if(result<0) throw Error("Probe streams",result);
    for(unsigned i=0;i<format->nb_streams;++i)
        if(format->streams[i]->codecpar->codec_type==AVMEDIA_TYPE_AUDIO)hasAudio=true;
    stream=av_find_best_stream(format,AVMEDIA_TYPE_VIDEO,-1,-1,nullptr,0);
    if(stream<0) throw std::runtime_error("No decodable video stream in this file");
    auto* video=format->streams[stream]; auto* parameters=video->codecpar;
    if((limits.maxDimension>0&&(parameters->width>limits.maxDimension||parameters->height>limits.maxDimension))||
        (limits.maxPixels>0&&int64_t(parameters->width)*parameters->height>limits.maxPixels))throw std::runtime_error("Source exceeds bounded decoder dimensions");
    startTime=video->start_time==AV_NOPTS_VALUE?0:video->start_time*av_q2d(video->time_base);
    auto guessedRate=av_guess_frame_rate(format,video,nullptr);sourceFps=guessedRate.num>0?av_q2d(guessedRate):0;
    std::ostringstream details;details.imbue(std::locale::classic());const auto* pixel=av_get_pix_fmt_name(static_cast<AVPixelFormat>(parameters->format));
    const auto* descriptor=av_pix_fmt_desc_get(static_cast<AVPixelFormat>(parameters->format));
    details<<"{\"averageFrameRate\":"<<(video->avg_frame_rate.den?av_q2d(video->avg_frame_rate):0)
        <<",\"nominalFrameRate\":"<<(video->r_frame_rate.den?av_q2d(video->r_frame_rate):0)
        <<",\"vfr\":null,\"vfrReason\":\"requires_complete_timestamp_scan\",\"containerBitrate\":"<<format->bit_rate
        <<",\"videoBitrate\":"<<parameters->bit_rate<<",\"bitDepth\":";
    if(parameters->bits_per_raw_sample>0)details<<parameters->bits_per_raw_sample;else if(descriptor)details<<descriptor->comp[0].depth;else details<<"null";
    details<<",\"pixelFormat\":"<<(pixel?JsonString(pixel):"null")<<",\"audioTracks\":[";bool firstTrack=true;
    for(unsigned i=0;i<format->nb_streams;++i) {auto* track=format->streams[i]->codecpar;if(track->codec_type!=AVMEDIA_TYPE_AUDIO)continue;if(!firstTrack)details<<',';firstTrack=false;
        details<<"{\"index\":"<<i<<",\"codec\":"<<JsonString(avcodec_get_name(track->codec_id))<<",\"sampleRate\":"<<track->sample_rate<<",\"channels\":"<<track->ch_layout.nb_channels<<",\"bitrate\":"<<track->bit_rate<<"}";}
    details<<"]}";metadataJson=details.str();
    auto transfer=parameters->color_trc;
    if(transfer==AVCOL_TRC_SMPTE2084 || transfer==AVCOL_TRC_ARIB_STD_B67) throw std::runtime_error("HDR10/HLG requires verified SDR tone mapping; use an SDR source in this build");
    auto* implementation=avcodec_find_decoder(parameters->codec_id);
    if(!implementation) throw std::runtime_error("Video decoder is not included in this FFmpeg build");
    codecName=implementation->name; color="SDR";
    if(!software) {
        for(int i=0;;++i) {
            auto* config=avcodec_get_hw_config(implementation,i); if(!config) break;
            if(config->device_type==AV_HWDEVICE_TYPE_D3D11VA && (config->methods&AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX)) {
                if(sharedDevice) {
                    hardware=av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA);
                    if(hardware) {
                        auto* context=static_cast<AVD3D11VADeviceContext*>(reinterpret_cast<AVHWDeviceContext*>(hardware->data)->hwctx);
                        sharedDevice->AddRef();context->device=sharedDevice;
                        if(av_hwdevice_ctx_init(hardware)>=0)useHardware=true;
                        else av_buffer_unref(&hardware);
                    }
                }
                if(!useHardware&&av_hwdevice_ctx_create(&hardware,AV_HWDEVICE_TYPE_D3D11VA,nullptr,nullptr,0)>=0)useHardware=true;
                break;
            }
        }
    }
    auto setup=[&]() {
        codec=avcodec_alloc_context3(implementation); if(!codec) throw std::bad_alloc();
        int r=avcodec_parameters_to_context(codec,parameters); if(r<0) throw Error("Decoder parameters",r);
        codec->thread_count=limits.threads>0?std::clamp(limits.threads,1,8):std::min(8,std::max(1,(int)std::thread::hardware_concurrency()/2));
        if(limits.maxPixels>0)codec->max_pixels=limits.CodecBufferPixels();
        if(useHardware) {codec->hw_device_ctx=av_buffer_ref(hardware);codec->get_format=SelectFormat;}
        return avcodec_open2(codec,implementation,nullptr);
    };
    result=setup();
    if(result<0 && useHardware) {avcodec_free_context(&codec);av_buffer_unref(&hardware);useHardware=false;result=setup();}
    if(result<0) throw Error("Open decoder",result);
    decoderName=useHardware?"D3D11VA":"Software";
    width=parameters->width;height=parameters->height;
    if(width<=0||height<=0) throw std::runtime_error("Invalid video dimensions");
    duration=format->duration!=AV_NOPTS_VALUE?format->duration/(double)AV_TIME_BASE:0;
    rotation=0;
    auto* matrix=av_packet_side_data_get(parameters->coded_side_data,parameters->nb_coded_side_data,AV_PKT_DATA_DISPLAYMATRIX);
    if(matrix && matrix->size>=9*sizeof(int32_t)) { rotation=-av_display_rotation_get((int32_t*)matrix->data);if(!std::isfinite(rotation)) rotation=0; }
    decoded=av_frame_alloc(); downloaded=av_frame_alloc(); filtered=av_frame_alloc(); packet=av_packet_alloc();
    if(!decoded||!downloaded||!filtered||!packet) throw std::bad_alloc();
    deadline=0;
}

AVFrame* MediaSession::Deinterlace(AVFrame* input) {
    if(!(input->flags&AV_FRAME_FLAG_INTERLACED) && !filterGraph) return input;
    if(!filterGraph) {
        filterGraph=avfilter_graph_alloc();
        if(!filterGraph) throw std::bad_alloc();
        auto sar=input->sample_aspect_ratio; if(!sar.num||!sar.den) sar={1,1};
        auto tb=format->streams[stream]->time_base;
        char args[256]; snprintf(args,sizeof(args),"video_size=%dx%d:pix_fmt=%d:time_base=%d/%d:pixel_aspect=%d/%d",input->width,input->height,input->format,tb.num,tb.den,sar.num,sar.den);
        AVFilterContext* yadif=nullptr;
        int r=avfilter_graph_create_filter(&filterSource,avfilter_get_by_name("buffer"),"in",args,nullptr,filterGraph);
        if(r>=0) r=avfilter_graph_create_filter(&yadif,avfilter_get_by_name("yadif"),"deinterlace","mode=send_frame:parity=auto:deint=interlaced",nullptr,filterGraph);
        if(r>=0) r=avfilter_graph_create_filter(&filterSink,avfilter_get_by_name("buffersink"),"out",nullptr,nullptr,filterGraph);
        if(r>=0) r=avfilter_link(filterSource,0,yadif,0);
        if(r>=0) r=avfilter_link(yadif,0,filterSink,0);
        if(r>=0) r=avfilter_graph_config(filterGraph,nullptr);
        if(r<0) throw Error("Deinterlace",r);
    }
    int r=av_buffersrc_add_frame_flags(filterSource,input,AV_BUFFERSRC_FLAG_KEEP_REF);
    if(r<0) throw Error("Deinterlace input",r);
    av_frame_unref(filtered); r=av_buffersink_get_frame(filterSink,filtered);
    if(r==AVERROR(EAGAIN)) return nullptr;
    if(r<0) throw Error("Deinterlace output",r);
    return filtered;
}

void MediaSession::Seek(double relativeSeconds) {
    auto* video=format->streams[stream];
    const int64_t origin=video->start_time==AV_NOPTS_VALUE?0:video->start_time;
    deadline=Millis()+10000;
    int result=av_seek_frame(format,stream,origin+static_cast<int64_t>(relativeSeconds/av_q2d(video->time_base)),AVSEEK_FLAG_BACKWARD);
    deadline=0;
    if(result<0)throw Error("Thumbnail seek",result);
    avcodec_flush_buffers(codec);av_packet_unref(packet);draining=false;nextLoop=false;
    selectionRate=0;nextSelectionTime=0;
    avfilter_graph_free(&filterGraph);filterSource=nullptr;filterSink=nullptr;
}

bool MediaSession::Next(VideoFrame& output,bool allowLoop,int fps,double minimumEffectTime,double minimumSourceSeconds) {
    const auto workStarted=std::chrono::steady_clock::now();
    fps=std::clamp(fps,0,240);
    auto select=[&](AVFrame* frame,AVRational tb) {
        ++decodedFrames;
        auto ts=frame->best_effort_timestamp;if(frame==filtered)ts=frame->pts;
        double previousPts=output.pts,previousDuration=output.duration;
        output.pts=ts!=AV_NOPTS_VALUE?ts*av_q2d(tb):output.pts+output.duration;
        output.duration=frame->duration>0?frame->duration*av_q2d(tb):(sourceFps>0?1.0/sourceFps:1.0/30);
        output.looped=nextLoop;nextLoop=false;++output.frameIndex;
        double step=output.pts-previousPts;
        if(output.frameIndex>1)output.effectTime+=!output.looped&&step>0&&step<5?step:previousDuration;
        if(minimumSourceSeconds>=0&&output.pts-startTime+1e-7<minimumSourceSeconds)return false;
        if(selectionRate!=fps||output.looped) {selectionRate=fps;nextSelectionTime=output.effectTime;}
        // Lateness catch-up is bounded: even sustained overload must produce pictures.
        bool catchupBudget=std::chrono::steady_clock::now()-workStarted<std::chrono::milliseconds(80);
        if(!output.looped&&output.frameIndex>1&&minimumEffectTime>=0&&output.effectTime<minimumEffectTime&&catchupBudget) {++lateSkippedFrames;return false;}
        if(fps>0) {
            if(output.effectTime+1e-7<nextSelectionTime) {++rateSkippedFrames;return false;}
            double period=1.0/fps;
            nextSelectionTime+=period*(std::floor(std::max(0.0,output.effectTime-nextSelectionTime)/period)+1);
        }
        return true;
    };
    while(!cancelled) {
        int result=avcodec_receive_frame(codec,decoded);
        if(result==0) {
            AVFrame* frame=decoded;
            useHardware=decoded->format==AV_PIX_FMT_D3D11;
            decoderName=useHardware?"D3D11VA":"Software";
            bool filteredTiming=(decoded->flags&AV_FRAME_FLAG_INTERLACED)||filterGraph;
            if(!filteredTiming&&!select(decoded,format->streams[stream]->time_base)) {av_frame_unref(decoded);continue;}
            bool direct=exportHardwareFrames&&decoded->format==AV_PIX_FMT_D3D11&&!filteredTiming&&maximumFrameDimension==0;
            if(decoded->format==AV_PIX_FMT_D3D11&&!direct) {
                av_frame_unref(downloaded); result=av_hwframe_transfer_data(downloaded,decoded,0);
                if(result<0) throw Error("Download hardware frame",result);
                av_frame_copy_props(downloaded,decoded); frame=downloaded;
            }
            if(frame->color_trc==AVCOL_TRC_SMPTE2084 || frame->color_trc==AVCOL_TRC_ARIB_STD_B67) throw std::runtime_error("HDR frame requires SDR tone mapping");
            if(!direct)frame=Deinterlace(frame);
            if(!frame) {av_frame_unref(decoded);continue;}
            if((limits.maxDimension>0&&(frame->width>limits.maxDimension||frame->height>limits.maxDimension))||
                (limits.maxPixels>0&&int64_t(frame->width)*frame->height>limits.maxPixels))throw std::runtime_error("Decoded frame exceeds bounded decoder dimensions");
            if(filteredTiming&&!select(frame,frame==filtered?av_buffersink_get_time_base(filterSink):format->streams[stream]->time_base)) {av_frame_unref(decoded);continue;}
            auto transfer=frame->color_trc;
            if(transfer==AVCOL_TRC_UNSPECIFIED)transfer=format->streams[stream]->codecpar->color_trc;
            bool assumed=transfer==AVCOL_TRC_UNSPECIFIED;
            if(assumed) {
                auto* desc=av_pix_fmt_desc_get((AVPixelFormat)frame->format);
                transfer=desc&&(desc->flags&AV_PIX_FMT_FLAG_RGB)?AVCOL_TRC_IEC61966_2_1:AVCOL_TRC_BT709;
            }
            switch(transfer) {
                case AVCOL_TRC_BT709:case AVCOL_TRC_SMPTE170M:case AVCOL_TRC_SMPTE240M:case AVCOL_TRC_BT2020_10:case AVCOL_TRC_BT2020_12:
                case AVCOL_TRC_IEC61966_2_1:case AVCOL_TRC_GAMMA22:case AVCOL_TRC_GAMMA28:case AVCOL_TRC_LINEAR:break;
                default:throw std::runtime_error("Unsupported SDR transfer function");
            }
            output.transfer=transfer;++selectedFrames;
            color=std::string("SDR / ")+av_color_transfer_name(transfer)+(assumed?" (assumed)":"");
            output.width=frame->width;output.height=frame->height;output.rotation=rotation;
            if(maximumFrameDimension>0) {
                const double scale=std::min(1.0,maximumFrameDimension/static_cast<double>(std::max(frame->width,frame->height)));
                output.width=std::max(1,static_cast<int>(frame->width*scale));
                output.height=std::max(1,static_cast<int>(frame->height*scale));
            }
            auto sar=frame->sample_aspect_ratio;
            output.aspect=(double)frame->width/frame->height*(sar.num>0&&sar.den>0?av_q2d(sar):1);
            output.hardware.reset();
            if(direct) {
                auto* retained=av_frame_clone(frame);if(!retained)throw std::bad_alloc();
                output.hardware=std::shared_ptr<AVFrame>(retained,[](AVFrame* value){av_frame_free(&value);});
                output.colorSpace=frame->colorspace;output.fullRange=frame->color_range==AVCOL_RANGE_JPEG;
                output.bgra.clear();av_frame_unref(decoded);return true;
            }
            ConvertPixels(frame,output);
            av_frame_unref(decoded);

            return true;
        }
        if(result==AVERROR_EOF) {
            if(!allowLoop)return false;
            deadline=Millis()+10000;
            auto start=format->streams[stream]->start_time;
            // Transport streams may have a nonzero PTS origin and incomplete keyframe indexes.
            // Rewind their byte stream, otherwise timestamp seeking can omit the first GOP on subsequent loops.
            if(std::string(format->iformat->name)=="mpegts")result=av_seek_frame(format,-1,0,AVSEEK_FLAG_BYTE);
            else result=av_seek_frame(format,stream,start==AV_NOPTS_VALUE?0:start,AVSEEK_FLAG_BACKWARD);
            deadline=0;
            if(result<0) throw Error("Loop seek",result);
            avcodec_flush_buffers(codec); draining=false; nextLoop=true;
            avfilter_graph_free(&filterGraph);filterSource=nullptr;filterSink=nullptr;
            continue;
        }
        if(result!=AVERROR(EAGAIN)) throw Error("Decode video",result);
        if(draining) throw std::runtime_error("Decoder drain did not reach EOF");
        do {
            av_packet_unref(packet);deadline=Millis()+10000;result=av_read_frame(format,packet);deadline=0;
        } while(result>=0 && packet->stream_index!=stream && !cancelled);
        if(result==AVERROR_EOF) { result=avcodec_send_packet(codec,nullptr); draining=true; }
        else if(result<0) throw Error("Read video packet",result);
        else result=avcodec_send_packet(codec,packet);
        av_packet_unref(packet);
        if(result<0 && result!=AVERROR_EOF) throw Error("Submit video packet",result);
    }
    return false;
}


void MediaSession::ConvertPixels(AVFrame* frame,VideoFrame& output) {
            output.bgra.resize((size_t)output.width*output.height*4);
            const bool alphaResize=limits.exactGifTiming&&(output.width!=frame->width||output.height!=frame->height);
            const int conversionWidth=alphaResize?frame->width:output.width,conversionHeight=alphaResize?frame->height:output.height;
            std::vector<uint8_t> alphaPixels;if(alphaResize)alphaPixels.resize(size_t(conversionWidth)*conversionHeight*4);
            scaler=sws_getCachedContext(scaler,frame->width,frame->height,(AVPixelFormat)frame->format,conversionWidth,conversionHeight,AV_PIX_FMT_BGRA,SWS_BILINEAR,nullptr,nullptr,nullptr);
            if(!scaler) throw std::runtime_error("Cannot allocate bounded color conversion buffer");
            int space=SWS_CS_DEFAULT;
            if(frame->colorspace==AVCOL_SPC_BT709) space=SWS_CS_ITU709;
            else if(frame->colorspace==AVCOL_SPC_BT2020_NCL||frame->colorspace==AVCOL_SPC_BT2020_CL) space=SWS_CS_BT2020;
            auto* coefficients=sws_getCoefficients(space);
            sws_setColorspaceDetails(scaler,coefficients,frame->color_range==AVCOL_RANGE_JPEG,coefficients,1,0,1<<16,1<<16);
            uint8_t* pixels[]={alphaResize?alphaPixels.data():output.bgra.data()}; int strides[]={conversionWidth*4};
            int result=sws_scale(scaler,frame->data,frame->linesize,0,frame->height,pixels,strides);
            if(result>0&&alphaResize) {
                PremultiplyAlpha(alphaPixels);
                alphaScaler=sws_getCachedContext(alphaScaler,conversionWidth,conversionHeight,AV_PIX_FMT_BGRA,output.width,output.height,AV_PIX_FMT_BGRA,SWS_BILINEAR,nullptr,nullptr,nullptr);
                if(!alphaScaler)throw std::runtime_error("Cannot allocate alpha-preserving resize buffer");
                const uint8_t* source[]={alphaPixels.data()};int sourceStride[]={conversionWidth*4};uint8_t* target[]={output.bgra.data()};int targetStride[]={output.width*4};
                result=sws_scale(alphaScaler,source,sourceStride,0,conversionHeight,target,targetStride);
                UnpremultiplyAlpha(output.bgra);
            }
    if(result<=0)throw std::runtime_error("Color conversion failed");
}

ID3D11Device* MediaSession::HardwareDevice() const {
    if(!hardware||!useHardware)return nullptr;
    auto* context=reinterpret_cast<AVHWDeviceContext*>(hardware->data);
    return static_cast<AVD3D11VADeviceContext*>(context->hwctx)->device;
}
void MediaSession::Materialize(VideoFrame& output) {
    if(!output.hardware)return;
    av_frame_unref(downloaded);
    int result=av_hwframe_transfer_data(downloaded,output.hardware.get(),0);
    if(result<0)throw Error("Download fallback frame",result);
    if(av_frame_copy_props(downloaded,output.hardware.get())<0)throw std::bad_alloc();
    ConvertPixels(downloaded,output);
    output.hardware.reset();
}
