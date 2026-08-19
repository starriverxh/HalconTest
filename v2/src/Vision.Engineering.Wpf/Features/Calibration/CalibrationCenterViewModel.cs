using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Calibration;

public sealed partial class CalibrationCenterViewModel : FeatureViewModel
{
    private readonly IEngineeringWorkbenchGateway _gateway;

    public CalibrationCenterViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "calibration", "标定中心", "标定中心", "将相机像素、机械XY和T轴旋转中心建立成可验证、可失效的坐标链。",
            "标定不是只求出六个数字。必须先补偿镜头畸变，再拟合平面映射，并用未参与拟合的点验证；相机、镜头、工作距离变化后标定必须失效。",
            ["标定板图像/特征点", "同步机械X/Y/T位置", "相机和镜头硬件指纹", "环境与Z高度"],
            ["内参和畸变模型", "camera→machine HomMat2D", "T轴旋转中心/回差模型", "残差报告与有效期"],
            ["find_calib_object", "calibrate_cameras", "vector_to_hom_mat2d", "affine_trans_point_2d", "fit_circle_contour_xld"],
            [
                new("Reprojection error", "px", "内参重投影误差", "≤ 0.20", "仅代表成像模型拟合，不等于机械毫米误差"),
                new("Plane RMSE", "mm", "拟合点均方根误差", "≤ 0.010", "必须同时看最大误差和空间分布"),
                new("Validation max", "mm", "独立验证点最大误差", "≤ 0.020", "验证点不得参与拟合"),
                new("Maximum age", "day", "标定有效期", "30", "还应由硬件指纹或维修事件立即失效")
            ],
            ["标定板覆盖图", "每点残差箭头", "拟合/验证误差热图", "正反向T轴圆拟合", "随机位置复测"],
            ["CALIBRATION_MISSING", "CALIBRATION_EXPIRED", "CALIBRATION_POINT_DEGENERATE", "CALIBRATION_RESIDUAL_TOO_HIGH", "HARDWARE_FINGERPRINT_CHANGED"],
            ["九点覆盖有效视野≥80%", "Plane RMSE≤0.010 mm", "独立验证最大误差≤0.020 mm", "重复/共线点被拒绝", "硬件变化自动使标定失效"])
    {
        _gateway = gateway;
        CalibrationKinds = ["1 相机内参与畸变", "2 平面九点 camera→machine", "3 T轴旋转中心与回差", "4 独立随机点复核"];
        SelectedCalibrationKind = CalibrationKinds[1];
        Points = new ObservableCollection<CalibrationPointResult>();
    }

    public IReadOnlyList<string> CalibrationKinds { get; }
    public ObservableCollection<CalibrationPointResult> Points { get; }

    [ObservableProperty] private string _selectedCalibrationKind;
    [ObservableProperty] private string _calibrationVersion = "未批准 · Engineering";
    [ObservableProperty] private string _hardwareFingerprint = "PCB-CAM:OFFLINE | LENS:UNKNOWN | WD:200mm(名义)";
    [ObservableProperty] private string _fitSummary = "等待同步采点；演示拟合不会授权真实运动。";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanFit))]
    private async Task FitOfflinePointsAsync()
    {
        IsBusy = true;
        FitOfflinePointsCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _gateway.FitCalibrationAsync(CancellationToken.None);
            Points.Clear();
            foreach (var point in result.Points)
            {
                Points.Add(point);
            }
            FitSummary = $"RMSE={result.RmseMm:F4} mm；最大拟合={result.MaximumFitErrorMm:F4} mm；独立验证最大={result.MaximumValidationErrorMm:F4} mm。";
            CalibrationVersion = "Validated Simulation · 禁止生产激活";
            StageStatus = "九点教学拟合完成；未读PLC/相机、未写标定JSON，不能发布";
        }
        finally
        {
            IsBusy = false;
            FitOfflinePointsCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void RunTAxisTeaching()
    {
        SelectedCalibrationKind = CalibrationKinds[2];
        Points.Clear();
        FitSummary =
            "T轴教学拟合：角度 -15°/-7.5°/0°/+7.5°/+15°；示例圆心=(125.430, 82.170) mm；" +
            "正反向最大回差=0.012 mm。数据由WPF教学模型生成，未读取轴反馈。";
        CalibrationVersion = "T-Axis Teaching Simulation · 未写标定文件";
        StageStatus = "T轴中心教学计算完成；真实PLC/相机证据缺失";
    }

    [RelayCommand]
    private void AttemptAutomaticTeach()
    {
        StageStatus = "已拒绝自动走点：MOTION_DISABLED_OFFLINE";
        FitSummary = "真实九点示教要求PLC安全握手、轴停止确认、同步位置读取和相机帧ID。当前未接硬件。";
    }

    private bool CanFit() => !IsBusy;
}
