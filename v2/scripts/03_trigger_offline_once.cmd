@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."
echo 请先保持 02_start_runtime.cmd 正在运行。
dotnet build "v2\src\Vision.Trigger.Client\Vision.Trigger.Client.csproj" -c Release --disable-build-servers -m:1
if errorlevel 1 goto :failed
dotnet "v2\src\Vision.Trigger.Client\bin\Release\net9.0\Vision.Trigger.Client.dll"
if errorlevel 1 goto :failed
popd
if /i "%~1"=="--no-pause" exit /b 0
pause
exit /b 0

:failed
echo [FAIL] 离线触发失败。请确认 Runtime 的 9201 端口正在运行。
popd
if /i "%~1"=="--no-pause" exit /b 1
pause
exit /b 1
