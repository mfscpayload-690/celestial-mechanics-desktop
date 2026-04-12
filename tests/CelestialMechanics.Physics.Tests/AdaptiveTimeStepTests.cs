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
        engine.Update(0.006);

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

    // ── Test 5: Collision limiter shrinks dt for very fast bodies ───────────

    [Fact]
    public void AdaptiveMode_CollisionLimiterReducesDtForFastBodies()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = false,
            UseAdaptiveTimestep = true,
            EnableCollisions = true,
            MinDt = 1e-6,
            MaxDt = 0.01,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, new Vec3d(-10, 0, 0), new Vec3d(100, 0, 0), BodyType.Asteroid) { Radius = 0.1 },
            new PhysicsBody(1, 1.0, new Vec3d(10, 0, 0), new Vec3d(-100, 0, 0), BodyType.Asteroid) { Radius = 0.1 },
        });

        engine.Start();
        engine.Update(0.02);

        Assert.True(engine.CurrentState.CurrentDt < 0.01,
            $"Expected collision limiter to reduce dt below 1e-2, got {engine.CurrentState.CurrentDt}");
    }

    // ── Test 6: MaxSubstepsPerFrame bounds physics catch-up work ────────────

    [Fact]
    public void Update_RespectsMaxSubstepsPerFrame()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = true,
            UseAdaptiveTimestep = false,
            MaxSubstepsPerFrame = 2,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.Update(1.0);

        Assert.True(engine.CurrentTime <= 0.0200001,
            $"CurrentTime {engine.CurrentTime} exceeded expected cap for 2 substeps");
    }

    // ── Test 7: Tiny compact-object radii should not freeze interactive dt ──

    [Fact]
    public void AdaptiveMode_DoesNotCollapseDtWhenCompactObjectPresent()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.001,
            DeterministicMode = false,
            UseAdaptiveTimestep = true,
            EnableCollisions = true,
            SofteningEpsilon = 1e-4,
            MinDt = 1e-6,
            MaxDt = 0.01,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, new Vec3d(1, 0, 0), new Vec3d(0, 0, 5), BodyType.Star) { Radius = 0.05 },
            new PhysicsBody(1, 1.0, new Vec3d(-1, 0, 0), new Vec3d(0, 0, -5), BodyType.Star) { Radius = 0.05 },
            new PhysicsBody(2, 20.0, Vec3d.Zero, Vec3d.Zero, BodyType.BlackHole) { Radius = 5.8e-7 },
        });

        engine.Start();
        engine.Update(0.01);

        // Regression guard: compact radii should not force dt close to MinDt.
        Assert.True(engine.CurrentState.CurrentDt > 1e-5,
            $"Expected effective dt to remain interactive, got {engine.CurrentState.CurrentDt}");
    }

    // ── Test 8: Time flow substep boost increases catch-up capacity ─────────

    [Fact]
    public void TimeFlowSubstepBoost_AllowsMoreCatchup()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = true,
            UseAdaptiveTimestep = false,
            MaxSubstepsPerFrame = 2,
            TimeFlowSubstepBoost = 4.0,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.Update(1.0);

        Assert.True(engine.CurrentTime > 0.0200001,
            $"Expected boosted catch-up beyond base cap, got {engine.CurrentTime}");
        Assert.True(engine.CurrentTime <= 0.0800001,
            $"Boosted catch-up should still be bounded by boosted cap, got {engine.CurrentTime}");
    }

    // ── Test 9: StepOnce pauses running engine before stepping ──────────────

    [Fact]
    public void StepOnce_PausesRunningEngineBeforeStep()
    {
        var config = new PhysicsConfig
        {
            TimeStep = 0.01,
            DeterministicMode = true,
            UseAdaptiveTimestep = false,
            UseSoAPath = true,
        };

        var engine = new SimulationEngine(config);
        engine.SetBodies(new[]
        {
            new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star),
        });

        engine.Start();
        engine.StepOnce();

        Assert.Equal(EngineState.Paused, engine.State);
        Assert.True(engine.CurrentTime > 0.0, "StepOnce should advance simulation time by one step.");
    }
}
