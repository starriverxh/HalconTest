using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Services;

public interface IEngineeringWorkbenchGateway
{
    bool IsOffline { get; }
    Task<ProductionResult> RunOfflineCycleAsync(CancellationToken cancellationToken);
    Task<TemplateValidationResult> ValidateTemplateAsync(CancellationToken cancellationToken);
    Task<CalibrationFitResult> FitCalibrationAsync(CancellationToken cancellationToken);
    Task<RegressionResult> RunRegressionAsync(CancellationToken cancellationToken);
    IReadOnlyList<TraceRecord> GetTraceRecords();
    IReadOnlyList<DeviceHealth> GetDeviceHealth();
}
