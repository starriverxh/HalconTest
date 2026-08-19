using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Maintenance;

public sealed record AlarmRow(string Severity, string Code, string Source, string State, string Recovery);

public sealed partial class SystemMaintenanceViewModel : FeatureViewModel
{
    public SystemMaintenanceViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "maintenance", "系统维护", "系统维护、报警与审计", "区分产品NG和设备故障，提供可操作恢复步骤，并验证服务、许可证、设备、存储和安全状态。",
            "工业系统不能只显示Exception.Message。报警需要稳定代码、级别、来源、确认人、清除条件和恢复SOP；参数、标定和发布变更要进入不可篡改审计。",
            ["Runtime/设备健康状态", "结构化事件和报警", "磁盘/内存/句柄指标", "用户角色和审计记录"],
            ["活动报警与恢复步骤", "健康快照", "备份/恢复证据", "权限和变更审计"],
            ["本页主要调用运行时诊断接口；HALCON侧检查版本、许可证、句柄和采集接口状态"],
            [
                new("Disk free", "% / GB", "图像和日志存储余量", ">20%", "到预警线降低保存策略，到停机线禁止接受新周期"),
                new("Alarm severity", "enum", "Info/Warning/Stop/Safety", "Stop", "决定是否阻断触发和所需复位权限"),
                new("Heartbeat", "ms", "服务/PLC/设备健康周期", "500", "失联必须有去抖、超时和安全降级")
            ],
            ["HALCON版本与License", "相机/光源/PLC状态", "进程内存和HALCON句柄", "存储写入探针", "最近审计事件"],
            ["LICENSE_NOT_AVAILABLE", "CAMERA_DISCONNECTED", "PLC_HEARTBEAT_LOST", "STORAGE_LOW", "PERSISTENCE_FAILED"],
            ["每个报警有触发/确认/清除测试", "阻断报警存在时拒绝生产触发", "权限在UI和Runtime同时执行", "备份可在隔离环境完成恢复演练"])
    {
        Devices = new ObservableCollection<DeviceHealth>(gateway.GetDeviceHealth());
        Alarms = new ObservableCollection<AlarmRow>(
        [
            new("STOP", "MOTION_DISABLED_OFFLINE", "PLC/运动", "Raised", "接入PLC并完成安全互锁验收；不能在UI绕过"),
            new("WARNING", "CAMERA_NOT_COMMISSIONED", "PCB/针排相机", "Acknowledged", "完成相机、镜头、光源选型和实机标定"),
            new("INFO", "USING_SYNTHETIC_DATA", "工程工作台", "Active", "仅用于学习；发布前导入冻结真实数据集")
        ]);
        AuditEvents = new ObservableCollection<string>(
        [
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} current.engineer LOGIN EngineeringOffline",
            $"{DateTime.Now.AddMinutes(-3):yyyy-MM-dd HH:mm:ss} runtime START Version=2-demo",
            $"{DateTime.Now.AddMinutes(-4):yyyy-MM-dd HH:mm:ss} system MOTION_GUARD Enabled"
        ]);
    }

    public ObservableCollection<DeviceHealth> Devices { get; }
    public ObservableCollection<AlarmRow> Alarms { get; }
    public ObservableCollection<string> AuditEvents { get; }

    [ObservableProperty] private AlarmRow? _selectedAlarm;
    [ObservableProperty] private string _maintenanceMessage = "工程离线保护生效；存在2个阻断项。";

    [RelayCommand]
    private void AcknowledgeAlarm()
    {
        MaintenanceMessage = SelectedAlarm is null
            ? "请选择报警。"
            : $"已演示确认 {SelectedAlarm.Code}；确认不等于清除，清除仍取决于恢复条件。";
        StageStatus = "报警确认演示完成";
    }

    [RelayCommand]
    private void RunHealthCheck()
    {
        MaintenanceMessage = "健康检查完成：Runtime演示可用；相机/PLC/光源未接入，因此系统保持EngineeringOffline且禁止运动。";
        StageStatus = "健康检查完成，有阻断项";
    }
}
