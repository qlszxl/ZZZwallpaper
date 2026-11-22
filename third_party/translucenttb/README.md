# TranslucentTB taskbar support

Only `ExplorerTAP.dll` from the unmodified, official TranslucentTB **2026.2**, x64 portable release is used. No TranslucentTB application or background service is installed. The helper loads the DLL only for the Windows 11 XAML taskbar when the user enables transparent or frosted taskbar appearance. It requests automatic appearance restoration on helper exit. The versioned local cache avoids locking the application's install directory in Explorer.

- Release: https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2
- Binary SHA-256: `c48f1a06168acd7839269dde0d15ccf5d09c702512b94dd6ba9a05df1b153dc2`
- GPL-3.0: original `LICENSE.md`; complete corresponding release source: `TranslucentTB-2026.2-source.zip`.
- Statically linked/header dependencies: Microsoft Detours `4b8c659f549b0ab21cf649377c7a84eb708f5e68`, WIL `v1.0.250325.1`, C++/WinRT `2.0.250303.1`. Their source archives and MIT licenses are included alongside this file.
- Detours uses the release's overlay port; WIL uses vcpkg baseline `84bab45d415d22042bd0b9081aea57f362da3f35`. `files.json` records hashes of the included originals.

Build the upstream `ExplorerTAP/ExplorerTAP.vcxproj` in Release/x64 using Visual Studio 2022 C++ tools, the source archive's Common property sheets, vcpkg manifest and NuGet `ExplorerTAP/packages.config`. Those files specify Windows SDK 10.0.26100.7463 and C++/WinRT 2.0.250303.1. Follow the upstream README build instructions for vcpkg integration and NuGet restoration. ZZZwallpaper does not modify, rebuild or re-sign the distributed DLL. SourceLink, signing tools, Visual Studio and Windows SDK are build/system tools, not additional shipped DLLs.

This directory is copied to `licenses/TranslucentTB` in the installer, excluding the DLL; the DLL is at `TaskbarSupport/ExplorerTAP.dll`.
