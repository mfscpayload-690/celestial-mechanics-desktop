using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using CelestialMechanics.Simulation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Tests for adaptive timestep control in SimulationEngine.
/// </summary>
public class AdaptiveTimeStepTests
{
    // ── Test 1: Deterministic mode always uses fixed dt ──────────────────────

    [Fact]
    public void DeterministicMode_AlwaysUsesFixedDt()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.001,
            DeterministicMode = true,
            UseAdaptiveTimestep = true, // Enabled but overridden by deterministic
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);

        // Two close bodies with high acceleration
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1000.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star),
            new PhysicsBody(1, 1000.0, new Vec3d(0.1, 0, 0), Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.Update(0.01); // 10 steps worth

        // In deterministic mode, dt should always be the fixed value
        Assert.Equal(0.001, engine.CurrentState.CurrentDt, 1e-15);
    }

    // ── Test 2: Adaptive dt reduces under high acceleration ──────────────────

    [Fact]
    public void AdaptiveMode_ReducesDtUnderHighAcceleration()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = false,
            UseAdaptiveTimestep = true,
            MinDt = 1e-6,
            MaxDt = 0.01,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);

        // Two massive bodies at moderate distance. Set large initial
        // acceleration manually so ComputeDt reduces dt on the first call.
        var b0 = new PhysicsBody(0, 1000.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star);
        var b1 = new PhysicsBody(1, 1000.0, new Vec3d(5.0, 0, 0), Vec3d.Zero, BodyType.Star);
        b0.Acceleration = new Vec3d(1e6, 0, 0);  // pre-set high acc
        b1.Acceleration = new Vec3d(-1e6, 0, 0);

        engine.SetBodies(new[] { b0, b1 });
        engine.Start();
        engine.Update(0.05);

        // dt should be reduced below MaxDt due to high acceleration
        Assert.True(engine.CurrentState.CurrentDt < config.MaxDt,
            $"Expected dt < {config.MaxDt}, got {engine.CurrentState.CurrentDt}");
        Assert.True(engine.CurrentState.CurrentDt >= config.MinDt,
            $"dt should be >= MinDt, got {engine.CurrentState.CurrentDt}");
    }

    // ── Test 3: MaxDt used when acceleration is negligible ───────────────────

    [Fact]
    public void AdaptiveMode_UsesMaxDtWhenLowAcceleration()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01, // This is the fixed dt, also = MaxDt
            DeterministicMode = false,
            UseAdaptiveTimestep = true,
            MinDt = 1e-6,
            MaxDt = 0.01,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);

        // Single body — no gravitational interaction, zero acceleration
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.Update(0.05);

        // With zero/negligible acceleration, dt should be MaxDt
        Assert.Equal(config.MaxDt, engine.CurrentState.CurrentDt, 1e-12);
    }

    // ── Test 4: dt clamped at MinDt even with extreme acceleration ───────────

    [Fact]
    public void AdaptiveMode_NeverGoesBelowMinDt()
    {
        double minDt = 0.0001;
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = false,
            UseAdaptiveTimestep = true,
            MinDt = minDt,
            MaxDt = 0.01,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);

        // Extreme close encounter
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1e6, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star),
            new PhysicsBody(1, 1e6, new Vec3d(1e-4, 0, 0), Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.Update(0.1);

        Assert.True(engine.CurrentState.CurrentDt >= minDt,
            $"dt {engine.CurrentState.CurrentDt} should be >= MinDt {minDt}");
    }
}
