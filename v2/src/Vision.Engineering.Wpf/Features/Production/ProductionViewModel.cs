using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Production;

public sealed partial class ProductionViewModel : FeatureViewModel
{
    private readonly IEngineeringWorkbenchGateway _gateway;

    public ProductionViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "production", "生产工作台", "生产工作台", "只显示已批准工件和检测结果；工程离线时永远禁止运动输出。",
            "生产页的职责是稳定执行已经验证过的配方，而不是现场试参数。运行前必须证明设备、配方、模板、标定和安全互锁均有效。",
            ["PLC触发ID / 产品SN", "已激活配方、模板和标定版本", "PCB与针排相机帧", "设备安全就绪状态"],
            ["OK/NG/FAULT判定", "XYT补偿建议值", "持久化CycleId和完整追溯记录", "PLC结果应答"],
            ["grab_image_async", "intensity / deviation_image / sobel_amp", "find_shape_model", "affine_trans_point_2d", "vector_angle_to_rigid"],
            [
                new("X/Y补偿", "mm", "同一机械坐标系中的平移建议", "+0.018 / -0.012", "超过软件安全限值必须FAULT，不能只判产品NG"),
                new("T补偿", "deg", "绕已标定旋转中心的角度建议", "+0.006", "正负号必须通过实机点动验证"),
                new("Cycle timeout", "ms", "从接受触发到结果发布的上限", "5000", "超时应返回唯一故障码并完成对账")
            ],
            ["原始帧与FrameId", "图像质量统计", "粗定位位姿", "孔/针对应关系", "最终残差与各阶段耗时"],
            ["CAMERA_NOT_READY", "IMAGE_BLURRED", "PIN_MISSING", "ALIGNMENT_RESIDUAL_TOO_HIGH", "MOTION_DISABLED_OFFLINE"],
            ["活动配方不可编辑", "每次周期均有唯一CycleId且可追溯", "关键NG数据集零错误放行", "运动输出必须同时通过安全互锁和补偿范围复核"])
    {
        _gateway = gateway;
        CycleStates = new ObservableCollection<string>(["Idle · 等待工程离线触发"]);
    }

    public ObservableCollection<string> CycleStates { get; }

    [ObservableProperty] private string _cycleId = "--";
    [ObservableProperty] private string _disposition = "等待";
    [ObservableProperty] private string _correctionX = "--";
    [ObservableProperty] private string _correctionY = "--";
    [ObservableProperty] private string _correctionTheta = "--";
    [ObservableProperty] private string _score = "--";
    [ObservableProperty] private string _duration = "--";
    [ObservableProperty] private string _lastFailure = "工程离线：MotionAllowed=false";
    [ObservableProperty] private string _runtimeConnection = "等待点击后连接 Runtime 127.0.0.1:9201";
    [ObservableProperty] private string _sourceSummary = "尚未执行 · 不存在本周期结果文件";
    [ObservableProperty] private string _motionAuthority = "MotionAllowed=false · 禁止运动";
    [ObservableProperty] private int _okCount = 1284;
    [ObservableProperty] private int _ngCount = 17;
    [ObservableProperty] private bool _isRunning;

    public string Yield => $"{OkCount * 100.0 / Math.Max(1, OkCount + NgCount):F2}%";

    [RelayCommand(CanExecute = nameof(CanRunCycle))]
    private async Task RunOfflineCycleAsync()
    {
        IsRunning = true;
        RunOfflineCycleCommand.NotifyCanExecuteChanged();
        CycleStates.Clear();
        foreach (var state in new[] { "ValidatingRequest", "CheckingInterlocks", "AcquiringBoardImage", "LocatingBoard", "InspectingPins", "CalculatingCorrection" })
        {
            CycleStates.Add($"{DateTime.Now:HH:mm:ss.fff} · {state}");
        }
        try
        {
            var result = await _gateway.RunOfflineCycleAsync(CancellationToken.None);
            CycleId = result.CycleId;
            Disposition = result.Disposition;
            CorrectionX = $"{result.Xmm:+0.000;-0.000;0.000} mm";
            CorrectionY = $"{result.Ymm:+0.000;-0.000;0.000} mm";
            CorrectionTheta = $"{result.ThetaDeg:+0.000;-0.000;0.000}°";
            Score = result.Score.ToString("F3");
            Duration = $"{result.DurationMs} ms";
            LastFailure = result.FailureCode;
            RuntimeConnection = result.RuntimeConnected
                ? "Runtime 127.0.0.1:9201 已连接并完成ACK"
                : result.FailureCode.StartsWith("RUNTIME_", StringComparison.Ordinal)
                    ? "Runtime 127.0.0.1:9201 不可用"
                    : "当前使用教学内存Gateway，未连接Runtime";
            SourceSummary = result.SourceDescription;
            MotionAuthority = result.MotionAllowed
                ? "危险：Runtime返回MotionAllowed=true"
                : "MotionAllowed=false · Runtime/工程站均禁止运动";
            if (result.StateHistory is { Count: > 0 })
            {
                CycleStates.Clear();
                foreach (var state in result.StateHistory)
                {
                    CycleStates.Add(state);
                }
            }
            else
            {
                CycleStates.Add(result.RuntimeConnected ? "RuntimeResult · 已完成并ACK" : "未产生Runtime周期记录");
            }
            StageStatus = result.RuntimeConnected
                ? "真实Socket→Runtime→HALCON离线周期完成；运动输出已拦截"
                : result.Disposition == "FAULT"
                    ? "Runtime不可用；没有执行算法，也没有写入结果"
                    : "教学内存周期完成；没有调用Runtime或写入生产工件";
        }
        finally
        {
            IsRunning = false;
            RunOfflineCycleCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunCycle() => !IsRunning;

    partial void OnOkCountChanged(int value) => OnPropertyChanged(nameof(Yield));
    partial void OnNgCountChanged(int value) => OnPropertyChanged(nameof(Yield));
}
