#pragma once
#include "HostOptions.h"
#include "HostProtocol.h"
#include "Renderer.h"
#include "../../Shared/MediaCore/MediaSession.h"

void PrintHostCapabilities();
void DiagnoseDesktop();
bool RunMediaTool(const HostOptions& options,MediaSession& media,VideoFrame& frame,HostProtocol& protocol);
void RunImagePreview(const std::wstring& imagePreview,Renderer& renderer,VideoFrame& frame,HostProtocol& protocol);
