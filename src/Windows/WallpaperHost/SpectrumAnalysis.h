#pragma once
#include <array>
#include <complex>
#include <cmath>
#include <algorithm>
#include <mutex>

// Two-channel power spectrum avoids cancelling anti-phase stereo content.
// Hann-windowed FFT, logarithmic 40 Hz..16 kHz bands. No audio is retained on disk.
template<int Window=4096> class BasicSpectrumAnalysis {
public:
    static constexpr int BandCount=256;
private:
    static constexpr int N=Window,Bands=BandCount;
    std::array<std::array<float,N>,2> samples_{};
    std::array<float,N> window_{};
    std::array<float,Bands> bands_{};
    std::array<int,Bands> begins_{},ends_{};
    int rate_=0,channels_=0,write_=0,filled_=0,since_=0;
    mutable std::mutex mutex_;
    static void Fft(std::array<std::complex<double>,N>& values) {
        for(int i=1,j=0;i<N;i++) {int bit=N>>1;for(;j&bit;bit>>=1)j^=bit;j^=bit;if(i<j)std::swap(values[i],values[j]);}
        for(int length=2;length<=N;length<<=1) {
            auto root=std::polar(1.0,-6.283185307179586/length);
            for(int start=0;start<N;start+=length) {std::complex<double> phase=1;
                for(int j=0;j<length/2;j++) {auto a=values[start+j],b=values[start+j+length/2]*phase;values[start+j]=a+b;values[start+j+length/2]=a-b;phase*=root;}
            }
        }
    }
    void Analyze() {
        std::array<double,N/2> powers{};
        int count=std::min(channels_,2);
        for(int channel=0;channel<count;channel++) {
            std::array<std::complex<double>,N> values;
            for(int i=0;i<N;i++)values[i]=samples_[channel][(write_+i)%N]*window_[i];
            Fft(values);for(int i=1;i<N/2;i++)powers[i]+=std::norm(values[i])/(count*N*N/16.0);
        }
        std::array<float,Bands> result{};
        for(int band=0;band<Bands;band++) {
            int begin=begins_[band],end=ends_[band];
            double power=0;for(int i=begin;i<end;i++)power=std::max(power,powers[i]);
            result[band]=(float)std::sqrt(std::max(0.0,power));
        }
        std::lock_guard lock(mutex_);bands_=result;
    }
public:
    BasicSpectrumAnalysis() {for(int i=0;i<N;i++)window_[i]=(float)(.5-.5*std::cos(6.283185307179586*i/(N-1)));}
    void Reset() {rate_=channels_=write_=filled_=since_=0;samples_={};std::lock_guard lock(mutex_);bands_={};}
    bool Feed(const float* data,size_t frames,int rate,int channels,int frequency=30) {
        bool analyzed=false;
        if(rate!=rate_||channels!=channels_||frames==0) {
            rate_=rate;channels_=channels;write_=filled_=since_=0;samples_={};std::lock_guard lock(mutex_);bands_={};
            double high=std::min(16000.0,rate_*.48);
            for(int band=0;band<Bands;band++) {
                double low=40*std::pow(high/40.0,band/(double)Bands),upper=40*std::pow(high/40.0,(band+1)/(double)Bands);
                begins_[band]=std::clamp((int)std::floor(low*N/rate_),1,N/2-1);ends_[band]=std::clamp((int)std::ceil(upper*N/rate_),begins_[band]+1,N/2);
            }
        }
        for(size_t frame=0;frame<frames;frame++) {
            for(int channel=0;channel<std::min(channels,2);channel++) {float v=data?data[frame*channels+channel]:0;samples_[channel][write_]=std::isfinite(v)?std::clamp(v,-1.f,1.f):0;}
            write_=(write_+1)%N;filled_=std::min(N,filled_+1);
            if(++since_>=std::max(1,rate/frequency)&&filled_==N) {since_=0;Analyze();analyzed=true;}
        }
        return analyzed;
    }
    std::array<float,Bands> Snapshot() const {std::lock_guard lock(mutex_);return bands_;}
};

using SpectrumAnalysis=BasicSpectrumAnalysis<>;
