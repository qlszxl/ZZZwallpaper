#include "PlaybackSession.h"
#include "DesktopSurface.h"
#include "PlaybackDiagnostics.h"
#include "HostTools.h"
#include "FramePacer.h"
#include "PresentationClock.h"
#include "PosterFrame.h"
#include "VideoAudioOutput.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <thread>

PlaybackSession::PlaybackSession(HostOptions options,std::shared_ptr<HostProtocol> protocol)
    :options_(std::move(options)),state_(std::make_shared<PlaybackControlState>()),protocol_(std::move(protocol)) {
    state_->fpsLimit=options_.fpsLimit;state_->decoderMode=options_.decoderMode;
    state_->contain=options_.contain;state_->looping=options_.looping;
    state_->position=PackWallpaperPosition(options_.positionX,options_.positionY);
}
int PlaybackSession::Run() {
    VideoEffects effects;
    bool software=options_.software,compareOriginal=options_.compareOriginal,playbackEnded=false;
    try {
        if(!options_.effectText.empty())effects=VideoEffects::Parse(Utf8(options_.effectText));
        options_.ValidateSource();
        software=software||state_->decoderMode.load()==2;
        MediaSession media(state_->stopped); VideoFrame frame;
        auto firstFrame=[&]() {media.Open(Utf8(options_.file),software,(options_.probe||options_.posterSession||options_.posterTime>=0)?960:0);return media.Next(frame,!options_.probe);};
        try {if(!firstFrame()) throw std::runtime_error("No video frames");}
        catch(...) {if(!media.Hardware())throw;software=true;if(!firstFrame())throw std::runtime_error("No software decoded frames");}
        if(options_.stillImage) {media.duration=0;media.hasAudio=false;frame.pts=0;frame.effectTime=0;frame.duration=1.0/30;}
        if(RunMediaTool(options_,media,frame,*protocol_))return 0;
        for(int n=1;n<options_.captureFrame;++n)if(!media.Next(frame))throw std::runtime_error("Capture frame unavailable");
        DesktopSurface surface(options_,*state_);
        const auto window=surface.Handle(),parent=surface.Parent();
        const int w=surface.Width(),h=surface.Height();
        std::ofstream trace;
        if(!options_.traceFrames.empty()) {trace.open(std::filesystem::path(options_.traceFrames));if(!trace)throw std::runtime_error("Cannot open frame trace");}
        Renderer renderer;renderer.TraceFrames=!options_.traceFrames.empty();renderer.Open(window,w,h,media.HardwareDevice());
        bool gpuTransfers=media.HardwareDevice()!=nullptr&&renderer.Device()==media.HardwareDevice();
        media.SetGpuFrames(gpuTransfers);
        if(!options_.imagePreview.empty()) {RunImagePreview(options_.imagePreview,renderer,frame,*protocol_);return 0;}
        AudioReactive audio(Utf8(options_.file),state_->stopped);bool audioVisible=false;
        std::unique_ptr<VideoAudioOutput> sound;
        if(!options_.stillImage&&!options_.smoke&&!options_.headless&&!options_.preview)sound=std::make_unique<VideoAudioOutput>(Utf8(options_.file),state_->stopped,[state=state_]{return state->soundEnabled.load()&&!state->paused.load();},[protocol=protocol_](const char* status,int code) {
            protocol->Report("{\"event\":\"sound-status\",\"status\":"+JsonString(status)+",\"code\":"+std::to_string(code)+"}");
        },[state=state_]{return state->soundVolume.load()/100.f;});
        PlaybackDiagnostics diagnostics(*protocol_,renderer);
        std::optional<double> telemetryAudioPosition;
        FramePacer framePacer;bool redrawPending=false,frameDirty=true;
        PresentationClock effectClock(frame.effectTime);
        std::optional<uint64_t> acknowledgement;
        uint64_t presentationCount=0;double presentedEffectTime=frame.effectTime;
        uint64_t appliedPosition=state_->position.load();
        auto draw=[&](bool present=true) {
            bool animated=!compareOriginal&&effects.NeedsAnimation();
            if(present&&!framePacer.Allow(PresentationClock::Rate(state_->fpsLimit.load(),animated,options_.stillImage?0:media.sourceFps))) {redrawPending=true;return false;}
            redrawPending=false;
            // Override metadata only; retain the decoded pixel buffer and source clock.
            struct FrameMetadata {
                VideoFrame& frame;double time,pts;uint32_t index;
                ~FrameMetadata(){frame.effectTime=time;frame.pts=pts;frame.frameIndex=index;}
            } original{frame,frame.effectTime,frame.pts,frame.frameIndex};
            if(animated&&options_.renderFrame.empty()) {
                frame.effectTime=effectClock.Time();
                if(!options_.stillImage)frame.pts+=std::max(0.0,frame.effectTime-original.time);
                frame.frameIndex=static_cast<uint32_t>(std::fmod(std::max(0.0,frame.effectTime)*60,4294967296.0));
            }
            bool measure=diagnostics.Measuring()&&present;
            auto audioStart=measure?PerformanceInterval::Clock::now():PerformanceInterval::Clock::time_point{};
            audio.Configure(effects,compareOriginal,state_->paused||playbackEnded||!audioVisible);
            auto drive=audio.Sample(frame,effects);
            if(measure) {
                diagnostics.AudioMeasured(std::chrono::duration<double,std::milli>(PerformanceInterval::Clock::now()-audioStart).count());
            }
            const auto position=state_->position.load();
            try {renderer.Draw(frame,state_->contain,effects,compareOriginal,present,drive,frameDirty,WallpaperPositionX(position),WallpaperPositionY(position),WallpaperPositionScale(position));}
            catch(const HardwareFrameTransferError&) {
                gpuTransfers=false;media.SetGpuFrames(false);media.Materialize(frame);
                renderer.Draw(frame,state_->contain,effects,compareOriginal,present,drive,true,WallpaperPositionX(position),WallpaperPositionY(position),WallpaperPositionScale(position));
                protocol_->Report("{\"event\":\"transfer-fallback\",\"path\":\"cpu\"}");
            }
            frameDirty=false;
            if(present&&renderer.LastPresent!=S_OK) {redrawPending=true;return false;}
            presentedEffectTime=frame.effectTime;
            if(present)++presentationCount;
            // A throttled or occluded draw must never acknowledge the new revision.
            if(present&&acknowledgement) {
                protocol_->Report("{\"event\":\"configured\",\"revision\":"+std::to_string(*acknowledgement)+",\"passes\":"+std::to_string(renderer.LastPasses)+",\"paused\":"+(state_->paused?"true":"false")+",\"frame\":"+std::to_string(original.index)+",\"presentation\":"+std::to_string(presentationCount)+",\"hash\":\""+std::to_string(renderer.LastHash)+"\"}");
                acknowledgement.reset();
            }
            if(measure) {diagnostics.Presented();}
            diagnostics.ReportAudio(audio,effects);return true;
        };
        draw(options_.renderFrame.empty());
        if(!options_.renderFrame.empty()) {renderer.SaveFrame(options_.renderFrame);protocol_->Report("{\"event\":\"rendered\",\"passes\":"+std::to_string(renderer.LastPasses)+"}");surface.Close();return 0;}
        protocol_->Report("{\"event\":\"prepared\",\"decoder\":"+JsonString(media.decoderName)+",\"hwnd\":"+std::to_string((uintptr_t)window)+",\"parent\":"+std::to_string((uintptr_t)parent)+",\"layered\":"+(surface.Layered()?"true":"false")+",\"hasAudio\":"+(media.hasAudio?"true":"false")+"}");
        if(!options_.smoke) {
            // A blocked stdin reader outlives Run(), so capture the mailbox and output
            // by shared ownership rather than referencing stack session resources.
            std::thread([state=state_,protocol=protocol_] {protocol->ReadCommands(std::cin,*state);}).detach();
        } else state_->show=true;
        using Clock=std::chrono::steady_clock;auto start=Clock::now(),base=start,pauseStart=start;double firstEffectTime=frame.effectTime;bool visible=false,wasPaused=false,hasPending=false,pendingNaturalLoop=false;VideoFrame pendingFrame;uint64_t count=1,loops=0;
        auto parentCheck=start;
        int appliedDecoderMode=state_->decoderMode.load();bool autoFellBack=software;
        auto lastRecovery=start;auto lateSince=Clock::time_point{};
        auto recordFrame=[&]() {
            if(trace.is_open())trace<<"{\"frame\":"<<frame.frameIndex<<",\"pts\":"<<frame.pts<<",\"time\":"<<presentedEffectTime<<",\"presentation\":"<<presentationCount<<",\"wall\":"<<std::chrono::duration<double>(Clock::now()-start).count()<<",\"loop\":"<<(frame.looped?"true":"false")<<",\"hash\":\""<<renderer.LastHash<<"\",\"mean\":"<<renderer.LastMean<<"}"<<std::endl;
        };
        auto syncAudioClock=[&](Clock::time_point now) {
            telemetryAudioPosition.reset();
            if(!sound||!visible||state_->paused||wasPaused||playbackEnded)return false;
            auto position=sound->Position(loops);
            if(!position)return false;
            telemetryAudioPosition=position;
            // Schedule pictures against consumed audio, not the reverse. A
            // slow decode/draw must not pull already-played sound backwards.
            base=now-std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>(
                frame.effectTime-firstEffectTime+*position-frame.pts));
            return true;
        };
        while(!state_->stopped) {
            audio.SetDevice(state_->AudioDevice());
            surface.Pump();auto now=Clock::now();
            effectClock.Tick(visible&&!state_->paused&&!playbackEnded,now);
            // Repair the paused wall clock before publishing the resume point.
            if(wasPaused&&!state_->paused) {base+=now-pauseStart;wasPaused=false;protocol_->Report(playbackEnded?"{\"event\":\"ended\"}":"{\"event\":\"playing\"}");}
            syncAudioClock(now);
            if(sound) {
                double elapsed=std::chrono::duration<double>(now-base).count()-(frame.effectTime-firstEffectTime);
                sound->Update(frame.pts+elapsed,
                    state_->soundEnabled&&audioVisible&&!state_->paused&&!playbackEnded,loops);
            }
            diagnostics.Sync(state_->monitor.load()&&!options_.smoke&&!options_.headless,state_->paused.load()||playbackEnded||!audioVisible,media);
            diagnostics.ReportPerformance(media,frame,w,h,options_.stillImage,state_->paused.load()||playbackEnded,telemetryAudioPosition);
            const auto nextPosition=state_->position.load();
            if(nextPosition!=appliedPosition) {appliedPosition=nextPosition;redrawPending=true;}
            auto update=state_->TakeConfiguration();
            if(update) {
                if(update->replaceAudioSession && (effects.audioReactive||update->effects.audioReactive))audio.ResetForPreset();
                effects=std::move(update->effects);compareOriginal=update->compare;state_->contain=update->fit;
                acknowledgement=update->revision;
                if(draw())recordFrame();
            }
            audio.Configure(effects,compareOriginal,state_->paused||playbackEnded||!audioVisible);diagnostics.ReportAudio(audio,effects);
            // A paused wallpaper redraws only when its configuration changes.
            if((state_->paused||playbackEnded)&&redrawPending&&draw())recordFrame();
            if(options_.smoke&&std::chrono::duration<double>(now-start).count()>=options_.seconds) break;
            if(!options_.smoke&&!options_.preview&&!options_.headless&&now-parentCheck>std::chrono::milliseconds(500)) {
                surface.Maintain(visible);
                parentCheck=now;
            }
            if(!state_->show) {std::this_thread::sleep_for(std::chrono::milliseconds(10));continue;}
            if(!visible) {
                if(surface.Show())draw();
                // Present success alone only acknowledges submission. Wait for this process's
                // DirectX updates to reach DWM before the control center retires the old output.
                renderer.CompletePresentation(!options_.smoke&&!options_.headless);
                if(surface.RaiseAbovePredecessor()) {
                    renderer.CompletePresentation(true);
                }
                if(surface.Dissolving()) {
                    if(!surface.Reveal())break;
                    renderer.CompletePresentation(true);
                }
                visible=true;audioVisible=!options_.smoke&&!options_.headless;base=Clock::now();firstEffectTime=frame.effectTime;recordFrame();
                protocol_->Report("{\"event\":\"ready\",\"decoder\":"+JsonString(media.decoderName)+",\"present\":"+std::to_string((unsigned long)renderer.LastPresent)+"}");
            }
            if(state_->paused) {
                if(!wasPaused) {pauseStart=Clock::now();wasPaused=true;protocol_->Report("{\"event\":\"paused\"}");}
                std::this_thread::sleep_for(std::chrono::milliseconds(20));continue;
            }
            if(playbackEnded) {
                if(!state_->looping&&!state_->restartRequested.exchange(false)) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));continue;
                }
                state_->restartRequested=false;playbackEnded=false;
                base=Clock::now()-std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>(pendingFrame.effectTime-firstEffectTime));
                protocol_->Report("{\"event\":\"playing\"}");
                // Consume the already-decoded first frame without reopening the source.
                pendingNaturalLoop=false;
            }
            if(options_.stillImage) {
                // Decode once. Reuse the pixels while effects and system audio animate.
                if(compareOriginal||!effects.NeedsAnimation()) {
                    if(redrawPending&&draw())recordFrame();
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));continue;
                }
                if(draw()) {++count;recordFrame();}
                std::this_thread::sleep_for(std::chrono::milliseconds(2));
                continue;
            }
            auto recover=[&](bool changeDecoder,bool useSoftware,const char* reason) {
                const double elapsed=std::max(0.0,std::chrono::duration<double>(Clock::now()-base).count());
                double targetEffect=firstEffectTime+elapsed;
                double targetSource=frame.pts-media.startTime+std::max(0.0,targetEffect-frame.effectTime);
                if(media.duration>0)targetSource=std::min(targetSource,std::max(0.0,media.duration-frame.duration));
                auto loadRecovery=[&](bool reopen,bool soft) {
                    if(reopen) {software=soft;media.Open(Utf8(options_.file),software,0,{},renderer.Device());media.SetGpuFrames(gpuTransfers&&media.HardwareDevice()==renderer.Device());}
                    media.Seek(std::max(0.0,targetSource));pendingFrame={};
                    if(!media.Next(pendingFrame,false,0,-1,std::max(0.0,targetSource)))
                        pendingFrame=PosterFrameAt(media,targetSource);
                };
                try {loadRecovery(changeDecoder,useSoftware);}
                catch(...) {
                    if(!media.Hardware())throw;
                    autoFellBack=true;loadRecovery(true,true);
                }
                pendingFrame.effectTime=targetEffect+(pendingFrame.pts-media.startTime-targetSource);
                pendingFrame.frameIndex=frame.frameIndex+1;pendingFrame.looped=false;
                pendingNaturalLoop=false;hasPending=true;lastRecovery=Clock::now();lateSince={};
                protocol_->Report("{\"event\":\"decoder-changed\",\"decoder\":"+JsonString(media.decoderName)+",\"reason\":"+JsonString(reason)+"}");
            };
            if(appliedDecoderMode!=state_->decoderMode.load()) {
                appliedDecoderMode=state_->decoderMode.load();autoFellBack=appliedDecoderMode==2;
                recover(true,appliedDecoderMode==2,"setting");
            }
            // Prolonged overload gets a bounded recovery; never indefinitely suppress every picture.
            const double videoLag=std::chrono::duration<double>(now-base).count()-(frame.effectTime-firstEffectTime);
            if(videoLag>.20) {if(lateSince==Clock::time_point{})lateSince=now;}else lateSince={};
            if(lateSince!=Clock::time_point{}&&now-lateSince>std::chrono::seconds(1)&&now-lastRecovery>std::chrono::seconds(2)) {
                if(appliedDecoderMode==0&&!autoFellBack&&media.Hardware()) {autoFellBack=true;recover(true,true,"sustained-lateness");}
                else if(videoLag>.75)recover(false,software,"synchronize");
            }
            if(!hasPending) {
                // Decode into a second buffer. The displayed frame remains available for paused
                // redraws, and a pause during the due-time wait must not consume the next frame.
                pendingFrame.pts=frame.pts;pendingFrame.duration=frame.duration;
                pendingFrame.frameIndex=frame.frameIndex;pendingFrame.effectTime=frame.effectTime;
                bool next=false;
                bool measureDecode=diagnostics.Measuring();
                auto decodeStart=measureDecode?Clock::now():Clock::time_point{};
                try {double cutoff=std::max(-1.0,firstEffectTime+std::chrono::duration<double>(Clock::now()-base).count()-.04);next=media.Next(pendingFrame,true,state_->fpsLimit.load(),cutoff);pendingNaturalLoop=pendingFrame.looped;}
                catch(...) {
                    if(!media.Hardware())throw;autoFellBack=true;recover(true,true,"decode-error");
                    next=true;
                    protocol_->Report("{\"event\":\"fallback\",\"decoder\":\"Software\"}");
                }
                if(measureDecode) {
                    diagnostics.DecodeMeasured(std::chrono::duration<double,std::milli>(Clock::now()-decodeStart).count());
                }
                if(!next)break;hasPending=true;
            }
            now=Clock::now();
            bool audioClock=syncAudioClock(now);
            auto due=base+std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>(pendingFrame.effectTime-firstEffectTime));
            if(now<due) {
                if((redrawPending||(!compareOriginal&&effects.NeedsAnimation()))&&draw()) {++count;recordFrame();}
                std::this_thread::sleep_for(std::chrono::milliseconds(1));continue;
            }
            if(state_->stopped||state_->paused)continue;
            // Silent playback retains its wall-clock fallback. Audible playback
            // catches the picture up without changing the audio timeline.
            if(!audioClock&&now-due>std::chrono::milliseconds(250))base+=now-due;
            if(pendingNaturalLoop&&!state_->looping) {
                // EOF is acknowledged at the final frame's display deadline. Keep that
                // frame visible until the controller promotes the next prepared player.
                playbackEnded=true;protocol_->Report("{\"event\":\"ended\"}");continue;
            }
            std::swap(frame,pendingFrame);hasPending=false;frameDirty=true;
            if(draw()) {++count;recordFrame();}
            if(frame.looped) {++loops;protocol_->Report("{\"event\":\"loop\"}");}
        }
        if(window) {surface.Close();}
        protocol_->Report("{\"event\":\"stopped\",\"frames\":"+std::to_string(count)+",\"loops\":"+std::to_string(loops)+"}");return 0;
    } catch(const std::exception& error) {
        state_->stopped=true;
        protocol_->Report("{\"ok\":false,\"event\":\"error\",\"error\":"+JsonString(error.what())+"}");return 1;
    }
}
