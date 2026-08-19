@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."
echo 正在构建模块化 WPF 工程站...
dotnet build "v2\src\Vision.Engineering.Wpf\Vision.Engineering.Wpf.csproj" -c Release --disable-build-servers -m:1
if errorlevel 1 goto :failed
start "Vision Engineering V2" "v2\src\Vision.Engineering.Wpf\bin\Release\net9.0-windows\VisionSystem.Engineering.Wpf.exe"
popd
exit /b 0

:failed
echo [FAIL] WPF 工程站构建失败。
popd
pause
exit /b 1
