@echo off
setlocal
cd /d "%~dp0.."
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
if defined LUMAWALL_VSROOT set "VSROOT=%LUMAWALL_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if not "%errorlevel%"=="0" exit /b 1
if not exist artifacts\test-audio-rules mkdir artifacts\test-audio-rules
cl /nologo /std:c++20 /EHsc /O2 /MT /utf-8 /W4 /DNOMINMAX tests\AudioRuleEngineTests.cpp /Foartifacts\test-audio-rules\ /Feartifacts\test-audio-rules\AudioRuleEngineTests.exe
if not "%errorlevel%"=="0" exit /b 1
artifacts\test-audio-rules\AudioRuleEngineTests.exe "%~1"
exit /b %errorlevel%
