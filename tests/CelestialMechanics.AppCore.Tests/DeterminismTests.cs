using AppScene = CelestialMechanics.AppCore.Scene.Scene;
using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.AppCore.Validation;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Tests;

public sealed class DeterminismTests
{
    // ── Deterministic mode: strict zero drift ─────────────────────────────────

    [Fact]
    public void Validate_DeterministicSim_PassesWithNearZeroDrift()
    {
        // Single body with no interaction → fixed trajectory, perfect determinism
        var manager = BuildSingleBodyManager();
        var scene   = new AppScene("DeterminismTest");

        var validator = new DeterminismValidator();
        var result    = validator.Validate(manager, scene, steps: 100, epsilon: 1e-6, baseDt: 0.001);

        Assert.True(result.Passed, $"Expected PASS.\n{result.Log}");
        Assert.True(result.MaxPositionDrift < 1e-6,
            $"Position drift {result.MaxPositionDrift:E3} exceeded epsilon.");
    }

    // ── Two-body determinism ──────────────────────────────────────────────────

    [Fact]
    public void Validate_TwoBodySystem_EnergyDriftBelowEpsilon()
    {
        var manager = BuildTwoBodyManager();
        var scene   = new AppScene("TwoBodyDeterminism");

        var result = new DeterminismValidator().Validate(
            manager, scene, steps: 50, epsilon: 1e-3, baseDt: 0.001);

        Assert.True(result.Passed, $"Two-body drift check failed.\n{result.Log}");
        Assert.True(result.EnergyDrift <= result.Epsilon * System.Math.Max(1.0, System.Math.Abs(result.EnergyDrift + 1)),
            $"Energy drift {result.EnergyDrift:E3} out of tolerance.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimulationManager BuildSingleBodyManager()
    {
        var config = new PhysicsConfig
        {
            DeterministicMode = true,
            UseSoAPath        = false,  // use simple AoS path for test clarity
            EnableCollisions  = false,
        };
        var manager = new SimulationManager(config);

        var body = new Entity();
        body.AddComponent(new PhysicsComponent(
            mass:     1.0,
            position: new Vec3d(0, 0, 0),
            velocity: new Vec3d(1.0, 0, 0),
            radius:   0.1));
        manager.AddEntity(body);
        manager.FlushPendingChanges();
        return manager;
    }

    private static SimulationManager BuildTwoBodyManager()
    {
        var config = new PhysicsConfig
        {
            DeterministicMode = true,
            UseSoAPath        = false,
            EnableCollisions  = false,
        };
        var manager = new SimulationManager(config);

        // Two equal masses in opposite positions
        var b1 = new Entity();
        b1.AddComponent(new PhysicsComponent(1.0, new Vec3d(-5, 0, 0), new Vec3d(0, 0.1, 0), 0.1));

        var b2 = new Entity();
        b2.AddComponent(new PhysicsComponent(1.0, new Vec3d(5, 0, 0), new Vec3d(0, -0.1, 0), 0.1));

        manager.AddEntity(b1);
        manager.AddEntity(b2);
        manager.FlushPendingChanges();
        return manager;
    }
}
