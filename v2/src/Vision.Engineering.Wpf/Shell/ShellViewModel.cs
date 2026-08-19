using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSystem.Engineering.Wpf.Features.Calibration;
using VisionSystem.Engineering.Wpf.Features.ImageRoi;
using VisionSystem.Engineering.Wpf.Features.Maintenance;
using VisionSystem.Engineering.Wpf.Features.Manual;
using VisionSystem.Engineering.Wpf.Features.Production;
using VisionSystem.Engineering.Wpf.Features.ProjectWizard;
using VisionSystem.Engineering.Wpf.Features.Recipes;
using VisionSystem.Engineering.Wpf.Features.Regression;
using VisionSystem.Engineering.Wpf.Features.Templates;
using VisionSystem.Engineering.Wpf.Features.Traceability;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;

namespace VisionSystem.Engineering.Wpf.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(IEngineeringWorkbenchGateway gateway)
    {
        Features = new ObservableCollection<FeatureViewModel>(
        [
            new ProductionViewModel(gateway),
            new ManualImagingViewModel(),
            new ProjectWizardViewModel(),
            new ImageRoiViewModel(),
            new TemplateManagementViewModel(gateway),
            new CalibrationCenterViewModel(gateway),
            new RecipeCenterViewModel(),
            new DatasetRegressionViewModel(gateway),
            new TraceabilityViewModel(gateway),
            new SystemMaintenanceViewModel(gateway)
        ]);
        _currentFeature = Features[0];
    }

    public ObservableCollection<FeatureViewModel> Features { get; }
    public string RuntimeMode => "ENGINEERING OFFLINE";
    public string MotionPermission => "MotionAllowed = false";
    public string CurrentUser => "current.engineer · 视觉工程师";
    public string ActiveRecipe => "pcb-pin-v2.3 · 演示";

    [ObservableProperty]
    private FeatureViewModel _currentFeature;

    [RelayCommand]
    private void SelectFeature(FeatureViewModel feature) => CurrentFeature = feature;
}
