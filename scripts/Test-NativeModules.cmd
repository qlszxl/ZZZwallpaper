@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
set "LUMAWALL_SDK_ROOT=%ProgramFiles(x86)%\Windows Kits\10"
for /f "delims=" %%V in ('dir /b /ad /o-n "%LUMAWALL_SDK_ROOT%\Include"') do if not defined LUMAWALL_SDK_VERSION set "LUMAWALL_SDK_VERSION=%%V"
for /f "delims=" %%V in ('dir /b /ad /o-n "%VSROOT%\VC\Tools\MSVC"') do if not defined LUMAWALL_VC_VERSION set "LUMAWALL_VC_VERSION=%%V"
set "LUMAWALL_VC_TOOLS=%VSROOT%\VC\Tools\MSVC\%LUMAWALL_VC_VERSION%"
if not exist "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um\Windows.h" exit /b 1
set "INCLUDE=%LUMAWALL_VC_TOOLS%\include;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\ucrt;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\shared;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\winrt"
set "LIB=%LUMAWALL_VC_TOOLS%\lib\x64;%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\ucrt\x64;%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\um\x64"
set "PATH=%LUMAWALL_VC_TOOLS%\bin\Hostx64\x64;%LUMAWALL_SDK_ROOT%\bin\%LUMAWALL_SDK_VERSION%\x64;%PATH%"
set LUMAWALL_CPP_INCLUDES=/I "%LUMAWALL_VC_TOOLS%\include" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\ucrt" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\shared" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\winrt"
set LUMAWALL_LINK_LIBS=/LIBPATH:"%LUMAWALL_VC_TOOLS%\lib\x64" /LIBPATH:"%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\ucrt\x64" /LIBPATH:"%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\um\x64"

if not exist artifacts\module-tests mkdir artifacts\module-tests
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\HostProtocolTests.cpp src\Windows\WallpaperHost\HostOptions.cpp src\Windows\WallpaperHost\HostProtocol.cpp src\Shared\MediaCore\MediaUtilities.cpp /Foartifacts\module-tests\ /Feartifacts\module-tests\HostProtocolTests.exe /link %LUMAWALL_LINK_LIBS%
if not "%errorlevel%"=="0" exit /b 1
artifacts\module-tests\HostProtocolTests.exe
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\PresentationTests.cpp /Foartifacts\module-tests\ /Feartifacts\module-tests\PresentationTests.exe /link %LUMAWALL_LINK_LIBS%
if not "%errorlevel%"=="0" exit /b 1
artifacts\module-tests\PresentationTests.exe
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\FramePacerSpectrumTests.cpp src\Windows\WallpaperHost\AudioAnalysis.cpp /Foartifacts\module-tests\ /Feartifacts\module-tests\FramePacerSpectrumTests.exe /link %LUMAWALL_LINK_LIBS%
if not "%errorlevel%"=="0" exit /b 1
artifacts\module-tests\FramePacerSpectrumTests.exe
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\EffectStackPlanTests.cpp /Foartifacts\module-tests\ /Feartifacts\module-tests\EffectStackPlanTests.exe /link %LUMAWALL_LINK_LIBS%
if not "%errorlevel%"=="0" exit /b 1
artifacts\module-tests\EffectStackPlanTests.exe
if not "%errorlevel%"=="0" exit /b 1
exit /b 0
