using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Traceability;

public sealed partial class TraceabilityViewModel : FeatureViewModel
{
    public TraceabilityViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "trace", "质量追溯", "质量追溯与离线复盘", "按CycleId还原当时使用的图像、工件版本、测量值、失败码和阶段耗时。",
            "没有配方、模板、标定和FrameId的结果不能解释，也不能用于质量争议和算法回归。离线重算必须保留原判定，不能覆盖生产事实。",
            ["CycleId/SN/时间", "原图、叠加图和必要中间结果", "版本化配方/模板/标定", "PLC触发ID和轴位置"],
            ["单件完整证据链", "当时版本重放", "候选版本离线重算", "统计和导出报告"],
            ["read_image（复盘）", "使用记录中的版本重建Pipeline", "候选版本A/B对比"],
            [
                new("Retention", "day", "图像与结果保留周期", "90/365", "OK与NG可采用不同策略，但不能破坏法规和客户要求"),
                new("CycleId", "string", "贯穿PLC、相机、算法、存储的唯一ID", "LINE1-20260819-000031", "重复或缺失必须报警")
            ],
            ["原始结果JSON", "图像FrameId", "每阶段输入输出摘要", "版本指纹", "重放差异"],
            ["TRACE_RECORD_INCOMPLETE", "IMAGE_NOT_FOUND", "ARTIFACT_VERSION_MISSING", "REPLAY_NONDETERMINISTIC"],
            ["100%周期可按CycleId查询", "原判定不可被离线重算覆盖", "所有工件版本可解析", "导出内容与数据库记录一致"])
    {
        Records = new ObservableCollection<TraceRecord>(gateway.GetTraceRecords());
        SelectedRecord = Records.FirstOrDefault();
    }

    public ObservableCollection<TraceRecord> Records { get; }

    [ObservableProperty] private TraceRecord? _selectedRecord;
    [ObservableProperty] private string _detail = "选择一条记录。";

    partial void OnSelectedRecordChanged(TraceRecord? value)
    {
        Detail = value is null
            ? "未选择记录。"
            : $"Cycle: {value.CycleId}\n产品: {value.ProductId}\n原判定: {value.Disposition}\n配方: {value.RecipeVersion}\n失败码: {value.FailureCode}\n耗时: {value.DurationMs} ms\n\n演示记录不包含生产图像；正式系统必须解析原始FrameId和工件指纹。";
    }

    [RelayCommand]
    private void ReplaySelected()
    {
        StageStatus = SelectedRecord is null
            ? "请选择记录"
            : $"{SelectedRecord.CycleId} 离线重放完成（演示）；原判定未被覆盖";
    }
}
