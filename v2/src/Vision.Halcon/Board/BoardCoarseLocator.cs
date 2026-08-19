using HalconDotNet;
using VisionSystem.Domain;
using VisionSystem.Halcon.Geometry;
using VisionSystem.Halcon.Models;
using VisionSystem.Halcon.Segmentation;

namespace VisionSystem.Halcon.Board;

public sealed record BoardCoarseOptions
{
    public double MinimumShapeScoreMargin { get; init; } = 0.05;
}

internal sealed record BoardCoarseResult(
    IReadOnlyList<PixelPoint> Points,
    double Score,
    double MatchRow,
    double MatchColumn,
    double MatchAngleDeg,
    bool UsedShapeModel,
    int ConnectedCount,
    double MeanAreaPx);

internal sealed class BoardCoarseLocator(
    ShapeModelCache shapeModels,
    BoardCoarseOptions options)
{
    public BoardCoarseResult Locate(HObject grayImage, BoardVisionRecipe recipe)
    {
        if (!recipe.SearchRoi.IsValid)
        {
            throw new InvalidDataException("Board search ROI is invalid.");
        }

        HOperatorSet.GenRectangle1(
            out HObject staticSearchRegion,
            recipe.SearchRoi.Row1,
            recipe.SearchRoi.Column1,
            recipe.SearchRoi.Row2,
            recipe.SearchRoi.Column2);
        using (staticSearchRegion)
        {
            if (recipe.LocatorMode == BoardLocatorMode.BlobRow)
            {
                return ExtractBlobs(grayImage, staticSearchRegion, recipe, null);
            }

            var shape = shapeModels.Get(recipe.ShapeModel.ModelPath, recipe.ShapeModel.MetadataPath);
            HOperatorSet.ReduceDomain(grayImage, staticSearchRegion, out HObject searchImage);
            using (searchImage)
            {
                HOperatorSet.FindShapeModel(
                    searchImage,
                    shape.ModelId,
                    DegreesToRadians(recipe.ShapeModel.AngleStartDeg),
                    DegreesToRadians(recipe.ShapeModel.AngleExtentDeg),
                    recipe.ShapeModel.MinimumScore,
                    2,
                    recipe.ShapeModel.MaximumOverlap,
                    "least_squares",
                    recipe.ShapeModel.PyramidLevels,
                    recipe.ShapeModel.Greediness,
                    out HTuple rows,
                    out HTuple columns,
                    out HTuple angles,
                    out HTuple scores);
                using (rows)
                using (columns)
                using (angles)
                using (scores)
                {
                    if (scores.Length == 0)
                    {
                        throw new InvalidDataException("Shape model did not locate the PCB.");
                    }

                    if (scores.Length > 1 && scores[0].D - scores[1].D < options.MinimumShapeScoreMargin)
                    {
                        throw new InvalidDataException(
                            $"Shape-model match is ambiguous; best/second score is {scores[0].D:F3}/{scores[1].D:F3}.");
                    }

                    var metadata = shape.Metadata;
                    var matchAngle = angles[0].D;
                    HOperatorSet.VectorAngleToRigid(
                        metadata.ReferenceRow,
                        metadata.ReferenceColumn,
                        DegreesToRadians(metadata.ReferenceAngleDeg),
                        rows[0].D,
                        columns[0].D,
                        matchAngle,
                        out HTuple followTransform);
                    using (followTransform)
                    {
                        HOperatorSet.AffineTransRegion(
                            staticSearchRegion,
                            out HObject followedRegion,
                            followTransform,
                            "nearest_neighbor");
                        using (followedRegion)
                        {
                            var match = new ShapeMatch(rows[0].D, columns[0].D, matchAngle, scores[0].D);
                            return ExtractBlobs(grayImage, followedRegion, recipe, match);
                        }
                    }
                }
            }
        }
    }

    private static BoardCoarseResult ExtractBlobs(
        HObject grayImage,
        HObject searchRegion,
        BoardVisionRecipe recipe,
        ShapeMatch? match)
    {
        var feature = recipe.HoleFeature;
        var extraction = BlobCandidateExtractor.Extract(
            grayImage,
            searchRegion,
            feature.ThresholdMin,
            feature.ThresholdMax,
            feature.MinimumAreaPx,
            feature.MaximumAreaPx,
            feature.MinimumCircularity);
        var sorted = PointSetGeometry.SortAlongPrincipalAxis(extraction.Points);
        var score = match is null
            ? extraction.MinimumCircularity
            : Math.Min(match.Score, extraction.MinimumCircularity);
        return new BoardCoarseResult(
            sorted,
            score,
            match?.Row ?? sorted.DefaultIfEmpty().Average(point => point.Row),
            match?.Column ?? sorted.DefaultIfEmpty().Average(point => point.Column),
            match is null ? EstimateImageAngle(sorted) : match.AngleRad * 180.0 / Math.PI,
            match is not null,
            extraction.ConnectedCount,
            extraction.MeanAreaPx);
    }

    private static double EstimateImageAngle(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var first = points[0];
        var last = points[^1];
        return Math.Atan2(last.Row - first.Row, last.Column - first.Column) * 180.0 / Math.PI;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180.0;

    private sealed record ShapeMatch(double Row, double Column, double AngleRad, double Score);
}
