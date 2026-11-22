#pragma once
#include <Windows.h>
#include <audioclient.h>
#include <audiopolicy.h>
#include <wrl/client.h>
#include <string>

// Mixers can read either session metadata or the audio process executable.
// Both audio hosts embed resource 101, so the fallback and explicit icon agree.
inline void SetAudioSessionIdentity(IAudioClient* client) {
    Microsoft::WRL::ComPtr<IAudioSessionControl> session;
    if(FAILED(client->GetService(IID_PPV_ARGS(&session))))return;
    session->SetDisplayName(L"ZZZwallpaper",nullptr);
    wchar_t module[32768];const DWORD length=GetModuleFileNameW(nullptr,module,32768);
    if(length>0&&length<32768) {
        const std::wstring icon=std::wstring(module,length)+L",-101";
        session->SetIconPath(icon.c_str(),nullptr);
    }
}
