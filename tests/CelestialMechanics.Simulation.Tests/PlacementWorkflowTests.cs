using CelestialMechanics.Data;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Placement;

namespace CelestialMechanics.Simulation.Tests;

public class PlacementWorkflowTests
{
    [Fact]
    public void StateMachine_Transitions_ThroughAnchorAndCommit()
    {
        var sm = new PlacementStateMachine();

        sm.BeginGhostFollow("Stars", "Sun", new Vec3d(1, 0, 1), cursorInPanel: true);
        Assert.Equal(PlacementState.GhostFollow, sm.State);

        sm.AnchorAt(new Vec3d(1, 0, 1));
        Assert.Equal(PlacementState.GhostAnchoredVectorEditing, sm.State);

        sm.UpdateDirection(new Vec3d(2, 0, 1), speedScale: 1.0, minSpeed: 0.0, maxSpeed: 10.0);
        Assert.True(sm.Draft.InitialVelocity.Length > 0.0);

        sm.Commit();
        Assert.Equal(PlacementState.PlacementCommitted, sm.State);
    }

    [Fact]
    public void StateMachine_Cancel_AndReset_Workflow()
    {
        var sm = new PlacementStateMachine();
        sm.BeginGhostFollow("Small Bodies", "Asteroid", Vec3d.Zero, cursorInPanel: true);

        sm.Cancel();
        Assert.Equal(PlacementState.PlacementCanceled, sm.State);

        sm.Reset();
        Assert.Equal(PlacementState.Idle, sm.State);
        Assert.False(sm.Draft.IsValid);
        Assert.Empty(sm.Draft.PreviewTrajectorySamples);
    }

    [Fact]
    public void VelocityMapping_RespectsDirectionAndClamp()
    {
        var anchor = new Vec3d(0, 0, 0);
        var drag = new Vec3d(3, 0, 4); // length 5

        var velocity = PlacementMath.MapVelocityFromDrag(
            anchor,
            drag,
            speedScale: 0.5,
            minSpeed: 0.1,
            maxSpeed: 2.0,
            out var direction,
            out var speed);

        Assert.Equal(2.0, speed, 10); // clamped from 2.5 to 2.0
        Assert.Equal(1.0, direction.Length, 10);
        Assert.Equal(2.0, velocity.Length, 10);
    }

    [Fact]
    public void GravityPreview_CurvesNearMassiveBody()
    {
        var start = new Vec3d(-2, 0, 0);
        var v0 = new Vec3d(0, 0, 0.8);

        var bodies = new List<PhysicsBody>
        {
            new PhysicsBody(1, 5.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star)
            {
                IsActive = true
            }
        };

        var preview = PlacementMath.BuildGravityAwarePreview(start, v0, bodies, steps: 32, dt: 0.03);

        Assert.True(preview.Count > 4);

        // Without gravity this would remain near x=-2. Ensure x bends toward origin.
        double minAbsX = preview.Min(p => System.Math.Abs(p.X));
        Assert.True(minAbsX < 1.9);
    }

    [Fact]
    public void Catalog_HasCategories_AndTemplateLookup()
    {
        Assert.NotEmpty(CelestialCatalog.Categories);
        Assert.Contains(CelestialCatalog.Categories, c => c.Name == "Stars");
        Assert.Contains(CelestialCatalog.Categories, c => c.Templates.Count > 0);

        Assert.True(CelestialCatalog.TryGetTemplate("Sun", out var sun));
        Assert.Equal("Star", sun.BodyType);
    }
}
