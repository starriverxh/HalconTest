using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vision.Desktop.Services;

namespace Vision.Desktop.ViewModels;

public partial class MainViewModel(VisionServiceClient serviceClient) : ObservableObject
{
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
    private bool _isBusy;

    public ObservableCollection<string> Logs { get; } = [];

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PingAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            var message = await serviceClient.PingAsync(Host, Port, cancellationToken);
            ConnectionStatus = "已连接";
            AddLog($"PING成功：{message}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TriggerAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            InspectionStatus = "检测中";
            var result = await serviceClient.TriggerAsync(
                Host,
                Port,
                string.IsNullOrWhiteSpace(SelectedImagePath) ? null : SelectedImagePath,
                cancellationToken);
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

            AddLog($"检测{InspectionStatus}：X={CorrectionX}, Y={CorrectionY}, T={CorrectionTheta}, Score={Score}");
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
        AddLog($"选择图片：{dialog.FileName}");
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await action(timeout.Token);
        }
        catch (Exception exception)
        {
            ConnectionStatus = "通信异常";
            InspectionStatus = "ERROR";
            StatusBrush = new SolidColorBrush(Color.FromRgb(251, 146, 60));
            AddLog($"错误：{exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        PingCommand.NotifyCanExecuteChanged();
        TriggerCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 100)
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
}
