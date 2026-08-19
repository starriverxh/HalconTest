using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Services;

public sealed class DemoEngineeringWorkbenchGateway : IEngineeringWorkbenchGateway
{
    public bool IsOffline => true;

    public async Task<ProductionResult> RunOfflineCycleAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        return new ProductionResult(
            $"SIM-{DateTime.UtcNow:HHmmssfff}",
            "ENGINEERING_ONLY",
            0.018,
            -0.012,
            0.006,
            0.982,
            43,
            "MOTION_DISABLED_OFFLINE",
            RuntimeConnected: false,
            MotionAllowed: false,
            SourceDescription: "教学内存Gateway · 合成数值 · 不写Runtime结果",
            StateHistory: ["Idle", "ValidatingRequest", "TeachingSimulation", "EngineeringOnly"]);
    }

    public async Task<TemplateValidationResult> ValidateTemplateAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(220, cancellationToken).ConfigureAwait(false);
        return new TemplateValidationResult(120, 120, 0, 1, 0.842, 0.531, 38);
    }

    public async Task<CalibrationFitResult> FitCalibrationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(160, cancellationToken).ConfigureAwait(false);
        var points = new List<CalibrationPointResult>();
        var index = 1;
        foreach (var row in new[] { 100d, 300d, 500d })
        {
            foreach (var column in new[] { 140d, 500d, 860d })
            {
                var error = 0.002 + index * 0.0006;
                points.Add(new CalibrationPointResult(
                    $"P{index}", row, column,
                    column * 0.02 - 10,
                    row * 0.02 - 6,
                    error,
                    false));
                index++;
            }
        }

        points.Add(new CalibrationPointResult("V1", 200, 320, -3.6, -2, 0.011, true));
        points.Add(new CalibrationPointResult("V2", 420, 680, 3.6, 2.4, 0.014, true));
        return new CalibrationFitResult(0.0064, 0.0091, 0.014, points);
    }

    public async Task<RegressionResult> RunRegressionAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(260, cancellationToken).ConfigureAwait(false);
        return new RegressionResult(360, 358, 0, 2, 0.016, 57, "REG-PCB-PIN-V1-0042");
    }

    public IReadOnlyList<TraceRecord> GetTraceRecords() =>
    [
        new("SIM-082001", DateTimeOffset.Now.AddMinutes(-2), "PCB-A00031", "OK", "pcb-pin-v2.3", "-", 46),
        new("SIM-081944", DateTimeOffset.Now.AddMinutes(-3), "PCB-A00030", "NG", "pcb-pin-v2.3", "PIN_PITCH_OUT_OF_TOLERANCE", 51),
        new("SIM-081907", DateTimeOffset.Now.AddMinutes(-4), "PCB-A00029", "OK", "pcb-pin-v2.3", "-", 44),
        new("SIM-081832", DateTimeOffset.Now.AddMinutes(-5), "PCB-A00028", "FAULT", "pcb-pin-v2.3", "ACQUISITION_TIMEOUT", 3000)
    ];

    public IReadOnlyList<DeviceHealth> GetDeviceHealth() =>
    [
        new("HALCON Runtime", "可用", "演示接口；正式运行由Runtime宿主报告版本和许可证", false),
        new("PCB下视相机", "未接入", "工程离线：File/Synthetic适配器", true),
        new("针排上视相机", "未接入", "未完成硬件选型和标定", true),
        new("光源控制器", "模拟", "CH1=62%，无频闪反馈", true),
        new("PLC/运动控制", "禁止", "未建立安全握手，任何运动命令均被拦截", true),
        new("结果存储", "演示", "内存数据；未连接生产数据库", false)
    ];
}
