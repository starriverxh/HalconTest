@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0\..\.."

if not defined HALCONROOT if exist "D:\Program Files\MVTec\HALCON-23.11-Progress" set "HALCONROOT=D:\Program Files\MVTec\HALCON-23.11-Progress"
if not defined HALCONARCH set "HALCONARCH=x64-win64"
if not exist "%HALCONROOT%\bin\dotnet35\halcondotnet.dll" goto :halcon_missing
set "PATH=%HALCONROOT%\bin\%HALCONARCH%;%PATH%"

echo [1/9] 构建 V2 完整解决方案...
dotnet build "v2\HalconPinInsertion.V2.sln" -c Release --disable-build-servers -m:1
if errorlevel 1 goto :failed

echo [2/9] Domain 测试...
dotnet "v2\tests\Vision.Domain.Tests\bin\Release\net9.0\VisionSystem.Domain.Tests.dll"
if errorlevel 1 goto :failed

echo [3/9] Application 测试...
dotnet "v2\tests\Vision.Application.Tests\bin\Release\net9.0\VisionSystem.Application.Tests.dll"
if errorlevel 1 goto :failed

echo [4/9] Architecture 测试...
dotnet "v2\tests\Vision.Architecture.Tests\bin\Release\net9.0\VisionSystem.Architecture.Tests.dll"
if errorlevel 1 goto :failed

echo [5/9] Runtime 测试...
dotnet "v2\tests\Vision.Runtime.Tests\bin\Release\net9.0\VisionSystem.Runtime.Tests.dll"
if errorlevel 1 goto :failed

echo [6/9] Infrastructure 测试...
dotnet "v2\tests\Vision.Infrastructure.Tests\bin\Release\net9.0\VisionSystem.Infrastructure.Tests.dll"
if errorlevel 1 goto :failed

echo [7/9] WPF 工程站测试...
dotnet "v2\tests\Vision.Engineering.Wpf.Tests\bin\Release\net9.0-windows\VisionSystem.Engineering.Wpf.Tests.dll"
if errorlevel 1 goto :failed

echo [8/9] HALCON C# 离线 Smoke...
dotnet "v2\src\Vision.Halcon\Smoke\bin\Release\net9.0\Vision.Halcon.Smoke.dll"
if errorlevel 1 goto :failed

echo [9/9] 数据集哈希 + HDevelop 01..09 语法/执行检查...
dotnet "v2\src\Vision.Dataset.Cli\bin\Release\net9.0\Vision.Dataset.Cli.dll" verify
if errorlevel 1 goto :failed
call :check_hdevelop "SyntheticGenerationPassed" "v2\hdevelop\01_generate_synthetic_pair.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "ImageQualityPassed" "v2\hdevelop\02_image_quality_gate.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "CoarsePassed" "v2\hdevelop\03_board_coarse_and_follow_roi.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "SubpixelCirclePassed" "v2\hdevelop\04_hole_subpixel_circle.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "PinInspectionPassed" "v2\hdevelop\05_pin_tip_inspection.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "NinePointMappingPassed" "v2\hdevelop\06_nine_point_mapping.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "RotationCenterPassed" "v2\hdevelop\07_t_axis_rotation_center.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "AlignmentPassed" "v2\hdevelop\08_coordinate_chain_alignment.hdev"
if errorlevel 1 goto :failed
call :check_hdevelop "CompleteCyclePassed" "v2\hdevelop\09_complete_offline_cycle.hdev"
if errorlevel 1 goto :failed

echo.
echo ============================================================
echo V2 全部验证通过。
echo 当前级别：Engineering Offline
echo MotionAllowed：false（没有真实双相机、标定、PLC与运动验收）
echo ============================================================
popd
if /i "%~1"=="--no-pause" exit /b 0
pause
exit /b 0

:check_hdevelop
echo   HALCON %~nx2 ^(check: %~1^)
"%HALCONROOT%\bin\%HALCONARCH%\hrun.exe" -b -P -c "%~1" "%~f2"
if errorlevel 1 exit /b 1
exit /b 0

:halcon_missing
echo [FAIL] 未找到 halcondotnet.dll。请安装 HALCON 23.11 x64 或设置 HALCONROOT。
goto :failed_end

:failed
echo.
echo [FAIL] V2 验证失败，请查看上方第一个错误。

:failed_end
popd
if /i "%~1"=="--no-pause" exit /b 1
pause
exit /b 1
