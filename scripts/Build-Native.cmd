@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
if defined LUMAWALL_VSROOT set "VSROOT=%LUMAWALL_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if not "%errorlevel%"=="0" exit /b 1
rem Registry discovery can be unavailable in a restricted shell. Keep the
rem fallback local to this build process and use the installed SDK/toolset.
call :SdkFallback
if not "%errorlevel%"=="0" exit /b 1
if not exist artifacts\native mkdir artifacts\native
if not exist artifacts\build-tools mkdir artifacts\build-tools
if not exist artifacts\generated mkdir artifacts\generated
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 scripts\Compile-Shaders.cpp /Foartifacts\build-tools\Compile-Shaders.obj /Feartifacts\build-tools\Compile-Shaders.exe /link %LUMAWALL_LINK_LIBS% d3dcompiler.lib
if not "%errorlevel%"=="0" exit /b 1
artifacts\build-tools\Compile-Shaders.exe artifacts\generated\PostProcess.generated.h
if not "%errorlevel%"=="0" exit /b 1
rc /nologo %LUMAWALL_CPP_INCLUDES% /fo artifacts\native\WallpaperHost.res src\Windows\WallpaperHost\AudioHost.rc
if not "%errorlevel%"=="0" exit /b 1
if not exist artifacts\native\ExpansionPacks\base mkdir artifacts\native\ExpansionPacks\base
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /LD /W4 /DUNICODE /D_UNICODE /DNOMINMAX /I artifacts\generated /external:I third_party\ffmpeg\include /external:W0 src\Windows\WallpaperHost\BasePlayback.cpp src\Windows\WallpaperHost\HostOptions.cpp src\Windows\WallpaperHost\HostProtocol.cpp src\Windows\WallpaperHost\DesktopSurface.cpp src\Windows\WallpaperHost\PlaybackDiagnostics.cpp src\Windows\WallpaperHost\HostTools.cpp src\Windows\WallpaperHost\PlaybackSession.cpp src\Windows\WallpaperHost\Renderer.cpp src\Windows\WallpaperHost\Renderer.Masks.cpp src\Windows\WallpaperHost\AudioAnalysis.cpp src\Windows\WallpaperHost\AudioCapture.cpp src\Windows\WallpaperHost\VideoAudioReader.cpp src\Windows\WallpaperHost\VideoAudioOutput.cpp src\Windows\WallpaperHost\AudioReactive.cpp src\Shared\MediaCore\MediaSession.cpp src\Shared\MediaCore\MediaUtilities.cpp src\Shared\MediaCore\PosterWriter.cpp /Foartifacts\native\ /Feartifacts\native\ExpansionPacks\base\BasePlayback.dll /link %LUMAWALL_LINK_LIBS% artifacts\native\WallpaperHost.res /LIBPATH:third_party\ffmpeg\lib avformat.lib avcodec.lib avutil.lib swscale.lib swresample.lib avfilter.lib user32.lib shell32.lib ole32.lib avrt.lib d3d11.lib dxgi.lib dwmapi.lib /SUBSYSTEM:CONSOLE /MANIFEST:NO
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE src\Windows\WallpaperHost\main.cpp /Foartifacts\native\main.obj /Feartifacts\native\WallpaperHost.exe /link %LUMAWALL_LINK_LIBS% artifacts\native\WallpaperHost.res
if not "%errorlevel%"=="0" exit /b 1
rc /nologo %LUMAWALL_CPP_INCLUDES% /fo artifacts\native\Launcher.res src\Windows\WallpaperHost\Launcher.rc
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 src\Windows\WallpaperHost\Launcher.cpp /Foartifacts\native\Launcher.obj /Feartifacts\native\ZZZwallpaper.Launcher.exe /link %LUMAWALL_LINK_LIBS% artifacts\native\Launcher.res user32.lib /SUBSYSTEM:WINDOWS
if not "%errorlevel%"=="0" exit /b 1
rc /nologo %LUMAWALL_CPP_INCLUDES% /d AUDIO_SPECTRUM_HOST /fo artifacts\native\AudioSpectrumHost.res src\Windows\WallpaperHost\AudioHost.rc
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /W4 /DUNICODE /D_UNICODE /DNOMINMAX src\Windows\WallpaperHost\SpectrumHost.cpp src\Windows\WallpaperHost\AudioCapture.cpp src\Windows\WallpaperHost\AudioAnalysis.cpp /Foartifacts\native\ /Feartifacts\native\AudioSpectrumHost.exe /link %LUMAWALL_LINK_LIBS% artifacts\native\AudioSpectrumHost.res ole32.lib /SUBSYSTEM:CONSOLE
if not "%errorlevel%"=="0" exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 /W4 /DUNICODE /D_UNICODE /DNOMINMAX src\Windows\WallpaperHost\TaskbarHost.cpp /Foartifacts\native\TaskbarHost.obj /Feartifacts\native\TaskbarHost.exe /link %LUMAWALL_LINK_LIBS% user32.lib dwmapi.lib /SUBSYSTEM:CONSOLE
if not "%errorlevel%"=="0" exit /b 1
copy /y third_party\ffmpeg\bin\*.dll artifacts\native\ >nul
exit /b 0

:SdkFallback
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
exit /b 0
