#include "HostTools.h"
#include "MediaTools.h"
#include "AudioRuleEngine.h"
#include <iostream>

void PrintHostCapabilities() {
    std::cout<<"{\"ffmpeg\":"<<JsonString(av_version_info())<<",\"configuration\":"<<JsonString(avcodec_configuration())<<",\"license\":"<<JsonString(avcodec_license())<<",\"decoders\":[";
    void* it=nullptr;const AVCodec* codec;bool first=true;
    while((codec=av_codec_iterate(&it))) if(av_codec_is_decoder(codec)&&codec->type==AVMEDIA_TYPE_VIDEO) {if(!first)std::cout<<',';std::cout<<JsonString(codec->name);first=false;}
    std::cout<<"],\"demuxers\":[";it=nullptr;first=true;const AVInputFormat* demux;
    while((demux=av_demuxer_iterate(&it))) {if(!first)std::cout<<',';std::cout<<JsonString(demux->name);first=false;}
    std::cout<<"],\"hardware\":[";auto type=AV_HWDEVICE_TYPE_NONE;first=true;
    while((type=av_hwdevice_iterate_types(type))!=AV_HWDEVICE_TYPE_NONE) {if(!first)std::cout<<',';std::cout<<JsonString(av_hwdevice_get_type_name(type));first=false;}
    std::cout<<"]}"<<std::endl;
}

void DiagnoseDesktop() {
        EnumWindows([](HWND hwnd,LPARAM)->BOOL {wchar_t cls[128];GetClassNameW(hwnd,cls,128);if(wcscmp(cls,L"Progman")==0||wcscmp(cls,L"WorkerW")==0) {RECT r;GetWindowRect(hwnd,&r);std::cout<<"{\"class\":"<<JsonString(Utf8(cls))<<",\"hwnd\":"<<(uintptr_t)hwnd<<",\"icons\":"<<(FindWindowExW(hwnd,nullptr,L"SHELLDLL_DefView",nullptr)?"true":"false")<<",\"visible\":"<<(IsWindowVisible(hwnd)?"true":"false")<<",\"width\":"<<r.right-r.left<<"}"<<std::endl;}return TRUE;},0);
}
bool RunMediaTool(const HostOptions& options,MediaSession& media,VideoFrame& frame,HostProtocol& protocol) {
        if(options.benchmarkSeconds>0) {protocol.Report(BenchmarkMedia(media,frame,options.benchmarkSeconds,options.fpsLimit));return true;}
        if(options.posterTime>=0) {
            frame=PosterFrameAt(media,options.posterTime,options.posterDirection);
            if(!options.poster.empty())SavePoster(frame,options.poster);
            protocol.Report(PosterFrameJson(media,frame));return true;
        }
        if(options.posterSession) {
            if(options.poster.empty())throw std::runtime_error("Poster output required");
            protocol.Report("{\"event\":\"poster-ready\"}");
            std::string line;
            while(std::getline(std::cin,line)) {
                if(line=="stop")break;
                std::istringstream input(line);input.imbue(std::locale::classic());std::string command;double positionSeconds=0;int direction=0;
                try {
                    if(!(input>>command>>positionSeconds>>direction)||command!="frame"||direction < -1||direction>1)throw std::runtime_error("Invalid poster command");
                    frame=PosterFrameAt(media,positionSeconds,direction);SavePoster(frame,options.poster);protocol.Report(PosterFrameJson(media,frame));
                }catch(const std::exception& e) {protocol.Report("{\"ok\":false,\"error\":"+JsonString(e.what())+"}");}
            }
            return true;
        }
        if(options.probe) {
            if(!options.stillImage&&!options.metadataOnly)FindPosterFrame(media,frame);
            if(!options.poster.empty()&&!options.metadataOnly) SavePoster(frame,options.poster);
            const auto quality=MeasurePosterFrame(frame);
            std::cerr<<"Poster selection: pts="<<frame.pts<<"; brightness="<<quality.brightness
                <<"; visible="<<quality.visible<<"; contrast="<<quality.contrast<<"; readable="<<quality.Readable()<<std::endl;
            std::ostringstream json;json<<"{\"ok\":true,\"error\":\"\",\"duration\":"<<media.duration<<",\"width\":"<<media.width<<",\"height\":"<<media.height<<",\"rotation\":"<<media.rotation<<",\"codec\":"<<JsonString(media.codecName)<<",\"color\":"<<JsonString(media.color)<<",\"decoder\":"<<JsonString(media.decoderName)<<",\"hasAudio\":"<<(media.hasAudio?"true":"false")<<",\"posterSeconds\":"<<std::setprecision(17)<<std::max(0.0,frame.pts-media.startTime)<<",\"sourceFps\":"<<media.sourceFps<<",\"metadata\":"<<media.metadataJson<<"}";protocol.Report(json.str());return true;
        }
    return false;
}
void RunImagePreview(const std::wstring& imagePreview,Renderer& renderer,VideoFrame& frame,HostProtocol& protocol) {
            // An isolated, hidden image renderer. No desktop attachment, audio device or playback thread.
            protocol.Report("{\"event\":\"image-ready\"}");
            std::string line;uint64_t index=0;AudioRuleEngine previewRules;double previewTime=0;
            while(std::getline(std::cin,line)) {
                if(line=="stop")break;
                std::istringstream input(line);std::string command;double time=0;int original=0;AudioDrive drive;
                if(!(input>>command>>time>>original>>drive.level>>drive.pulse>>drive.impact)||command!="frame"||!std::isfinite(time)||time<0||time>1e9)throw std::runtime_error("Invalid image preview command");
                std::string wire;std::getline(input,wire);auto previewEffects=VideoEffects::Parse(wire);
                drive.active=true;drive.motion=time*.5;drive.largePhase=std::fmod(time,6.0);drive.burst=static_cast<uint64_t>(time/6)+1;
                AudioLevels simulated{drive.level,drive.level,drive.level,drive.level,true,0};
                previewRules.Step(simulated,previewEffects,std::max(0.0,time-previewTime),time,drive);previewTime=time;
                frame.effectTime=time;frame.duration=1.0/8;frame.frameIndex=static_cast<uint32_t>(index++);
                renderer.Draw(frame,true,previewEffects,original!=0,false,drive);renderer.SaveFrame(imagePreview);
                protocol.Report("{\"event\":\"image-frame\"}");
            }

}
