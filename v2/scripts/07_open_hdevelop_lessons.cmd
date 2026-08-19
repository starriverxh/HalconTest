@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."
if not defined HALCONROOT if exist "D:\Program Files\MVTec\HALCON-23.11-Progress" set "HALCONROOT=D:\Program Files\MVTec\HALCON-23.11-Progress"
if not defined HALCONARCH set "HALCONARCH=x64-win64"
if not exist "%HALCONROOT%\bin\%HALCONARCH%\hdevelop.exe" goto :failed
start "HDevelop V2 Lessons" "%HALCONROOT%\bin\%HALCONARCH%\hdevelop.exe" "v2\hdevelop\01_generate_synthetic_pair.hdev"
popd
exit /b 0

:failed
echo [FAIL] 找不到 HDevelop，请设置 HALCONROOT。
popd
pause
exit /b 1
