using System.Windows;
using Vision.Desktop.Services;
using Vision.Desktop.ViewModels;

namespace Vision.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new VisionServiceClient());
    }
}
