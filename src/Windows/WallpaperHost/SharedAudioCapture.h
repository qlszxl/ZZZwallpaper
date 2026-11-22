#pragma once
#include "AudioAnalysis.h"
#include <Windows.h>
#include <string>
#include <stdexcept>
#include <type_traits>

// One producer per endpoint and application profile, across wallpaper and
// spectrum processes. An abandoned producer mutex permits automatic takeover.
// Readers copy a bounded snapshot; audio samples never cross process boundaries.
class SharedAudioCapture {
    struct Lease {ULONGLONG token=0,seen=0;bool spectrum=false;};
    static_assert(std::is_trivially_copyable_v<AudioAnalysisFrame>);
    struct Block {ULONGLONG published=0,overflowSpectrumUntil=0;AudioAnalysisFrame frame{};std::array<Lease,64> leases{};};
    HANDLE mapping_=nullptr,lock_=nullptr,owner_=nullptr;
    Block* block_=nullptr;
    bool owns_=false,spectrum_=false;
    ULONGLONG token_=0,renewed_=0;
    int slot_=-1;
    bool Lock(DWORD wait=0) {
        DWORD result=WaitForSingleObject(lock_,wait);
        if(result==WAIT_ABANDONED) {block_->published=0;return true;}
        return result==WAIT_OBJECT_0;
    }
public:
    SharedAudioCapture(const std::wstring& endpoint,const std::wstring& profile,bool spectrum):spectrum_(spectrum) {
        uint64_t hash=14695981039346656037ull;
        for(wchar_t c:profile+L"|"+endpoint) {hash^=static_cast<uint16_t>(c);hash*=1099511628211ull;}
        std::wstring name=L"Local\\ZZZwallpaper.AudioAnalysis.v1."+std::to_wstring(hash);
        mapping_=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Block),(name+L".snapshot").c_str());
        if(mapping_)block_=static_cast<Block*>(MapViewOfFile(mapping_,FILE_MAP_ALL_ACCESS,0,0,sizeof(Block)));
        lock_=CreateMutexW(nullptr,FALSE,(name+L".lock").c_str());
        owner_=CreateMutexW(nullptr,FALSE,(name+L".producer").c_str());
        if(!block_||!lock_||!owner_) {Close();throw std::runtime_error("Shared audio snapshot unavailable");}
        token_=(static_cast<ULONGLONG>(GetCurrentProcessId())<<32)|GetCurrentThreadId();
        Renew(true);
    }
    ~SharedAudioCapture() {Close();}
    SharedAudioCapture(const SharedAudioCapture&)=delete;
    SharedAudioCapture& operator=(const SharedAudioCapture&)=delete;
    void Close() {
        if(block_&&lock_&&Lock(50)) {
            if(slot_>=0&&block_->leases[slot_].token==token_)block_->leases[slot_]={};
            if(owns_)block_->published=0;
            ReleaseMutex(lock_);
        }
        if(owns_&&owner_)ReleaseMutex(owner_);
        if(block_)UnmapViewOfFile(block_);
        if(mapping_)CloseHandle(mapping_);
        if(lock_)CloseHandle(lock_);
        if(owner_)CloseHandle(owner_);
        block_=nullptr;mapping_=lock_=owner_=nullptr;owns_=false;
    }
    void Renew(bool force=false) {
        const auto now=GetTickCount64();
        if(!force&&now-renewed_<250)return;
        if(!Lock())return;
        if(slot_<0||block_->leases[slot_].token!=token_) {
            slot_=-1;
            for(size_t i=0;i<block_->leases.size();++i)
                if(!block_->leases[i].token||now-block_->leases[i].seen>2000) {slot_=static_cast<int>(i);break;}
        }
        if(slot_>=0)block_->leases[slot_]={token_,now,spectrum_};
        else if(spectrum_)block_->overflowSpectrumUntil=now+2000;
        renewed_=now;ReleaseMutex(lock_);
    }
    bool TryOwn() {
        if(owns_)return true;
        DWORD result=WaitForSingleObject(owner_,0);
        owns_=result==WAIT_OBJECT_0||result==WAIT_ABANDONED;
        return owns_;
    }
    bool WantsSpectrum() {
        bool result=spectrum_;const auto now=GetTickCount64();
        if(Lock()) {
            result|=block_->overflowSpectrumUntil>now;
            for(const auto& lease:block_->leases)result|=lease.token&&lease.spectrum&&now-lease.seen<=2000;
            ReleaseMutex(lock_);
        }
        return result;
    }
    void Publish(const AudioAnalysisFrame& frame) {
        if(!Lock())return;
        block_->frame=frame;block_->published=GetTickCount64();ReleaseMutex(lock_);
    }
    bool Read(AudioAnalysisFrame& frame) {
        if(!Lock())return false;
        bool fresh=block_->published&&GetTickCount64()-block_->published<1500;
        frame=fresh?block_->frame:AudioAnalysisFrame{};
        ReleaseMutex(lock_);return true;
    }
};
