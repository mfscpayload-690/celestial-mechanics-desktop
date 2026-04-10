using CelestialMechanics.Math;

namespace CelestialMechanics.Simulation.Placement;

public enum PlacementState
{
    Idle,
    GhostFollow,
    GhostAnchoredVectorEditing,
    PlacementCommitted,
    PlacementCanceled,
}

public sealed class PlacementDraft
{
    public string Category { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;

    public Vec3d GhostPosition { get; set; } = Vec3d.Zero;
    public Vec3d AnchorPosition { get; set; } = Vec3d.Zero;
    public Vec3d DirectionEnd { get; set; } = Vec3d.Zero;
    public Vec3d DirectionNormalized { get; set; } = Vec3d.Zero;
    public double DirectionMagnitude { get; set; }
    public Vec3d InitialVelocity { get; set; } = Vec3d.Zero;

    public List<Vec3d> PreviewTrajectorySamples { get; } = new();

    public PlacementState State { get; set; } = PlacementState.Idle;
    public bool IsValid { get; set; }
    public bool IsCursorInSimulationPanel { get; set; }
}

public sealed class PlacementStateMachine
{
    public PlacementDraft Draft { get; } = new();

    public PlacementState State => Draft.State;

    public void BeginGhostFollow(string category, string templateName, Vec3d ghostPosition, bool cursorInPanel)
    {
        Draft.Category = category;
        Draft.TemplateName = templateName;
        Draft.GhostPosition = ghostPosition;
        Draft.IsCursorInSimulationPanel = cursorInPanel;
        Draft.IsValid = cursorInPanel;
        Draft.State = PlacementState.GhostFollow;
        Draft.PreviewTrajectorySamples.Clear();
    }

    public void UpdateGhostPosition(Vec3d ghostPosition, bool cursorInPanel)
    {
        if (Draft.State != PlacementState.GhostFollow)
            return;

        Draft.GhostPosition = ghostPosition;
        Draft.IsCursorInSimulationPanel = cursorInPanel;
        Draft.IsValid = cursorInPanel;
    }

    public void AnchorAt(Vec3d anchorPosition)
    {
        if (Draft.State != PlacementState.GhostFollow || !Draft.IsValid)
            return;

        Draft.AnchorPosition = anchorPosition;
        Draft.DirectionEnd = anchorPosition;
        Draft.DirectionNormalized = Vec3d.Zero;
        Draft.DirectionMagnitude = 0.0;
        Draft.InitialVelocity = Vec3d.Zero;
        Draft.State = PlacementState.GhostAnchoredVectorEditing;
    }

    public void UpdateDirection(Vec3d cursorWorldPosition, double speedScale, double minSpeed, double maxSpeed)
    {
        if (Draft.State != PlacementState.GhostAnchoredVectorEditing)
            return;

        Draft.DirectionEnd = cursorWorldPosition;
        Draft.InitialVelocity = PlacementMath.MapVelocityFromDrag(
            Draft.AnchorPosition,
            cursorWorldPosition,
            speedScale,
            minSpeed,
            maxSpeed,
            out var direction,
            out var magnitude);

        Draft.DirectionNormalized = direction;
        Draft.DirectionMagnitude = magnitude;
        Draft.IsValid = true;
    }

    public void SetPreviewSamples(IReadOnlyList<Vec3d> samples)
    {
        Draft.PreviewTrajectorySamples.Clear();
        Draft.PreviewTrajectorySamples.AddRange(samples);
    }

    public void Commit()
    {
        if (Draft.State != PlacementState.GhostAnchoredVectorEditing || !Draft.IsValid)
            return;

        Draft.State = PlacementState.PlacementCommitted;
    }

    public void Cancel()
    {
        Draft.State = PlacementState.PlacementCanceled;
        Draft.IsValid = false;
        Draft.PreviewTrajectorySamples.Clear();
    }

    public void Reset()
    {
        Draft.Category = string.Empty;
        Draft.TemplateName = string.Empty;
        Draft.GhostPosition = Vec3d.Zero;
        Draft.AnchorPosition = Vec3d.Zero;
        Draft.DirectionEnd = Vec3d.Zero;
        Draft.DirectionNormalized = Vec3d.Zero;
        Draft.DirectionMagnitude = 0.0;
        Draft.InitialVelocity = Vec3d.Zero;
        Draft.PreviewTrajectorySamples.Clear();
        Draft.State = PlacementState.Idle;
        Draft.IsValid = false;
        Draft.IsCursorInSimulationPanel = false;
    }
}
