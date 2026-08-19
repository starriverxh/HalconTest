using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Features.Manual;

public sealed partial class ManualImagingViewModel : FeatureViewModel
{
    public ManualImagingViewModel()
        : base(
            "manual", "手动采图 / 相机", "手动采图与相机光源", "在无运动风险的维护模式中建立稳定成像，再把已验证参数写入草稿配方。",
            "视觉算法首先依赖稳定的图像。曝光、光圈、工作距离和光源不稳定时，继续调阈值只是在掩盖成像问题。",
            ["相机设备与序列号", "镜头、工作距离和光圈", "触发方式与光源时序", "OK/边界/NG实物"],
            ["相机参数档案", "亮度/对比度/清晰度/饱和比例", "重复采图统计", "代表性样本集"],
            ["open_framegrabber", "set_framegrabber_param", "grab_image_start", "grab_image_async", "intensity / deviation_image / sobel_amp"],
            [
                new("Exposure", "µs", "传感器积分时间", "1500", "过小会欠曝，过大会拖影或过曝"),
                new("Gain", "dB", "模拟/数字增益", "0.0", "增益提高同时放大噪声，不用于补救错误光源"),
                new("Light CH1", "%", "同轴或环形光亮度", "62", "应记录控制器型号和实际回读值"),
                new("Trigger delay", "µs", "触发到曝光/频闪的延迟", "120", "错误时序会形成黑帧或亮度抖动")
            ],
            ["直方图和过曝像素", "Sobel清晰度", "视野中心/四角灰度", "30次重复采图均值与标准差"],
            ["CAMERA_NOT_READY", "ACQUISITION_TIMEOUT", "FRAME_SEQUENCE_INVALID", "IMAGE_TOO_DARK", "IMAGE_SATURATED"],
            ["30帧无丢帧和乱序", "清晰度及亮度波动落在冻结门槛内", "中心与四角均满足分辨率要求", "相机/镜头/光源硬件指纹已记录"])
    {
        QualityMetrics = new ObservableCollection<string>(
        ["Mean gray: --", "Gray deviation: --", "Mean edge: --", "Saturated: --"]);
    }

    public ObservableCollection<string> QualityMetrics { get; }
    public IReadOnlyList<string> TriggerModes { get; } = ["Off（连续）", "Software", "Hardware Line1"];

    [ObservableProperty] private string _cameraState = "未接入 · File/Synthetic演示";
    [ObservableProperty] private string _selectedTriggerMode = "Software";
    [ObservableProperty] private double _exposureMicroseconds = 1500;
    [ObservableProperty] private double _gainDb;
    [ObservableProperty] private double _lightPercent = 62;
    [ObservableProperty] private double _triggerDelayMicroseconds = 120;
    [ObservableProperty] private string _frameId = "--";
    [ObservableProperty] private string _motionMessage = "PLC/轴未接入，运动命令在UI和Runtime两层禁止。";

    [RelayCommand]
    private void AcquireSingleFrame()
    {
        FrameId = $"OFFLINE-{DateTime.Now:HHmmssfff}";
        QualityMetrics.Clear();
        QualityMetrics.Add("Mean gray: 112.4");
        QualityMetrics.Add("Gray deviation: 31.8");
        QualityMetrics.Add("Mean edge: 8.7");
        QualityMetrics.Add("Saturated: 0.04%");
        CameraState = "离线帧已生成 · 未连接真实相机";
        StageStatus = "单帧质量演示完成";
    }

    [RelayCommand]
    private void AttemptMotion()
    {
        MotionMessage = $"{DateTime.Now:HH:mm:ss} 已拒绝：MOTION_DISABLED_OFFLINE；未发送任何运动命令。";
        StageStatus = "运动请求已安全拦截";
    }
}
