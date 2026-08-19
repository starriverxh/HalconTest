@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."
if not defined HALCONROOT if exist "D:\Program Files\MVTec\HALCON-23.11-Progress" set "HALCONROOT=D:\Program Files\MVTec\HALCON-23.11-Progress"
if not defined HALCONARCH set "HALCONARCH=x64-win64"
set "PATH=%HALCONROOT%\bin\%HALCONARCH%;%PATH%"

netstat -ano -p TCP | findstr /R /C:":9200 .*LISTENING" >nul
if not errorlevel 1 goto :ports_busy
netstat -ano -p TCP | findstr /R /C:":9201 .*LISTENING" >nul
if not errorlevel 1 goto :ports_busy

echo 正在构建 Vision.Runtime V2...
dotnet build "v2\src\Vision.Runtime\Vision.Runtime.csproj" -c Release --disable-build-servers -m:1
if errorlevel 1 goto :failed
echo.
echo 服务启动后请保持本窗口打开。工程端口 9201，生产端口 9200 当前禁用检测。
dotnet "v2\src\Vision.Runtime\bin\Release\net9.0\Vision.Runtime.dll"
popd
exit /b %errorlevel%

:ports_busy
echo [FAIL] 端口 9200 或 9201 已被占用。请关闭旧的 Vision.Runtime 窗口后再启动。
echo 可在任务管理器中确认旧进程已结束，不要同时启动两个视觉服务。
popd
pause
exit /b 1

:failed
echo [FAIL] Runtime 构建失败。
popd
pause
exit /b 1
