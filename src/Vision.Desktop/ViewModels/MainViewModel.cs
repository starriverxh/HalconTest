using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vision.Desktop.Models;
using Vision.Desktop.Services;

namespace Vision.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VisionServiceClient _serviceClient;
    private readonly string _workspaceRoot;
    private string? _binaryPreviewPath;

    public MainViewModel(VisionServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
        _workspaceRoot = FindWorkspaceRoot();
        CalibrationModelPath = Path.Combine(_workspaceRoot, "runtime", "calibration", "camera-to-machine-taught.json");
        CalibrationCsvPath = Path.Combine(_workspaceRoot, "runtime", "calibration", "nine-point-taught.csv");
        var defaultImage = Path.Combine(_workspaceRoot, "data", "offline", "synthetic_pcb_good.png");
        if (File.Exists(defaultImage))
        {
            SelectedImagePath = defaultImage;
            PreviewImage = LoadBitmap(defaultImage);
            ProcessingPreviewImage = PreviewImage;
        }
    }

    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private int _port = 9100;

    [ObservableProperty]
    private string _selectedImagePath = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "未连接";

    [ObservableProperty]
    private string _inspectionStatus = "等待触发";

    [ObservableProperty]
    private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));

    [ObservableProperty]
    private string _correctionX = "--";

    [ObservableProperty]
    private string _correctionY = "--";

    [ObservableProperty]
    private string _correctionTheta = "--";

    [ObservableProperty]
    private string _score = "--";

    [ObservableProperty]
    private string _duration = "--";

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private ImageSource? _processingPreviewImage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private RectangleRoiData? _currentRoi;

    [ObservableProperty]
    private IReadOnlyList<VisionOverlay> _processingOverlays = [];

    [ObservableProperty]
    private IReadOnlyList<VisionOverlay> _templateOverlays = [];

    [ObservableProperty]
    private IReadOnlyList<VisionOverlay> _calibrationOverlays = [];

    [ObservableProperty]
    private double _thresholdMin;

    [ObservableProperty]
    private double _thresholdMax = 65;

    [ObservableProperty]
    private double _minRadiusPx = 15;

    [ObservableProperty]
    private double _maxRadiusPx = 22;

    [ObservableProperty]
    private double _minCircularity = 0.85;

    [ObservableProperty]
    private string _processingSummary = "先在图上拖动矩形 ROI，再执行预处理。";

    [ObservableProperty]
    private string _modelName = "pcb-five-hole-row";

    [ObservableProperty]
    private string _shapeModelPath = string.Empty;

    [ObservableProperty]
    private double _modelAngleStartDeg = -10;

    [ObservableProperty]
    private double _modelAngleExtentDeg = 20;

    [ObservableProperty]
    private int _modelContrast = 20;

    [ObservableProperty]
    private double _matchMinScore = 0.5;

    [ObservableProperty]
    private int _matchCount = 1;

    [ObservableProperty]
    private string _templateSummary = "画 ROI 创建 .shm 模型，然后在当前图片中匹配。";

    [ObservableProperty]
    private ImageCoordinate? _selectedCalibrationImagePoint;

    [ObservableProperty]
    private double _teachingRow;

    [ObservableProperty]
    private double _teachingColumn;

    [ObservableProperty]
    private double _teachingMachineX;

    [ObservableProperty]
    private double _teachingMachineY;

    [ObservableProperty]
    private bool _teachingUseForFit = true;

    [ObservableProperty]
    private CalibrationPointRow? _selectedCalibrationPoint;

    [ObservableProperty]
    private string _calibrationModelPath = string.Empty;

    [ObservableProperty]
    private string _calibrationCsvPath = string.Empty;

    [ObservableProperty]
    private string _calibrationSummary = "点击图像特征点，录入该位置的机械 X/Y；至少 3 个拟合点，推荐 9 点并另留验证点。";

    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<CalibrationPointRow> CalibrationPoints { get; } = [];

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PingAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            var message = await _serviceClient.PingAsync(Host, Port, cancellationToken);
            ConnectionStatus = "已连接";
            AddLog($"PING 成功：{message}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TriggerAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            EnsureImageSelected();
            InspectionStatus = "检测中";
            var result = await _serviceClient.TriggerAsync(Host, Port, SelectedImagePath, cancellationToken);
            InspectionStatus = result.IsOk ? "OK" : "NG";
            StatusBrush = new SolidColorBrush(result.IsOk
                ? Color.FromRgb(34, 197, 94)
                : Color.FromRgb(248, 113, 113));
            CorrectionX = result.X.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " mm";
            CorrectionY = result.Y.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " mm";
            CorrectionTheta = result.Theta.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "°";
            Score = result.Score.ToString("0.000", CultureInfo.InvariantCulture);
            Duration = result.DurationMs + " ms";
            if (result.ImagePath is { Length: > 0 } savedImagePath && File.Exists(savedImagePath))
            {
                PreviewImage = LoadBitmap(savedImagePath);
            }

            AddLog($"检测 {InspectionStatus}：X={CorrectionX}, Y={CorrectionY}, T={CorrectionTheta}, Score={Score}");
        });
    }

    [RelayCommand]
    private void SelectImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择离线产品图片",
            Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedImagePath = dialog.FileName;
        PreviewImage = LoadBitmap(dialog.FileName);
        ProcessingPreviewImage = PreviewImage;
        CurrentRoi = null;
        ProcessingOverlays = [];
        TemplateOverlays = [];
        _binaryPreviewPath = null;
        AddLog($"选择图片：{dialog.FileName}");
    }

    [RelayCommand]
    private void ClearRoi()
    {
        CurrentRoi = null;
        AddLog("ROI 已清除；空 ROI 表示对整幅图处理。");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PreviewProcessingAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            EnsureImageSelected();
            var result = await _serviceClient.PreviewProcessingAsync(
                Host, Port, SelectedImagePath, CurrentRoi,
                ThresholdMin, ThresholdMax, MinRadiusPx, MaxRadiusPx, MinCircularity,
                cancellationToken);
            _binaryPreviewPath = result.BinaryPreviewPath;
            CurrentRoi = result.Roi;
            ProcessingOverlays = result.Points.Select((point, index) => new VisionOverlay(
                point.Row, point.Column, point.RadiusPx,
                $"#{index + 1} C={point.Score:F3}")).ToArray();
            ProcessingPreviewImage = PreviewImage;
            ProcessingSummary =
                $"HALCON: threshold → connection → select_shape → area_center；" +
                $"候选 {result.CandidateCount}，筛选后 {result.SelectedCount}，总阈值面积 {result.CandidateAreaPx:F0} px，" +
                $"耗时 {result.ProcessingTimeMs} ms。";
            AddLog($"预处理完成：找到 {result.SelectedCount} 个圆孔。二值图：{result.BinaryPreviewPath}");
        });
    }

    [RelayCommand]
    private void ShowProcessingOriginal()
    {
        ProcessingPreviewImage = PreviewImage;
    }

    [RelayCommand]
    private void ShowProcessingBinary()
    {
        if (_binaryPreviewPath is not null && File.Exists(_binaryPreviewPath))
        {
            ProcessingPreviewImage = LoadBitmap(_binaryPreviewPath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CreateTemplateAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            EnsureImageSelected();
            if (CurrentRoi is null)
            {
                throw new InvalidOperationException("请先在模板页图像上拖出矩形 ROI。");
            }

            var result = await _serviceClient.CreateShapeModelAsync(
                Host, Port, SelectedImagePath, CurrentRoi, ModelName,
                ModelAngleStartDeg, ModelAngleExtentDeg, ModelContrast, cancellationToken);
            ShapeModelPath = result.ModelPath;
            TemplateOverlays =
            [
                new VisionOverlay(result.OriginRow, result.OriginColumn, 10, "模型原点", "#38BDF8", "cross")
            ];
            TemplateSummary = $"模型已写入 {result.ModelPath}；耗时 {result.ProcessingTimeMs} ms。";
            AddLog($"HALCON shape model 创建完成：{result.ModelPath}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task MatchTemplateAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            EnsureImageSelected();
            if (string.IsNullOrWhiteSpace(ShapeModelPath))
            {
                throw new InvalidOperationException("请先创建模板，或填写已有 .shm 路径。");
            }

            var result = await _serviceClient.MatchShapeModelAsync(
                Host, Port, SelectedImagePath, ShapeModelPath,
                ModelAngleStartDeg, ModelAngleExtentDeg, MatchMinScore, MatchCount, 0.5,
                cancellationToken);
            TemplateOverlays = result.Matches.Select((match, index) => new VisionOverlay(
                match.Row, match.Column, 14,
                $"M{index + 1} S={match.Score:F3} A={match.AngleDeg:F2}°",
                "#F59E0B", "cross")).ToArray();
            TemplateSummary = result.Matches.Count == 0
                ? $"未找到分数 ≥ {MatchMinScore:F2} 的模型实例；耗时 {result.ProcessingTimeMs} ms。"
                : $"找到 {result.Matches.Count} 个实例；最佳分数 {result.Matches[0].Score:F3}，" +
                  $"角度 {result.Matches[0].AngleDeg:F3}°，耗时 {result.ProcessingTimeMs} ms。";
            AddLog($"模板匹配完成：{result.Matches.Count} 个结果。");
        });
    }

    [RelayCommand]
    private void AddCalibrationPoint()
    {
        if (SelectedCalibrationImagePoint is null)
        {
            AddLog("提示：先在标定页图像上点击一个特征点。");
            return;
        }

        AddCalibrationRow(new CalibrationPointRow
        {
            Row = TeachingRow,
            Column = TeachingColumn,
            MachineX = TeachingMachineX,
            MachineY = TeachingMachineY,
            UseForFit = TeachingUseForFit
        });
        RefreshCalibrationOverlays();
        AddLog($"加入标定点：pixel=({TeachingColumn:F2}, {TeachingRow:F2}) → machine=({TeachingMachineX:F3}, {TeachingMachineY:F3})");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SnapCalibrationPointAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            EnsureImageSelected();
            var clicked = SelectedCalibrationImagePoint
                ?? throw new InvalidOperationException("先在图像上单击标定圆孔附近。鼠标只用于给出搜索范围。");
            var searchRoi = new RectangleRoiData(
                clicked.Row - 45,
                clicked.Column - 45,
                clicked.Row + 45,
                clicked.Column + 45);
            var result = await _serviceClient.PreviewProcessingAsync(
                Host, Port, SelectedImagePath, searchRoi,
                ThresholdMin, ThresholdMax, MinRadiusPx, MaxRadiusPx, MinCircularity,
                cancellationToken);
            var nearest = result.Points
                .OrderBy(point => Math.Pow(point.Row - clicked.Row, 2) + Math.Pow(point.Column - clicked.Column, 2))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("搜索 ROI 内没有找到符合参数的圆孔；请检查阈值、半径和点击位置。");
            SelectedCalibrationImagePoint = new ImageCoordinate(nearest.Row, nearest.Column);
            AddLog($"HALCON 精定位标定中心：row={nearest.Row:F3}, column={nearest.Column:F3}, circularity={nearest.Score:F3}");
        });
    }

    [RelayCommand]
    private void RemoveCalibrationPoint()
    {
        if (SelectedCalibrationPoint is null)
        {
            return;
        }

        CalibrationPoints.Remove(SelectedCalibrationPoint);
        SelectedCalibrationPoint = null;
        RefreshCalibrationOverlays();
    }

    [RelayCommand]
    private void LoadCalibrationCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "载入九点标定 CSV",
            InitialDirectory = Path.Combine(_workspaceRoot, "config"),
            FileName = "nine-point.sample.csv",
            Filter = "CSV 文件|*.csv|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LoadCalibrationPoints(dialog.FileName);
        }
        catch (Exception exception)
        {
            AddLog($"CSV 载入失败：{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task FitCalibrationAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            if (CalibrationPoints.Count(point => point.UseForFit) < 3)
            {
                throw new InvalidOperationException("至少需要 3 个启用拟合的非共线点；实际项目推荐 9 个拟合点并增加验证点。");
            }

            var result = await _serviceClient.FitCalibrationAsync(
                Host, Port, CalibrationPoints.ToArray(), CalibrationModelPath, CalibrationCsvPath, cancellationToken);
            for (var index = 0; index < Math.Min(CalibrationPoints.Count, result.Points.Count); index++)
            {
                CalibrationPoints[index].PredictedX = result.Points[index].PredictedX;
                CalibrationPoints[index].PredictedY = result.Points[index].PredictedY;
                CalibrationPoints[index].ErrorMm = result.Points[index].ErrorMm;
            }

            CalibrationSummary =
                $"拟合完成：RMSE={result.Model.RmseMm:F6} mm，最大拟合误差={result.Model.MaxFitErrorMm:F6} mm，" +
                $"最大独立验证误差={result.Model.MaxValidationErrorMm:F6} mm。模型：{result.ModelPath}";
            AddLog($"九点标定完成：RMSE={result.Model.RmseMm:F6} mm，模型和 CSV 已保存。");
        });
    }

    partial void OnSelectedCalibrationImagePointChanged(ImageCoordinate? value)
    {
        if (value is null)
        {
            return;
        }

        TeachingRow = value.Row;
        TeachingColumn = value.Column;
    }

    partial void OnIsBusyChanged(bool value)
    {
        PingCommand.NotifyCanExecuteChanged();
        TriggerCommand.NotifyCanExecuteChanged();
        PreviewProcessingCommand.NotifyCanExecuteChanged();
        CreateTemplateCommand.NotifyCanExecuteChanged();
        MatchTemplateCommand.NotifyCanExecuteChanged();
        FitCalibrationCommand.NotifyCanExecuteChanged();
        SnapCalibrationPointCommand.NotifyCanExecuteChanged();
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await action(timeout.Token);
            ConnectionStatus = "已连接";
        }
        catch (Exception exception)
        {
            ConnectionStatus = "通信/算法异常";
            StatusBrush = new SolidColorBrush(Color.FromRgb(251, 146, 60));
            AddLog($"错误：{exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadCalibrationPoints(string path)
    {
        var rows = File.ReadAllLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(',', StringSplitOptions.TrimEntries))
            .ToArray();
        CalibrationPoints.Clear();
        foreach (var values in rows)
        {
            if (values.Length != 5)
            {
                throw new InvalidDataException("标定 CSV 必须是 row,column,machine_x_mm,machine_y_mm,use_for_fit 五列。");
            }

            AddCalibrationRow(new CalibrationPointRow
            {
                Row = double.Parse(values[0], CultureInfo.InvariantCulture),
                Column = double.Parse(values[1], CultureInfo.InvariantCulture),
                MachineX = double.Parse(values[2], CultureInfo.InvariantCulture),
                MachineY = double.Parse(values[3], CultureInfo.InvariantCulture),
                UseForFit = bool.Parse(values[4])
            });
        }

        RefreshCalibrationOverlays();
        AddLog($"载入 {CalibrationPoints.Count} 个标定点：{path}");
    }

    private void RefreshCalibrationOverlays()
    {
        CalibrationOverlays = CalibrationPoints.Select((point, index) => new VisionOverlay(
            point.Row,
            point.Column,
            7,
            $"P{index + 1} ({point.MachineX:F2},{point.MachineY:F2})",
            point.UseForFit ? "#A78BFA" : "#F59E0B",
            "cross")).ToArray();
    }

    private void AddCalibrationRow(CalibrationPointRow row)
    {
        row.PropertyChanged += (_, _) => RefreshCalibrationOverlays();
        CalibrationPoints.Add(row);
    }

    private void EnsureImageSelected()
    {
        if (string.IsNullOrWhiteSpace(SelectedImagePath) || !File.Exists(SelectedImagePath))
        {
            throw new FileNotFoundException("请先选择存在的产品图片。", SelectedImagePath);
        }
    }

    private void AddLog(string message)
    {
        Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 150)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string FindWorkspaceRoot()
    {
        var candidates = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            for (var depth = 0; depth < 10 && directory is not null; depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HalconPinInspection.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        return Environment.CurrentDirectory;
    }
}
