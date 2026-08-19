using VisionSystem.Engineering.Wpf.Controls;
using VisionSystem.Engineering.Wpf.Features.Calibration;
using VisionSystem.Engineering.Wpf.Features.ImageRoi;
using VisionSystem.Engineering.Wpf.Features.Manual;
using VisionSystem.Engineering.Wpf.Features.Maintenance;
using VisionSystem.Engineering.Wpf.Features.Production;
using VisionSystem.Engineering.Wpf.Features.ProjectWizard;
using VisionSystem.Engineering.Wpf.Features.Recipes;
using VisionSystem.Engineering.Wpf.Features.Regression;
using VisionSystem.Engineering.Wpf.Features.Templates;
using VisionSystem.Engineering.Wpf.Features.Traceability;
using VisionSystem.Engineering.Wpf.Models;
using VisionSystem.Engineering.Wpf.Services;
using VisionSystem.Engineering.Wpf.Shell;

namespace VisionSystem.Engineering.Wpf.Tests;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

    private static async Task<int> MainAsync(string[] args)
    {
        try
        {
            var application = new App();
            application.InitializeComponent();
            var gateway = new DemoEngineeringWorkbenchGateway();
            var shell = new ShellViewModel(gateway);

            Test("compiled WPF shell constructs with application resources", () =>
            {
                var window = new MainWindow();
                Check(window.DataContext is ShellViewModel, "MainWindow composition root did not create ShellViewModel.");
                window.Measure(new System.Windows.Size(1680, 980));
                window.Arrange(new System.Windows.Rect(0, 0, 1680, 980));
                Check(window.Content is not null, "MainWindow has no content.");
                window.Close();
            });

            Test("shell exposes ten modular features", () =>
            {
                Equal(10, shell.Features.Count);
                Equal(10, shell.Features.Select(feature => feature.Key).Distinct(StringComparer.Ordinal).Count());
                Check(shell.Features.All(feature => feature.GetType().Name != "MainViewModel"), "A monolithic MainViewModel was introduced.");
            });

            Test("every feature contains beginner engineering evidence", () =>
            {
                foreach (var feature in shell.Features)
                {
                    Check(!string.IsNullOrWhiteSpace(feature.Why), $"{feature.Key} has no purpose explanation.");
                    Check(feature.Inputs.Count > 0, $"{feature.Key} has no inputs.");
                    Check(feature.Outputs.Count > 0, $"{feature.Key} has no outputs.");
                    Check(feature.Operators.Count > 0, $"{feature.Key} has no HALCON/runtime operator boundary.");
                    Check(feature.Parameters.Count > 0, $"{feature.Key} has no parameter units/meaning.");
                    Check(feature.IntermediateResults.Count > 0, $"{feature.Key} has no intermediate results.");
                    Check(feature.FailureCodes.Count > 0, $"{feature.Key} has no failure codes.");
                    Check(feature.AcceptanceCriteria.Count > 0, $"{feature.Key} has no acceptance criteria.");
                }
            });

            Test("navigation switches feature instances", () =>
            {
                var target = shell.Features.Single(feature => feature.Key == "calibration");
                shell.SelectFeatureCommand.Execute(target);
                Check(ReferenceEquals(target, shell.CurrentFeature), "Navigation did not retain the feature instance.");
            });

            Test("ROI domain supports all required shapes and spaces", () =>
            {
                var rectangle1 = new RoiDefinition("R1", RoiShapeKind.Rectangle1)
                    { Row1 = 10, Column1 = 20, Row2 = 100, Column2 = 200 };
                var rectangle2 = new RoiDefinition("R2", RoiShapeKind.Rectangle2)
                    { CenterRow = 50, CenterColumn = 60, Length1 = 30, Length2 = 15, PhiDeg = 12 };
                var circle = new RoiDefinition("C1", RoiShapeKind.Circle)
                    { CenterRow = 50, CenterColumn = 60, Radius = 25 };
                var polygon = new RoiDefinition("P1", RoiShapeKind.Polygon);
                polygon.Vertices.Add(new RoiVertex(0, 0));
                polygon.Vertices.Add(new RoiVertex(0, 10));
                polygon.Vertices.Add(new RoiVertex(10, 5));
                Check(rectangle1.IsValid && rectangle2.IsValid && circle.IsValid && polygon.IsValid, "One required ROI kind is invalid.");
                rectangle1.CoordinateSpace = RoiCoordinateSpace.ProductPose;
                rectangle1.FollowLocalization = true;
                var clone = rectangle1.Clone("R1-copy");
                Equal(RoiCoordinateSpace.ProductPose, clone.CoordinateSpace);
                Equal(true, clone.FollowLocalization);
            });

            Test("ROI workbench has example data and MVVM commands", () =>
            {
                var viewModel = new ImageRoiViewModel();
                Equal(4, viewModel.Rois.Select(roi => roi.ShapeKind).Distinct().Count());
                var created = new RoiDefinition("Added", RoiShapeKind.Circle)
                    { CenterRow = 100, CenterColumn = 100, Radius = 20 };
                viewModel.AddRoiCommand.Execute(created);
                Check(viewModel.Rois.Contains(created), "MVVM ROI create command did not add the ROI.");
                viewModel.RunPipelinePreviewCommand.Execute(null);
                Check(viewModel.PipelineStages.All(stage => stage.Status == "演示通过"), "Pipeline intermediate stages were not updated.");
                _ = new RoiCanvas();
            });

            Test("development wizard exposes gated conventional flow", () =>
            {
                var viewModel = new ProjectWizardViewModel();
                Equal(6, viewModel.Gates.Count);
                Check(viewModel.Gates.Any(gate => gate.Name.Contains("光学", StringComparison.Ordinal)), "Optical imaging gate is missing.");
                Check(viewModel.Gates.Any(gate => gate.Name.Contains("量产", StringComparison.Ordinal)), "Production release gate is missing.");
                viewModel.ReviewSelectedGateCommand.Execute(null);
                Check(viewModel.StageStatus.Contains("已复核", StringComparison.Ordinal), "Gate review command is display-only or inactive.");
            });

            await TestAsync("offline production cycle never authorizes motion", async () =>
            {
                var viewModel = new ProductionViewModel(gateway);
                await viewModel.RunOfflineCycleCommand.ExecuteAsync(null);
                Equal("ENGINEERING_ONLY", viewModel.Disposition);
                Equal("MOTION_DISABLED_OFFLINE", viewModel.LastFailure);
            });

            Test("manual motion request is explicitly rejected", () =>
            {
                var viewModel = new ManualImagingViewModel();
                viewModel.AcquireSingleFrameCommand.Execute(null);
                Check(viewModel.FrameId.StartsWith("OFFLINE-", StringComparison.Ordinal), "Manual acquisition command did not produce a frame identity.");
                Equal(4, viewModel.QualityMetrics.Count);
                viewModel.AttemptMotionCommand.Execute(null);
                Check(viewModel.MotionMessage.Contains("MOTION_DISABLED_OFFLINE", StringComparison.Ordinal), "Offline motion guard is not visible.");
            });

            await TestAsync("calibration demo separates fit and validation points", async () =>
            {
                var viewModel = new CalibrationCenterViewModel(gateway);
                await viewModel.FitOfflinePointsCommand.ExecuteAsync(null);
                Equal(11, viewModel.Points.Count);
                Equal(2, viewModel.Points.Count(point => point.IsValidation));
                Check(viewModel.FitSummary.Contains("独立验证", StringComparison.Ordinal), "Validation result is not explained.");
            });

            Test("T-axis calibration teaching action is operational and non-persistent", () =>
            {
                var viewModel = new CalibrationCenterViewModel(gateway);
                viewModel.RunTAxisTeachingCommand.Execute(null);
                Check(viewModel.FitSummary.Contains("正反向最大回差", StringComparison.Ordinal), "T-axis teaching result is missing backlash evidence.");
                Check(viewModel.CalibrationVersion.Contains("未写标定文件", StringComparison.Ordinal), "T-axis teaching result could be mistaken for a persisted calibration.");
            });

            await TestAsync("template page labels teaching data and does not claim shm persistence", async () =>
            {
                var viewModel = new TemplateManagementViewModel(gateway);
                viewModel.CreateDraftModelCommand.Execute(null);
                Check(viewModel.StageStatus.Contains("未写.shm", StringComparison.Ordinal), "Template teaching action could be mistaken for a real model write.");
                await viewModel.ValidateTemplateCommand.ExecuteAsync(null);
                Check(viewModel.StageStatus.Contains("未调用Runtime", StringComparison.Ordinal), "Template validation did not disclose its teaching gateway.");
            });

            await TestAsync("regression demo reports safety metrics", async () =>
            {
                var viewModel = new DatasetRegressionViewModel(gateway);
                await viewModel.RunRegressionCommand.ExecuteAsync(null);
                Equal(100d, viewModel.Progress);
                Check(viewModel.ResultSummary.Contains("False Accept=0", StringComparison.Ordinal), "False-accept metric is missing.");
            });

            Test("recipe activation remains blocked offline", () =>
            {
                var viewModel = new RecipeCenterViewModel();
                var initialCount = viewModel.Versions.Count;
                viewModel.CloneDraftCommand.Execute(null);
                Equal(initialCount + 1, viewModel.Versions.Count);
                Check(viewModel.WorkflowMessage.Contains("未写配方JSON", StringComparison.Ordinal), "Recipe teaching draft could be mistaken for a persisted artifact.");
                viewModel.ValidateDraftCommand.Execute(null);
                Check(viewModel.StageStatus.Contains("REGRESSION_GATE_FAILED", StringComparison.Ordinal), "Recipe validation gate is inactive.");
                viewModel.AttemptActivateCommand.Execute(null);
                Check(viewModel.StageStatus.Contains("OFFLINE", StringComparison.Ordinal), "Offline activation was not blocked.");
            });

            Test("traceability and maintenance pages have operational commands", () =>
            {
                var traceability = new TraceabilityViewModel(gateway);
                traceability.ReplaySelectedCommand.Execute(null);
                Check(traceability.StageStatus.Contains("重放完成", StringComparison.Ordinal), "Trace replay command is inactive.");
                var maintenance = new SystemMaintenanceViewModel(gateway);
                maintenance.RunHealthCheckCommand.Execute(null);
                Check(maintenance.StageStatus.Contains("健康检查完成", StringComparison.Ordinal), "Health-check command is inactive.");
            });

            await TestAsync("unavailable Runtime is not reported as a persisted simulation", async () =>
            {
                var unavailable = new RuntimeEngineeringWorkbenchGateway(port: 1);
                var result = await unavailable.RunOfflineCycleAsync(CancellationToken.None);
                Equal(false, result.RuntimeConnected);
                Equal("FAULT", result.Disposition);
                Check(result.SourceDescription.Contains("未写结果", StringComparison.Ordinal), "Runtime connection failure falsely claims persistence.");
            });

            Test("UI assembly has no HalconDotNet reference", () =>
            {
                var references = typeof(ShellViewModel).Assembly.GetReferencedAssemblies();
                Check(references.All(reference => !reference.Name!.Contains("Halcon", StringComparison.OrdinalIgnoreCase)), "UI assembly references HalconDotNet.");
            });

            if (args.Contains("--runtime-9201", StringComparer.OrdinalIgnoreCase))
            {
                await TestAsync("WPF production page uses real Runtime engineering endpoint 9201", async () =>
                {
                    var viewModel = new ProductionViewModel(new RuntimeEngineeringWorkbenchGateway());
                    await viewModel.RunOfflineCycleCommand.ExecuteAsync(null);
                    Check(viewModel.RuntimeConnection.Contains("已连接", StringComparison.Ordinal), "WPF did not connect to Runtime 9201.");
                    Equal("ENGINEERINGONLY", viewModel.Disposition.ToUpperInvariant());
                    Check(viewModel.MotionAuthority.Contains("MotionAllowed=false", StringComparison.Ordinal), "Runtime engineering result did not preserve the motion guard.");
                    Check(viewModel.SourceSummary.Contains("Runtime 127.0.0.1:9201", StringComparison.Ordinal), "Result source is not identified as Runtime 9201.");
                    Check(viewModel.CycleStates.Contains("PersistingResult"), "Runtime state history was not returned to the WPF page.");
                });
            }

            Console.WriteLine($"All {_passed} Vision.Engineering.Wpf self-tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Test(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static async Task TestAsync(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
