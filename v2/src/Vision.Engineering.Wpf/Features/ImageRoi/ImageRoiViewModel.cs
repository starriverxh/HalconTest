using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Features.ImageRoi;

public sealed partial class ImageRoiViewModel : FeatureViewModel
{
    public ImageRoiViewModel()
        : base(
            "image-roi", "图像与ROI工作台", "图像处理与ROI工作台", "逐节点检查HALCON的图像、Region、XLD和数值输出；ROI属于具体节点并可跟随产品位姿。",
            "ROI不仅用于提速，更用于隔离干扰。生产ROI必须定义在参考产品坐标中，经粗定位刚性变换后跟随，而不是永远固定在相机像素位置。",
            ["原始图和图像质量结果", "粗定位参考位姿", "命名ROI集合", "Blob/轮廓参数"],
            ["筛选Region和特征点", "产品坐标下的局部测量", "阶段失败码", "可保存的草稿算法参数"],
            ["reduce_domain", "threshold", "connection", "select_shape", "area_center", "gen_contour_region_xld", "affine_trans_region"],
            [
                new("Threshold min/max", "gray", "保留的灰度范围", "0…65", "过宽引入背景，过窄会丢失污染/亮度波动样本"),
                new("Area", "px²", "连通域面积范围", "700…1600", "应由真实样本分布确定，不应只换算理想圆面积"),
                new("Circularity", "0…1", "4πA/P²圆度", "≥ 0.85", "缺口、粘连和噪声会降低圆度"),
                new("ROI coordinate", "space", "Image或ProductPose", "ProductPose", "生产检测区通常应跟随定位，图像质量ROI可固定")
            ],
            ["灰度直方图", "二值Region", "连接后候选区", "形状筛选后Region", "质心/半径/圆度表", "位姿变换后的ROI"],
            ["IMAGE_LOW_CONTRAST", "ROI_OUT_OF_IMAGE", "FEATURE_COUNT_MISMATCH", "BOARD_SCORE_TOO_LOW"],
            ["不同漂移和旋转下ROI仍覆盖目标", "显示坐标与原图Row/Column误差≤0.25 px", "OK/边界/NG样本参数分布有安全间隔", "所有失败都有稳定错误码"])
    {
        ShapeKinds = Enum.GetValues<RoiShapeKind>();
        CoordinateSpaces = Enum.GetValues<RoiCoordinateSpace>();
        PipelineStages = new ObservableCollection<FeatureStage>(
        [
            new("图像质量", "intensity → deviation_image → sobel_amp", "亮度/对比度/清晰度/饱和率", "通过"),
            new("粗定位", "find_shape_model", "Row/Column/Angle/Score", "模拟通过"),
            new("ROI跟随", "vector_angle_to_rigid → affine_trans_region", "产品坐标ROI", "待预览"),
            new("孔Blob", "threshold → connection → select_shape", "5个孔中心", "待预览"),
            new("几何复核", "fit_line_contour_xld / distance_pp", "孔距/直线度", "待预览")
        ]);
        Rois = new ObservableCollection<RoiDefinition>();
        AddExampleRois();
        SelectedRoi = Rois[0];
    }

    public IReadOnlyList<RoiShapeKind> ShapeKinds { get; }
    public IReadOnlyList<RoiCoordinateSpace> CoordinateSpaces { get; }
    public ObservableCollection<RoiDefinition> Rois { get; }
    public ObservableCollection<FeatureStage> PipelineStages { get; }

    [ObservableProperty] private RoiShapeKind _activeTool = RoiShapeKind.Rectangle1;
    [ObservableProperty] private RoiDefinition? _selectedRoi;
    [ObservableProperty] private FeatureStage? _selectedPipelineStage;
    [ObservableProperty] private string _previewSummary = "画布为1000×600合成PCB；选择工具后绘制ROI。多边形使用左键加点、右键结束。";

    [RelayCommand]
    private void AddRoi(RoiDefinition roi)
    {
        Rois.Add(roi);
        SelectedRoi = roi;
        PreviewSummary = $"已创建 {roi.Name}：{roi.ShapeKind} / {roi.CoordinateSpace} / 跟随={roi.FollowLocalization}";
        StageStatus = "ROI草稿已修改，尚未发布";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRoi))]
    private void DuplicateRoi()
    {
        var clone = SelectedRoi!.Clone($"{SelectedRoi.Name}_副本");
        Rois.Add(clone);
        SelectedRoi = clone;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRoi))]
    private void DeleteRoi()
    {
        var removed = SelectedRoi!;
        Rois.Remove(removed);
        SelectedRoi = Rois.FirstOrDefault();
        PreviewSummary = $"已从草稿移除 {removed.Name}。";
    }

    [RelayCommand]
    private void RunPipelinePreview()
    {
        foreach (var stage in PipelineStages)
        {
            stage.Status = "演示通过";
        }
        PreviewSummary = "候选=7，筛选后=5；孔中心已在画布中标记。当前仅为合成样本，不构成真实验收。";
        StageStatus = "离线节点预览完成";
    }

    private bool HasSelectedRoi() => SelectedRoi is not null;

    partial void OnSelectedRoiChanged(RoiDefinition? value)
    {
        DuplicateRoiCommand.NotifyCanExecuteChanged();
        DeleteRoiCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPipelineStageChanged(FeatureStage? value)
    {
        if (value is not null)
        {
            OperatorExplanation = $"{value.Name}\n算子：{value.OperatorChain}\n输出：{value.Output}\n状态：{value.Status}";
        }
    }

    private void AddExampleRois()
    {
        Rois.Add(new RoiDefinition("Board_Search_Rect1", RoiShapeKind.Rectangle1)
        {
            Row1 = 115, Column1 = 120, Row2 = 485, Column2 = 880,
            CoordinateSpace = RoiCoordinateSpace.Image, FollowLocalization = false
        });
        Rois.Add(new RoiDefinition("Hole_Row_Rect2", RoiShapeKind.Rectangle2)
        {
            CenterRow = 300, CenterColumn = 500, PhiDeg = 0, Length1 = 360, Length2 = 62
        });
        Rois.Add(new RoiDefinition("Reference_Hole_Circle", RoiShapeKind.Circle)
        {
            CenterRow = 300, CenterColumn = 220, Radius = 36
        });
        var polygon = new RoiDefinition("Exclude_Connector_Polygon", RoiShapeKind.Polygon)
        {
            CenterRow = 180, CenterColumn = 760
        };
        foreach (var vertex in new[] { new RoiVertex(120, 700), new RoiVertex(120, 840), new RoiVertex(210, 820), new RoiVertex(220, 710) })
        {
            polygon.Vertices.Add(vertex);
        }
        Rois.Add(polygon);
    }
}
