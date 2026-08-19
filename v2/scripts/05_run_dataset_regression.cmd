@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."
echo 请先保持 02_start_runtime.cmd 正在运行。
dotnet build "v2\src\Vision.Dataset.Cli\Vision.Dataset.Cli.csproj" -c Release --disable-build-servers -m:1
if errorlevel 1 goto :failed
dotnet "v2\src\Vision.Dataset.Cli\bin\Release\net9.0\Vision.Dataset.Cli.dll" run
if errorlevel 1 goto :failed
popd
if /i "%~1"=="--no-pause" exit /b 0
pause
exit /b 0

:failed
echo [FAIL] 数据集回归失败。请查看第一个 FAIL，并确认工程端口 9201 可用。
popd
if /i "%~1"=="--no-pause" exit /b 1
pause
exit /b 1
