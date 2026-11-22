@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
if defined LUMAWALL_VSROOT set "VSROOT=%LUMAWALL_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
call :SdkFallback
if errorlevel 1 exit /b 1
if not exist artifacts\playback-tests mkdir artifacts\playback-tests
rem This entry point only builds. Shader compilation uses no windows or D3D device.
rem Keep its generated files independent of the main application build.
cl /nologo %LUMAWALL_CPP_INCLUDES% /std:c++20 /EHsc /O2 /MT /utf-8 scripts\Compile-Shaders.cpp /Foartifacts\playback-tests\Compile-Shaders.obj /Feartifacts\playback-tests\Compile-Shaders.exe /link %LUMAWALL_LINK_LIBS% d3dcompiler.lib
if errorlevel 1 exit /b 1
artifacts\playback-tests\Compile-Shaders.exe artifacts\playback-tests\PostProcess.generated.h
if errorlevel 1 exit /b 1
"%LUMAWALL_SDK_ROOT%\bin\%LUMAWALL_SDK_VERSION%\x64\rc.exe" /nologo %LUMAWALL_CPP_INCLUDES% /fo artifacts\playback-tests\PlaybackTransitions.res tests\PlaybackTransitions.rc
if errorlevel 1 exit /b 1
cl /nologo %LUMAWALL_CPP_INCLUDES% /external:I third_party\ffmpeg\include /external:W0 /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX /I artifacts\playback-tests tests\PlaybackTransitions.cpp src\Windows\WallpaperHost\Renderer.cpp src\Windows\WallpaperHost\Renderer.Masks.cpp src\Shared\MediaCore\MediaSession.cpp src\Shared\MediaCore\MediaUtilities.cpp /Foartifacts\playback-tests\ /Feartifacts\playback-tests\PlaybackTransitions.exe /link %LUMAWALL_LINK_LIBS% /LIBPATH:third_party\ffmpeg\lib avformat.lib avcodec.lib avutil.lib swscale.lib avfilter.lib artifacts\playback-tests\PlaybackTransitions.res user32.lib d3d11.lib dxgi.lib dwmapi.lib /MANIFEST:NO
if errorlevel 1 exit /b 1
copy /y third_party\ffmpeg\bin\*.dll artifacts\playback-tests\ >nul
exit /b %errorlevel%

:SdkFallback
rem Match the native build's process-local SDK fallback. Explicit include/library
rem switches also work when tool subprocesses do not inherit INCLUDE and LIB.
set "LUMAWALL_SDK_ROOT=%ProgramFiles(x86)%\Windows Kits\10"
for /f "delims=" %%V in ('dir /b /ad /o-n "%LUMAWALL_SDK_ROOT%\Include"') do if not defined LUMAWALL_SDK_VERSION set "LUMAWALL_SDK_VERSION=%%V"
for /f "delims=" %%V in ('dir /b /ad /o-n "%VSROOT%\VC\Tools\MSVC"') do if not defined LUMAWALL_VC_VERSION set "LUMAWALL_VC_VERSION=%%V"
set "LUMAWALL_VC_TOOLS=%VSROOT%\VC\Tools\MSVC\%LUMAWALL_VC_VERSION%"
if not exist "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um\Windows.h" exit /b 1
if not exist "%LUMAWALL_SDK_ROOT%\bin\%LUMAWALL_SDK_VERSION%\x64\rc.exe" exit /b 1
set "INCLUDE=%LUMAWALL_VC_TOOLS%\include;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\ucrt;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\shared;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um;%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\winrt"
set "LIB=%LUMAWALL_VC_TOOLS%\lib\x64;%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\ucrt\x64;%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\um\x64"
set "PATH=%LUMAWALL_VC_TOOLS%\bin\Hostx64\x64;%LUMAWALL_SDK_ROOT%\bin\%LUMAWALL_SDK_VERSION%\x64;%PATH%"
set LUMAWALL_CPP_INCLUDES=/I "%LUMAWALL_VC_TOOLS%\include" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\ucrt" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\shared" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\um" /I "%LUMAWALL_SDK_ROOT%\Include\%LUMAWALL_SDK_VERSION%\winrt"
set LUMAWALL_LINK_LIBS=/LIBPATH:"%LUMAWALL_VC_TOOLS%\lib\x64" /LIBPATH:"%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\ucrt\x64" /LIBPATH:"%LUMAWALL_SDK_ROOT%\Lib\%LUMAWALL_SDK_VERSION%\um\x64"
exit /b 0
