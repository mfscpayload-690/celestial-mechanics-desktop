using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation;
using CelestialMechanics.Simulation.Analysis;

namespace CelestialMechanics.Simulation.Tests;

public class EventAdvisorTests
{
    [Fact]
    public void Build_ReturnsCriticalMergeAdvice_ForCollisionImminent()
    {
        var advisory = EventAdvisor.Build(
            activeEventWarning: "Collision Imminent",
            orbitType: OrbitType.Elliptical,
            stabilityLabel: "Stable",
            collisionMode: CollisionMode.MergeOnly);

        Assert.Equal(InsightSeverity.Critical, advisory.Severity);
        Assert.Contains("Elastic", advisory.SuggestedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ReturnsWarning_ForEscapeTrajectory()
    {
        var advisory = EventAdvisor.Build(
            activeEventWarning: "Escape Trajectory",
            orbitType: OrbitType.Hyperbolic,
            stabilityLabel: "Stable",
            collisionMode: CollisionMode.Realistic);

        Assert.Equal(InsightSeverity.Warning, advisory.Severity);
        Assert.Contains("retrograde", advisory.SuggestedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReturnsWarning_ForUnstableWithoutEvent()
    {
        var advisory = EventAdvisor.Build(
            activeEventWarning: null,
            orbitType: OrbitType.Elliptical,
            stabilityLabel: "Unstable",
            collisionMode: CollisionMode.BounceOnly);

        Assert.Equal(InsightSeverity.Warning, advisory.Severity);
        Assert.Contains("timestep", advisory.SuggestedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReturnsInfo_ForStableNominalCase()
    {
        var advisory = EventAdvisor.Build(
            activeEventWarning: null,
            orbitType: OrbitType.Circular,
            stabilityLabel: "Stable",
            collisionMode: CollisionMode.Realistic);

        Assert.Equal(InsightSeverity.Info, advisory.Severity);
        Assert.Contains("Maintain", advisory.SuggestedAction, StringComparison.OrdinalIgnoreCase);
    }
}