@echo off
cd /d "%~dp0.."
hrun -b -c DemoPassed --dump=HoleCount --dump=CorrectionX --dump=CorrectionY "halcon\programs\01_single_file_learning_demo.hdev"
echo.
if errorlevel 1 (
  echo HALCON demo failed.
) else (
  echo HALCON demo passed. Open halcon\programs\01_single_file_learning_demo.hdev in HDevelop.
)
pause

