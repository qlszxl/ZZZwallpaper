@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
if defined LUMAWALL_VSROOT set "VSROOT=%LUMAWALL_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
if not exist artifacts\presentation-tests mkdir artifacts\presentation-tests
cl /nologo /std:c++20 /EHsc /O2 /MT /utf-8 /DNOMINMAX tests\PresentationTests.cpp /Foartifacts\presentation-tests\PresentationTests.obj /Feartifacts\presentation-tests\PresentationTests.exe
if errorlevel 1 exit /b 1
artifacts\presentation-tests\PresentationTests.exe
exit /b %errorlevel%
