namespace Vision.Service.Workbench;

public interface IVisionWorkbench
{
    Task<ProcessingResult> PreviewProcessingAsync(ProcessingRequest request, CancellationToken cancellationToken);
    Task<ShapeModelCreateResult> CreateShapeModelAsync(ShapeModelCreateRequest request, CancellationToken cancellationToken);
    Task<ShapeModelMatchResult> MatchShapeModelAsync(ShapeModelMatchRequest request, CancellationToken cancellationToken);
    Task<CalibrationFitResult> FitCalibrationAsync(CalibrationFitRequest request, CancellationToken cancellationToken);
    Task<RecipeSaveResult> SaveRecipeAsync(RecipeSaveRequest request, CancellationToken cancellationToken);
    Task<RecipeSnapshotResult> GetRecipeAsync(RecipeGetRequest request, CancellationToken cancellationToken);
}
