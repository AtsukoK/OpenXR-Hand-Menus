@echo off
call "G:\Claude\Tools\VSBuildTools\Common7\Tools\VsDevCmd.bat" -arch=x64 >nul
set CMAKE="G:\Claude\Tools\VSBuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
%CMAKE% -S "%~dp0." -B "%~dp0build" -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release >nul || exit /b 1
%CMAKE% --build "%~dp0build" 2>&1
