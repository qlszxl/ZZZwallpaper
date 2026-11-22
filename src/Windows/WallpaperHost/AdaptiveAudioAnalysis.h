#pragma once
#include "AudioAnalysis.h"
#include "SpectrumAnalysis.h"
#include <Windows.h>
#include <atomic>

// Shared, sample-clock analysis. Continuous amplitude and transient evidence
// stay separate: changing the normalization gain cannot create an onset.
class AdaptiveAudioAnalysis {
    static constexpr int Groups=24,History=400;
    AudioMeter meter_;
    BasicSpectrumAnalysis<2048> fastSpectrum_;
    SpectrumAnalysis legacySpectrum_;
    AudioAnalysisFrame frame_{};
    int rate_=0,channels_=0,hop_=1,pending_=0,historyCount_=0,historyWrite_=0,ticks_=0;
    bool adaptive_=false,spectrum_=false,spectralReady_=false,fftSleeping_=false;
    double seconds_=0,silence_=0;
    std::array<std::array<float,4>,History> history_{};
    std::array<double,4> reference_{},targetReference_{},previousRaw_{},mean_{},deviation_{},previousNovelty_{},olderNovelty_{},previousGate_{},lastOnset_{};
    std::array<float,Groups> previousSpectrum_{},spectralMean_{};
    std::array<int,Groups> groupBand_{};
    std::array<float,800> rhythm_{};
    int rhythmWrite_=0,rhythmCount_=0;
    double period_=0,confidence_=0,lastRhythmOnset_=-100;

    static uint64_t Epoch() {
        static std::atomic_uint64_t last{0};LARGE_INTEGER qpc;QueryPerformanceCounter(&qpc);
        uint64_t value=static_cast<uint64_t>(qpc.QuadPart),old=last.load();
        while(!last.compare_exchange_weak(old,std::max(old+1,value))) {}
        return std::max(old+1,value);
    }
    void ClearAdaptive() {
        historyCount_=historyWrite_=ticks_=rhythmWrite_=rhythmCount_=0;seconds_=silence_=0;
        reference_.fill(.1);targetReference_.fill(.1);previousRaw_={};mean_={};deviation_={};
        previousNovelty_={};olderNovelty_={};previousGate_={};lastOnset_.fill(-100);
        previousSpectrum_={};spectralMean_={};spectralReady_=false;fftSleeping_=false;rhythm_={};period_=confidence_=0;lastRhythmOnset_=-100;
        frame_={};frame_.levels.epoch=Epoch();fastSpectrum_.Reset();legacySpectrum_.Reset();
    }
    void UpdateReference(const std::array<float,4>& raw) {
        history_[historyWrite_]=raw;historyWrite_=(historyWrite_+1)%History;historyCount_=std::min(historyCount_+1,History);
        if(historyCount_==1)for(int b=0;b<4;++b)reference_[b]=targetReference_[b]=std::max(.025,static_cast<double>(raw[b]));
        if(ticks_%25==0&&historyCount_>=10) {
            std::array<float,History> values{};
            for(int b=0;b<4;++b) {
                int count=0;
                for(int i=0;i<historyCount_;++i)if(history_[i][0]>.0003f)values[count++]=history_[i][b];
                if(count) {
                    int index=std::min(count-1,count*9/10);
                    std::nth_element(values.begin(),values.begin()+index,values.begin()+count);
                    targetReference_[b]=std::max(.025,static_cast<double>(values[index]));
                }
            }
        }
        for(int b=0;b<4;++b)reference_[b]+=(1-std::exp(-.01/(targetReference_[b]>reference_[b]?.15:.7)))*(targetReference_[b]-reference_[b]);
    }
    void Rhythm(float value) {
        rhythm_[rhythmWrite_]=value;rhythmWrite_=(rhythmWrite_+1)%static_cast<int>(rhythm_.size());
        rhythmCount_=std::min(rhythmCount_+1,static_cast<int>(rhythm_.size()));
        if(ticks_%50||rhythmCount_<200)return;
        auto at=[&](int ago) {return rhythm_[(rhythmWrite_-1-ago+static_cast<int>(rhythm_.size()))%rhythm_.size()];};
        double best=0;int bestLag=0;
        for(int lag=25;lag<=150;++lag) {
            double cross=0,a=0,b=0;int evidence=0;
            for(int i=0;i<rhythmCount_-lag-2;++i) {
                double x=at(i),y=std::max({at(i+lag-1),at(i+lag),at(i+lag+1)});
                cross+=x*y;a+=x*x;b+=y*y;if(x>.2&&y>.2)++evidence;
            }
            double score=evidence>=4?cross/std::sqrt(std::max(1e-9,a*b)):0;
            // Prefer an established period only when the new evidence agrees.
            if(period_>0&&std::abs(lag*.01-period_)<.04)score*=1.05;
            if(score>best) {best=score;bestLag=lag;}
        }
        confidence_=std::clamp(best,0.0,1.0);period_=bestLag*.01;
    }
    void Analyze(const std::array<float,256>& bands,const std::array<float,4>& raw,double gate) {
        std::array<float,Groups> current{};
        std::array<double,4> novelty{},weights{};
        for(int g=0;g<Groups;++g) {
            for(int i=g*256/Groups;i<(g+1)*256/Groups;++i)current[g]=std::max(current[g],bands[i]);
            if(spectralReady_) {
                float previous=std::max({previousSpectrum_[std::max(0,g-1)],previousSpectrum_[g],previousSpectrum_[std::min(Groups-1,g+1)]});
                // Both sides use the same reference; gain recovery is not evidence.
                double rise=std::max<double>(0.0,std::log1p(current[g]/std::max(.003f,spectralMean_[g]))-
                    std::log1p(previous/std::max(.003f,spectralMean_[g])));
                int band=groupBand_[g];novelty[0]+=rise;novelty[band]+=rise;weights[0]++;weights[band]++;
            }
            spectralMean_[g]+=.02f*(current[g]-spectralMean_[g]);
        }
        AudioOnset event;event.seconds=seconds_-.01;
        for(int b=0;b<4;++b) {
            novelty[b]=gate*(novelty[b]/std::max(1.0,weights[b])+.25*std::max(0.0,(raw[b]-previousRaw_[b])/std::max(.025,reference_[b])));
            double threshold=.012+mean_[b]+1.5*deviation_[b];
            if(spectralReady_&&seconds_>.08&&previousGate_[b]>.1&&previousNovelty_[b]>novelty[b]&&previousNovelty_[b]>=olderNovelty_[b]&&
                previousNovelty_[b]>threshold&&event.seconds-lastOnset_[b]>=.08) {
                double score=std::clamp(.35+.5*(previousNovelty_[b]/threshold-1),0.0,1.0);
                if(confidence_>.55&&period_>.0&&lastRhythmOnset_>0) {
                    double phase=std::remainder(event.seconds-lastRhythmOnset_,period_);
                    if(std::abs(phase)<std::min(.07,period_*.15))score=std::min(1.0,score*1.12);
                }
                event.strength[b]=static_cast<float>(score);lastOnset_[b]=event.seconds;
            }
            // A bounded innovation keeps one explosion from raising the baseline
            // for the following several seconds.
            double delta=std::clamp(novelty[b]-mean_[b],-.15,.15);
            mean_[b]+=.02*delta;deviation_[b]+=.02*(std::abs(delta)-deviation_[b]);
            olderNovelty_[b]=previousNovelty_[b];previousNovelty_[b]=novelty[b];previousGate_[b]=gate;previousRaw_[b]=raw[b];
        }
        if(*std::max_element(event.strength.begin(),event.strength.end())>0) {
            event.sequence=++frame_.levels.onsetSequence;
            frame_.levels.onsets[(event.sequence-1)%frame_.levels.onsets.size()]=event;
        }
        if(event.strength[0]>.45)lastRhythmOnset_=event.seconds;
        Rhythm(event.strength[0]);previousSpectrum_=current;spectralReady_=true;
    }
public:
    void Reset(int rate,int channels) {
        rate_=rate;channels_=channels;hop_=std::max(1,rate/100);pending_=0;
        meter_.Reset(rate,channels);ClearAdaptive();
        double high=std::min(16000.0,rate*.48);
        for(int g=0;g<Groups;++g) {double hz=40*std::pow(high/40.0,(g+.5)/Groups);groupBand_[g]=hz<180?1:hz<2500?2:3;}
    }
    void Configure(bool adaptive,bool spectrum=false) {
        if(adaptive_!=adaptive) {adaptive_=adaptive;pending_=0;ClearAdaptive();}
        if(spectrum&&!spectrum_)legacySpectrum_.Reset();spectrum_=spectrum;
    }
    void ProcessInterleaved(const float* samples,size_t frames) {
        if(rate_<=0||channels_<=0)return;
        while(frames) {
            size_t count=std::min(frames,static_cast<size_t>(hop_-pending_));
            meter_.ProcessInterleaved(samples,count);auto measured=meter_.Levels();
            bool analyzed=false;
            if(adaptive_) {
                bool sleep=silence_>=1&&measured.level<.0003f;
                if(sleep!=fftSleeping_) {fastSpectrum_.Reset();spectralReady_=false;previousNovelty_={};olderNovelty_={};fftSleeping_=sleep;}
                if(!sleep)analyzed=fastSpectrum_.Feed(samples,count,rate_,channels_,100);
            }
            else if(spectrum_)legacySpectrum_.Feed(samples,count,rate_,channels_);
            pending_+=static_cast<int>(count);frames-=count;if(samples)samples+=count*channels_;
            if(pending_<hop_)continue;
            pending_=0;seconds_+=static_cast<double>(hop_)/rate_;++ticks_;
            std::array<float,4> raw{measured.level,measured.bass,measured.mid,measured.treble};
            if(adaptive_) {
                silence_=raw[0]<.0003f?silence_+.01:0;
                if(silence_>=1) {
                    // Forget an old song during silence, without generating a new
                    // epoch on every silent block or retaining old beat events.
                    if(silence_<1.011) {historyCount_=historyWrite_=0;spectralReady_=false;mean_={};deviation_={};previousNovelty_={};olderNovelty_={};rhythm_={};rhythmCount_=0;confidence_=period_=0;}
                }
                UpdateReference(raw);
                double gate=std::clamp((raw[0]-.00015)/.00065,0.0,1.0);gate*=gate*(3-2*gate);
                auto bands=fastSpectrum_.Snapshot();
                if(analyzed)Analyze(bands,raw,gate);
                std::array<float,4> values{};
                for(int b=0;b<4;++b)values[b]=static_cast<float>(std::clamp(gate*.8*raw[b]/std::max(.025,reference_[b]),0.0,1.0));
                measured.level=values[0];measured.bass=values[1];measured.mid=values[2];measured.treble=values[3];
                if(spectrum_)for(size_t i=0;i<bands.size();++i) {
                    int band=groupBand_[std::min(Groups-1,static_cast<int>(i*Groups/bands.size()))];
                    frame_.bands[i]=static_cast<float>(std::clamp(bands[i]*gate*.8/std::max(.025,reference_[band]),0.0,8.0));
                }
            } else if(spectrum_)frame_.bands=legacySpectrum_.Snapshot();
            // Preserve the shared event ring while refreshing the level snapshot.
            frame_.levels.level=measured.level;frame_.levels.bass=measured.bass;frame_.levels.mid=measured.mid;frame_.levels.treble=measured.treble;
            frame_.levels.available=measured.available;frame_.levels.error=measured.error;
            frame_.levels.adaptive=adaptive_;frame_.levels.seconds=seconds_;frame_.levels.raw=raw;
        }
    }
    AudioLevels Levels() const {auto result=frame_.levels;if(!adaptive_) {auto raw=meter_.Levels();result.level=raw.level;result.bass=raw.bass;result.mid=raw.mid;result.treble=raw.treble;result.available=raw.available;}return result;}
    AudioAnalysisFrame Snapshot() const {auto result=frame_;result.levels=Levels();return result;}
};
