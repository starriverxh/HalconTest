using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Features.Recipes;

public sealed record RecipeVersionRow(
    string Version,
    string Status,
    string Author,
    DateTimeOffset Updated,
    string Model,
    string Calibration,
    string Regression);

public sealed partial class RecipeCenterViewModel : FeatureViewModel
{
    public RecipeCenterViewModel()
        : base(
            "recipes", "配方中心", "配方中心", "把相机、算法、ROI、模板、标定和判定限值组成不可变版本，再经过验证、审批和激活。",
            "活动配方不能现场覆盖。每次修改创建草稿版本，完成静态校验和数据集回归后再由不同角色审批，从而能追溯和安全回滚。",
            ["产品代码和工艺版本", "相机/光源Profile", "算法和ROI版本", "模板、标定和判定限值", "回归报告"],
            ["不可变RecipeRelease", "版本差异", "审批和激活记录", "可验证的回滚目标"],
            ["本页不直接执行HALCON；保存的是HALCON算法所需工件引用和强类型参数"],
            [
                new("Recipe version", "SemVer", "不可变发布版本", "2.3.0", "任何算法/阈值/工件变化都创建新版本"),
                new("Correction limit X/Y", "mm", "软件允许输出的安全范围", "±2.0", "不能代替PLC软限位和碰撞保护"),
                new("Max residual", "mm", "针孔刚性对齐后的最大残差", "≤ 0.05", "超限必须禁止运动")
            ],
            ["Schema校验", "依赖工件指纹", "版本Diff", "回归报告签名", "激活前设备预检"],
            ["RECIPE_INVALID", "RECIPE_VERSION_CONFLICT", "DEPENDENCY_NOT_APPROVED", "REGRESSION_GATE_FAILED", "ACTIVATION_NOT_ALLOWED_OFFLINE"],
            ["活动版本不可编辑", "工程师不能审批自己的版本", "未绑定已批准模板/标定/回归报告时不能提交", "激活和回滚均留审计记录"])
    {
        Versions = new ObservableCollection<RecipeVersionRow>(
        [
            new("2.3.0", "Active（演示）", "vision.eng", DateTimeOffset.Now.AddDays(-2), "shape-1.7", "cal-20260815", "REG-0041 PASS"),
            new("2.4.0-draft", "Draft", "vision.eng", DateTimeOffset.Now, "shape-1.8", "cal-20260815", "未执行"),
            new("2.2.1", "Retired", "vision.eng", DateTimeOffset.Now.AddDays(-12), "shape-1.6", "cal-20260728", "REG-0036 PASS")
        ]);
        Differences = new ObservableCollection<string>(["等待选择候选版本"]);
        SelectedVersion = Versions[1];
    }

    public ObservableCollection<RecipeVersionRow> Versions { get; }
    public ObservableCollection<string> Differences { get; }

    [ObservableProperty] private RecipeVersionRow? _selectedVersion;
    [ObservableProperty] private string _workflowMessage = "工程师角色：可编辑草稿，不可自审批。";

    partial void OnSelectedVersionChanged(RecipeVersionRow? value)
    {
        Differences.Clear();
        if (value is null) return;
        Differences.Add($"候选：{value.Version} / {value.Status}");
        Differences.Add($"ShapeModel：shape-1.7 → {value.Model}");
        Differences.Add($"Calibration：cal-20260815 → {value.Calibration}");
        Differences.Add($"Regression：{value.Regression}");
    }

    [RelayCommand]
    private void CloneDraft()
    {
        var version = $"2.{Versions.Count + 2}.0-draft";
        var row = new RecipeVersionRow(version, "Draft", "current.engineer", DateTimeOffset.Now, "shape-1.7", "cal-20260815", "未执行");
        Versions.Insert(0, row);
        SelectedVersion = row;
        WorkflowMessage = $"已创建 {version}；仅存在当前WPF内存，未调用Runtime、未写配方JSON。";
        StageStatus = "教学草稿已创建；关闭WPF后丢失";
    }

    [RelayCommand]
    private void ValidateDraft()
    {
        WorkflowMessage = "静态校验：Schema通过；模板和标定有效；数据集回归尚未绑定，因此不能提交审批。";
        StageStatus = "阻断：REGRESSION_GATE_FAILED";
    }

    [RelayCommand]
    private void AttemptActivate()
    {
        WorkflowMessage = "激活被拒绝：工程离线，且当前用户不能审批自己的草稿。未改变活动版本。";
        StageStatus = "ACTIVATION_NOT_ALLOWED_OFFLINE";
    }
}
