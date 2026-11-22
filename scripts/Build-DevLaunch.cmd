@echo off
setlocal
cd /d "%~dp0.."
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /O2 /MT /utf-8 scripts\DevLaunch.cpp /Foartifacts\native\DevLaunch.obj /Feartifacts\DevLaunch.exe
