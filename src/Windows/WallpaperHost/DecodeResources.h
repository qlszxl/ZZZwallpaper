#pragma once
#include <Windows.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <psapi.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <map>
#include <mutex>
#include <optional>
#include <ostream>
#include <string>
#include <thread>
#include <vector>
#pragma comment(lib,"pdh.lib")
#pragma comment(lib,"psapi.lib")

// Resource measurements belong only to this benchmark process. Sampling runs
// beside decoding; the UI process and other desktop players are never included.
class DecodeResources {
    struct Samples {
        double sum=0,weight=0,peak=0;unsigned count=0;
        void Add(double value,double seconds=1) {
            sum+=value*seconds;weight+=seconds;peak=std::max(peak,value);++count;
        }
        std::optional<double> Mean() const {return count?std::optional<double>(sum/weight):std::nullopt;}
        std::optional<double> Peak() const {return count?std::optional<double>(peak):std::nullopt;}
    };
    PDH_HQUERY query=nullptr;
    PDH_HCOUNTER gpu=nullptr,dedicated=nullptr,shared=nullptr;
    std::wstring processPrefix=L"pid_"+std::to_wstring(GetCurrentProcessId())+L"_";
    std::jthread worker;
    std::condition_variable_any wake;
    std::mutex gate;
    bool baseline=false;
    std::chrono::steady_clock::time_point collected;
    Samples gpuValues,workingSet,privateBytes,dedicatedBytes,sharedBytes;

    PDH_HCOUNTER Add(const wchar_t* path) {
        PDH_HCOUNTER counter=nullptr;
        return query&&PdhAddEnglishCounterW(query,path,0,&counter)==ERROR_SUCCESS?counter:nullptr;
    }
    std::optional<double> Read(PDH_HCOUNTER counter,bool engines) {
        if(!counter)return std::nullopt;
        for(int attempt=0;attempt<3;++attempt) {
            DWORD size=0,count=0;
            constexpr DWORD format=PDH_FMT_DOUBLE|PDH_FMT_NOSCALE|PDH_FMT_NOCAP100;
            if(PdhGetFormattedCounterArrayW(counter,format,&size,&count,nullptr)!=PDH_MORE_DATA||!size||size>16*1024*1024)return std::nullopt;
            std::vector<unsigned char> buffer(size);DWORD capacity=size;
            auto* items=reinterpret_cast<PDH_FMT_COUNTERVALUE_ITEM_W*>(buffer.data());
            auto status=PdhGetFormattedCounterArrayW(counter,format,&size,&count,items);
            if(status==PDH_MORE_DATA)continue; // Re-probe from zero if instances changed.
            if(status!=ERROR_SUCCESS||static_cast<size_t>(count)*sizeof(*items)>capacity)return std::nullopt;
            std::map<std::wstring,double> groups;bool providerValid=false,incomplete=false;
            for(DWORD i=0;i<count;++i) {
                if(!items[i].szName)continue;
                std::wstring name=items[i].szName;
                if(!name.starts_with(L"pid_"))continue;
                const auto& value=items[i].FmtValue;
                bool valid=(value.CStatus==PDH_CSTATUS_VALID_DATA||value.CStatus==PDH_CSTATUS_NEW_DATA)&&std::isfinite(value.doubleValue)&&value.doubleValue>=0;
                providerValid|=valid;
                if(!name.starts_with(processPrefix))continue;
                if(!valid) {incomplete=true;continue;}
                auto adapter=name.find(L"luid_");
                if(adapter==std::wstring::npos||(engines&&name.find(L"_eng_",adapter)==std::wstring::npos)) {incomplete=true;continue;}
                auto duplicate=name.find(L'#',adapter);
                groups[name.substr(adapter,duplicate==std::wstring::npos?std::wstring::npos:duplicate-adapter)]+=value.doubleValue;
            }
            if(incomplete||!providerValid)return std::nullopt;
            // A healthy provider with no instances for this PID means no GPU work.
            double total=0;
            for(const auto& group:groups)total=engines?std::max(total,group.second):total+group.second;
            return engines?std::clamp(total,0.0,100.0):total;
        }
        return std::nullopt;
    }
    void Memory() {
        PROCESS_MEMORY_COUNTERS_EX memory{};memory.cb=sizeof(memory);
        if(GetProcessMemoryInfo(GetCurrentProcess(),reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory))) {
            workingSet.Add(static_cast<double>(memory.WorkingSetSize));privateBytes.Add(static_cast<double>(memory.PrivateUsage));
        }
    }
    void Collect() {
        Memory();
        if(!query)return;
        auto now=std::chrono::steady_clock::now();
        if(PdhCollectQueryData(query)!=ERROR_SUCCESS) {baseline=false;collected=now;return;}
        double interval=std::chrono::duration<double>(now-collected).count();collected=now;
        if(baseline&&interval>0)if(auto value=Read(gpu,true))gpuValues.Add(*value,interval);
        baseline=true;
        if(auto value=Read(dedicated,false))dedicatedBytes.Add(*value);
        if(auto value=Read(shared,false))sharedBytes.Add(*value);
    }
public:
    DecodeResources() {
        if(PdhOpenQueryW(nullptr,0,&query)==ERROR_SUCCESS) {
            gpu=Add(L"\\GPU Engine(*)\\Utilization Percentage");
            dedicated=Add(L"\\GPU Process Memory(*)\\Dedicated Usage");
            shared=Add(L"\\GPU Process Memory(*)\\Shared Usage");
            baseline=PdhCollectQueryData(query)==ERROR_SUCCESS;
        }
        collected=std::chrono::steady_clock::now();
    }
    ~DecodeResources() {Stop();if(query)PdhCloseQuery(query);}
    DecodeResources(const DecodeResources&)=delete;
    DecodeResources& operator=(const DecodeResources&)=delete;
    void Start() {
        Memory();
        worker=std::jthread([this](std::stop_token stop) {
            try {
                std::unique_lock lock(gate);
                while(!stop.stop_requested()) {
                    wake.wait_for(lock,stop,std::chrono::milliseconds(250),[]{return false;});
                    if(!stop.stop_requested())Collect();
                }
            }catch(...) { } // Counter availability must not fail a decode benchmark.
        });
    }
    void Stop() {if(worker.joinable()) {worker.request_stop();wake.notify_all();worker.join();}}
    void Write(std::ostream& output) const {
        auto field=[&](const char* name,std::optional<double> value) {
            output<<",\""<<name<<"\":";if(value)output<<*value;else output<<"null";
        };
        field("gpuPercent",gpuValues.Mean());field("peakGpuPercent",gpuValues.Peak());
        field("workingSetBytes",workingSet.Mean());field("peakWorkingSetBytes",workingSet.Peak());
        field("privateBytes",privateBytes.Mean());field("peakPrivateBytes",privateBytes.Peak());
        field("dedicatedGpuBytes",dedicatedBytes.Mean());field("peakDedicatedGpuBytes",dedicatedBytes.Peak());
        field("sharedGpuBytes",sharedBytes.Mean());field("peakSharedGpuBytes",sharedBytes.Peak());
        output<<",\"gpuSamples\":"<<gpuValues.count<<",\"memorySamples\":"<<workingSet.count;
    }
};
