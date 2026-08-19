using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Features.ProjectWizard;

public sealed partial class ProjectWizardViewModel : FeatureViewModel
{
    public ProjectWizardViewModel()
        : base(
            "wizard", "项目开发向导", "项目开发向导", "用门禁和证据代替“感觉参数差不多”，按常规工业视觉流程推进。",
            "初学者最容易直接打开HDevelop调阈值，却没有先冻结需求、误差预算和成像条件。开发向导强制每一阶段留下可复核证据。",
            ["产品图纸、公差和节拍", "设备坐标系和运动能力", "OK/边界/NG实物", "质量与安全要求"],
            ["URS需求基线", "误差预算", "光学打样报告", "数据集、算法、标定和发布验收报告"],
            ["本阶段不直接运行算子；建立后续算子的输入边界和验收定义"],
            [
                new("最终插装误差", "mm", "针尖相对PCB孔的最大允许误差", "≤ 0.05", "必须分配给光学、特征、标定、运动、旋转和机构"),
                new("Cycle time", "ms", "视觉单周期预算", "≤ 500", "应同时定义P50/P95/P99，不只看一次最快结果"),
                new("FOV", "mm", "相机必须覆盖的物理视野", "由PCB孔排尺寸+漂移余量确定", "FOV与像素数共同决定mm/px")
            ],
            ["误差预算RSS", "最坏样本清单", "坐标系图", "各Gate签字证据"],
            ["REQUIREMENT_NOT_FROZEN", "ERROR_BUDGET_EXCEEDED", "DATASET_INCOMPLETE", "RELEASE_GATE_FAILED"],
            ["最终0.05 mm有分项预算和测量方法", "至少包含OK/边界/NG样本", "每个Gate均有版本化证据", "未通过Gate的配方不能进入生产"])
    {
        Gates = new ObservableCollection<DevelopmentGate>(
        [
            new("G0", "需求与误差预算", "URS、图纸、坐标系图、RSS预算", "公差/节拍/安全边界已签字", "待补真实图纸"),
            new("G1", "光学成像冻结", "FOV、mm/px、MTF、景深、光照报告", "中心和四角可分辨目标特征", "未开始"),
            new("G2", "样本与黄金标签", "OK/边界/NG数据集+SHA-256", "质量人员复核标签", "仅有合成图"),
            new("G3", "算法与ROI冻结", "节点结果、参数范围、失败码", "全数据集达到检测门槛", "演示"),
            new("G4", "标定与运动闭环", "内参、平面、T轴、独立验证", "误差预算分项全部通过", "硬件阻断"),
            new("G5", "发布与量产验收", "回归、GR&R、Cmk、168h报告", "跨部门审批", "阻断")
        ]);
        SelectedGate = Gates[0];
    }

    public ObservableCollection<DevelopmentGate> Gates { get; }

    [ObservableProperty] private DevelopmentGate? _selectedGate;
    [ObservableProperty] private string _gateDetail = "选择Gate查看证据缺口。";

    partial void OnSelectedGateChanged(DevelopmentGate? value)
    {
        GateDetail = value is null
            ? "未选择Gate。"
            : $"{value.Number} {value.Name}\n证据：{value.Evidence}\n验收：{value.Acceptance}\n当前：{value.Status}";
    }

    [RelayCommand]
    private void ReviewSelectedGate()
    {
        StageStatus = SelectedGate is null ? "请选择Gate" : $"{SelectedGate.Number} 已复核；仍需真实设备/样本证据";
    }
}
