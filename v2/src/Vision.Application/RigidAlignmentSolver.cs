using VisionSystem.Domain;

namespace VisionSystem.Application;

/// <summary>
/// Fits q = R*p + t from measured pin points p to measured PCB hole points q,
/// then converts the rigid transform into an XY/T axis command around the real
/// machine T-axis pivot.
/// </summary>
public sealed class RigidAlignmentSolver
{
    public VisionStageResult<AlignmentPlan> Solve(
        IReadOnlyList<IdentifiedMachinePoint> pins,
        IReadOnlyList<IdentifiedMachinePoint> holes,
        MachineKinematicsRecipe kinematics)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(holes);
        ArgumentNullException.ThrowIfNull(kinematics);

        if (!string.Equals(
                kinematics.CommandConvention,
                MachineCommandConventions.RotateAboutPivotThenTranslate,
                StringComparison.Ordinal))
        {
            return CorrespondenceFailure(
                $"Unsupported machine command convention '{kinematics.CommandConvention}'.");
        }

        if (pins.Count < 2 || holes.Count < 2)
        {
            return CorrespondenceFailure(
                $"Rigid alignment requires at least two identified points; pins={pins.Count}, holes={holes.Count}.");
        }

        if (pins.Any(item => string.IsNullOrWhiteSpace(item.Id)) ||
            holes.Any(item => string.IsNullOrWhiteSpace(item.Id)))
        {
            return CorrespondenceFailure("Every pin and hole must have a non-empty correspondence ID.");
        }

        var duplicatePinIds = FindDuplicateIds(pins);
        var duplicateHoleIds = FindDuplicateIds(holes);
        if (duplicatePinIds.Length > 0 || duplicateHoleIds.Length > 0)
        {
            return CorrespondenceFailure(
                $"Correspondence IDs must be unique; duplicate pins=[{string.Join(",", duplicatePinIds)}], " +
                $"duplicate holes=[{string.Join(",", duplicateHoleIds)}].");
        }

        if (pins.Any(item => !IsFinite(item.Point)) || holes.Any(item => !IsFinite(item.Point)) ||
            !IsFinite(kinematics.RotationCenterMachine))
        {
            return CorrespondenceFailure("Alignment points and the T-axis pivot must contain finite coordinates.");
        }

        var pinIds = pins.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var holeIds = holes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (!pinIds.SetEquals(holeIds))
        {
            var missingHoles = pinIds.Except(holeIds, StringComparer.Ordinal).Order(StringComparer.Ordinal);
            var missingPins = holeIds.Except(pinIds, StringComparer.Ordinal).Order(StringComparer.Ordinal);
            return CorrespondenceFailure(
                $"Rigid alignment requires one-to-one IDs; " +
                $"missing hole IDs=[{string.Join(",", missingHoles)}], " +
                $"missing pin IDs=[{string.Join(",", missingPins)}].");
        }

        var holeById = holes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var pairs = pins.Select(pin => (Pin: pin, Hole: holeById[pin.Id])).ToArray();
        if (pairs.Length != pins.Count || pairs.Length != holes.Count)
        {
            return CorrespondenceFailure(
                $"Rigid alignment requires one-to-one IDs; pins={pins.Count}, holes={holes.Count}, matched={pairs.Length}.");
        }

        var pinCenter = new MachinePoint(
            pairs.Average(pair => pair.Pin.Point.Xmm),
            pairs.Average(pair => pair.Pin.Point.Ymm));
        var holeCenter = new MachinePoint(
            pairs.Average(pair => pair.Hole.Point.Xmm),
            pairs.Average(pair => pair.Hole.Point.Ymm));

        double dot = 0;
        double cross = 0;
        double pinEnergy = 0;
        double holeEnergy = 0;
        foreach (var pair in pairs)
        {
            var px = pair.Pin.Point.Xmm - pinCenter.Xmm;
            var py = pair.Pin.Point.Ymm - pinCenter.Ymm;
            var qx = pair.Hole.Point.Xmm - holeCenter.Xmm;
            var qy = pair.Hole.Point.Ymm - holeCenter.Ymm;
            dot += px * qx + py * qy;
            cross += px * qy - py * qx;
            pinEnergy += px * px + py * py;
            holeEnergy += qx * qx + qy * qy;
        }

        const double geometryEpsilon = 1e-12;
        if (pinEnergy < geometryEpsilon || holeEnergy < geometryEpsilon ||
            dot * dot + cross * cross < geometryEpsilon * geometryEpsilon)
        {
            return CorrespondenceFailure(
                "Point geometry is degenerate and cannot determine a stable rigid rotation.");
        }

        var angle = Math.Atan2(cross, dot);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var translation = new MachinePoint(
            holeCenter.Xmm - (cos * pinCenter.Xmm - sin * pinCenter.Ymm),
            holeCenter.Ymm - (sin * pinCenter.Xmm + cos * pinCenter.Ymm));

        var residuals = pairs.Select(pair =>
        {
            var transformed = new MachinePoint(
                cos * pair.Pin.Point.Xmm - sin * pair.Pin.Point.Ymm + translation.Xmm,
                sin * pair.Pin.Point.Xmm + cos * pair.Pin.Point.Ymm + translation.Ymm);
            var error = Math.Sqrt(
                Math.Pow(transformed.Xmm - pair.Hole.Point.Xmm, 2) +
                Math.Pow(transformed.Ymm - pair.Hole.Point.Ymm, 2));
            return new AlignmentResidual(pair.Pin.Id, pair.Hole.Point, transformed, error);
        }).ToArray();

        // The fitted transform is q = R*p + t. The machine command convention is
        // q = R*(p-c) + c + d, where c is the physical T-axis pivot. Therefore:
        // d = t - c + R*c. This is deliberately separate from fit residuals.
        var pivot = kinematics.RotationCenterMachine;
        var rotatedPivot = new MachinePoint(
            cos * pivot.Xmm - sin * pivot.Ymm,
            sin * pivot.Xmm + cos * pivot.Ymm);
        var command = new XYThetaCorrection(
            translation.Xmm - pivot.Xmm + rotatedPivot.Xmm,
            translation.Ymm - pivot.Ymm + rotatedPivot.Ymm,
            Pose2D.NormalizeAngle(angle * 180.0 / Math.PI));
        var rmse = Math.Sqrt(residuals.Average(item => item.ErrorMm * item.ErrorMm));
        var plan = new AlignmentPlan(
            command,
            command.ThetaDeg,
            translation,
            residuals,
            rmse,
            residuals.Max(item => item.ErrorMm));
        return VisionStageResult<AlignmentPlan>.Success(plan, new Dictionary<string, double>
        {
            ["alignment_rmse_mm"] = rmse,
            ["alignment_max_residual_mm"] = plan.MaximumResidualMm,
            ["alignment_pair_count"] = pairs.Length
        });

        static VisionStageResult<AlignmentPlan> CorrespondenceFailure(string message) =>
            VisionStageResult<AlignmentPlan>.Failure(new VisionFailure(
                VisionFailureCode.PointCorrespondenceFailed,
                message,
                false,
                "Alignment",
                VisionFailureImpact.RejectCycle));

        static string[] FindDuplicateIds(IReadOnlyList<IdentifiedMachinePoint> points) => points
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        static bool IsFinite(MachinePoint point) =>
            double.IsFinite(point.Xmm) && double.IsFinite(point.Ymm);
    }
}
