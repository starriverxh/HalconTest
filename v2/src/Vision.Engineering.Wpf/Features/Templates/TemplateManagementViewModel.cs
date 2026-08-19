using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Features.Templates;

public sealed partial class TemplateManagementViewModel : FeatureViewModel
{
    private readonly IEngineeringWorkbenchGateway _gateway;

    public TemplateManagementViewModel(IEngineeringWorkbenchGateway gateway)
        : base(
            "templates", "模板管理", "Shape Model模板管理", "训练、观察轮廓、批量验证并版本化；单张图高分不等于模型可靠。",
            "Shape Model负责产品粗定位和建立产品坐标系。模板ROI要包含唯一、稳定的轮廓组合，随后检测ROI才能跟随产品平移和旋转。",
            ["训练图与唯一轮廓ROI", "允许角度/尺度范围", "OK、边界、NG验证集", "模型原点定义"],
            [".shm模型工件", "模型元数据和样本指纹", "分数分布与定位误差", "已验证模板版本"],
            ["create_shape_model", "get_shape_model_contours", "set_shape_model_origin", "find_shape_model", "write_shape_model / read_shape_model"],
            [
                new("Angle start/extent", "deg", "搜索角度范围", "-10 / 20", "范围过宽增加耗时和误匹配，过窄漏掉姿态漂移"),
                new("Contrast", "gray", "进入模板的边缘强度", "20", "过低纳入噪声，过高丢失弱边缘"),
                new("MinScore", "0…1", "匹配最低接受分数", "0.65", "必须来自OK最低分与NG最高分分布间隔"),
                new("Greediness", "0…1", "搜索速度与完整性权衡", "0.9", "越高越快，但可能漏掉弱匹配")
            ],
            ["模板XLD轮廓", "各层金字塔轮廓", "训练图原点", "每张验证图Row/Column/Angle/Score", "OK与NG分数直方图"],
            ["MODEL_ROI_INVALID", "MODEL_CONTOUR_EMPTY", "BOARD_NOT_FOUND", "BOARD_SCORE_TOO_LOW", "MODEL_VERSION_MISMATCH"],
            ["模板ROI具有唯一性", "关键NG验证集零错误匹配", "定位误差满足特征预算", "模型、元数据、样本哈希和HALCON版本一一对应"])
    {
        _gateway = gateway;
        Samples = new ObservableCollection<string>(["OK: 80", "边界: 20", "NG: 20", "光照扰动: 20"]);
        ContourLayers = new ObservableCollection<string>(["Level 0: 待创建", "Level 1: 待创建", "Level 2: 待创建"]);
    }

    public ObservableCollection<string> Samples { get; }
    public ObservableCollection<string> ContourLayers { get; }

    [ObservableProperty] private string _modelName = "PCB_FIVE_HOLE_ROW";
    [ObservableProperty] private string _modelVersion = "Draft 0.1";
    [ObservableProperty] private double _angleStartDeg = -10;
    [ObservableProperty] private double _angleExtentDeg = 20;
    [ObservableProperty] private int _contrast = 20;
    [ObservableProperty] private double _minimumScore = 0.65;
    [ObservableProperty] private string _validationSummary = "尚未在冻结数据集上验证。";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private void CreateDraftModel()
    {
        ModelVersion = "Draft 0.2";
        ContourLayers.Clear();
        ContourLayers.Add("Level 0: 1,842 contour points");
        ContourLayers.Add("Level 1: 916 contour points");
        ContourLayers.Add("Level 2: 451 contour points");
        StageStatus = "教学轮廓已在内存生成；未运行HALCON、未写.shm";
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateTemplateAsync()
    {
        IsBusy = true;
        ValidateTemplateCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _gateway.ValidateTemplateAsync(CancellationToken.None);
            ValidationSummary =
                $"总数 {result.Total}，定位 {result.Located}；False Accept={result.FalseAccept}，False Reject={result.FalseReject}；" +
                $"OK最低分={result.MinimumOkScore:F3}，NG最高分={result.MaximumNgScore:F3}，P99={result.P99Milliseconds} ms。";
            ModelVersion = result.FalseAccept == 0 ? "Validated 0.2（演示）" : "Validation Failed";
            StageStatus = result.FalseAccept == 0
                ? "教学数据验证完成；未调用Runtime，不能审批或发布"
                : "教学数据验证失败";
        }
        finally
        {
            IsBusy = false;
            ValidateTemplateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanValidate() => !IsBusy;
}
