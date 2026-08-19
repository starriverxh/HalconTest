using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Regression;

public sealed record DatasetClassRow(string ClassName, int Count, string GoldLabel, string Risk);

public sealed partial class DatasetRegressionViewModel : FeatureViewModel
{
    private readonly IEngineeringWorkbenchGateway _gateway;

    public DatasetRegressionViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "regression", "数据集回归", "数据集回归", "用冻结的黄金数据集客观比较活动配方与候选配方，并阻止算法退化进入生产。",
            "单张好图无法说明检出率、误杀、位置精度和最坏耗时。数据集必须覆盖正常、边界、缺陷、光照、姿态和污染变化，并由质量人员复核标签。",
            ["带SHA-256的数据集版本", "黄金标签和审核记录", "活动/候选配方", "位置真值与错误码真值"],
            ["混淆矩阵", "定位误差分布", "P50/P95/P99耗时", "逐样本差异和发布Gate报告"],
            ["read_image", "候选配方完整HALCON Pipeline", "结果与Golden Label比较"],
            [
                new("False Accept", "count", "NG被错误判为OK", "0", "错插风险场景通常要求关键挑战集为零"),
                new("False Reject", "%", "OK被错误判为NG", "≤ 工艺约定", "过高会降低设备稼动率，但不能靠放宽安全阈值解决"),
                new("Position error", "mm", "算法结果相对计量真值", "≤ 0.010", "应看P95和Maximum并按视野位置分层"),
                new("P99 time", "ms", "99%样本不超过的算法时间", "≤ 100", "必须低于周期预算并留调度余量")
            ],
            ["每张样本的节点结果", "Active vs Candidate差异", "失败样本缩略图", "混淆矩阵", "耗时直方图"],
            ["DATASET_HASH_MISMATCH", "GOLD_LABEL_MISSING", "FALSE_ACCEPT_DETECTED", "POSITION_ERROR_EXCEEDED", "REGRESSION_GATE_FAILED"],
            ["数据集和标签版本冻结", "关键NG集合False Accept=0", "定位误差满足分项预算", "P99低于周期预算", "报告绑定候选配方完整工件指纹"])
    {
        _gateway = gateway;
        Classes = new ObservableCollection<DatasetClassRow>(
        [
            new("OK/正常批次", 180, "OK + pose", "基线"),
            new("边界/最大漂移", 50, "OK + pose", "漏检"),
            new("缺针/弯针/节距", 60, "明确NG code", "错误放行"),
            new("欠曝/过曝/虚焦", 40, "IMAGE_*", "图像门禁"),
            new("污染/遮挡/反光", 30, "NG或可恢复", "鲁棒性")
        ]);
    }

    public ObservableCollection<DatasetClassRow> Classes { get; }

    [ObservableProperty] private string _datasetVersion = "pcb-pin-golden-v1 · SHA256: DEMO-7A91…";
    [ObservableProperty] private string _candidateRecipe = "2.4.0-draft";
    [ObservableProperty] private string _resultSummary = "尚未执行候选回归。";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunRegressionAsync()
    {
        IsBusy = true;
        Progress = 15;
        RunRegressionCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _gateway.RunRegressionAsync(CancellationToken.None);
            Progress = 100;
            ResultSummary = $"{result.ReportId}：{result.Passed}/{result.Total}符合黄金标签；False Accept={result.FalseAccept}，False Reject={result.FalseReject}；最大位置误差={result.MaximumPositionErrorMm:F3} mm，P99={result.P99Milliseconds} ms。";
            StageStatus = result.FalseAccept == 0
                ? "UI教学回归完成；未调用Runtime，正式证据请运行CMD 05"
                : "UI教学回归失败";
        }
        finally
        {
            IsBusy = false;
            RunRegressionCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !IsBusy;
}
