@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
if defined LUMAWALL_VSROOT set "VSROOT=%LUMAWALL_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if not "%errorlevel%"=="0" exit /b 1
if not exist artifacts\particle-tests mkdir artifacts\particle-tests
cl /nologo /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\ParticleTests\Native.cpp /Foartifacts\particle-tests\Native.obj /Feartifacts\particle-tests\Native.exe
if not "%errorlevel%"=="0" exit /b 1
artifacts\particle-tests\Native.exe artifacts\particle-tests\layers.wire artifacts\particle-tests\legacy.wire artifacts\particle-tests\legacy24.wire artifacts\particle-tests\parameters25.wire
if not "%errorlevel%"=="0" exit /b 1
cl /nologo /std:c++20 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX tests\ParticleTests\Rotation.cpp /Foartifacts\particle-tests\Rotation.obj /Feartifacts\particle-tests\Rotation.exe /link d3d11.lib d3dcompiler.lib
if not "%errorlevel%"=="0" exit /b 1
artifacts\particle-tests\Rotation.exe
exit /b %errorlevel%
