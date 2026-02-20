using CelestialMechanics.Physics.Collisions;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Tests for collision detection, merge resolution, and conservation laws.
/// </summary>
public class CollisionMergeTests
{
    private static PhysicsBody MakeBody(int id, double mass, Vec3d pos, Vec3d vel,
        BodyType type = BodyType.Star, double radius = -1.0)
    {
        var b = new PhysicsBody(id, mass, pos, vel, type);
        if (radius > 0.0) b.Radius = radius;
        return b;
    }

    // ── Test 1: Two overlapping spheres detected ─────────────────────────────

    [Fact]
    public void Detect_TwoOverlappingSpheres_ReturnsOneCollision()
    {
        var bodies = new[]
        {
            MakeBody(0, 10.0, new Vec3d(0, 0, 0), Vec3d.Zero, radius: 1.0),
            MakeBody(1, 10.0, new Vec3d(1.5, 0, 0), Vec3d.Zero, radius: 1.0),
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        var detector = new CollisionDetector();
        var events = detector.Detect(soa);

        Assert.Single(events);
        Assert.True(events[0].OverlapDepth > 0.0);
    }

    // ── Test 2: No collision when spheres don't overlap ──────────────────────

    [Fact]
    public void Detect_SeparateSpheres_ReturnsNoCollisions()
    {
        var bodies = new[]
        {
            MakeBody(0, 10.0, new Vec3d(0, 0, 0), Vec3d.Zero, radius: 1.0),
            MakeBody(1, 10.0, new Vec3d(5.0, 0, 0), Vec3d.Zero, radius: 1.0),
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        var detector = new CollisionDetector();
        var events = detector.Detect(soa);

        Assert.Empty(events);
    }

    // ── Test 3: Inactive bodies are ignored ──────────────────────────────────

    [Fact]
    public void Detect_InactiveBody_NotDetected()
    {
        var bodies = new[]
        {
            MakeBody(0, 10.0, new Vec3d(0, 0, 0), Vec3d.Zero, radius: 1.0),
            MakeBody(1, 10.0, new Vec3d(1.5, 0, 0), Vec3d.Zero, radius: 1.0),
        };
        bodies[1].IsActive = false;

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        var detector = new CollisionDetector();
        var events = detector.Detect(soa);

        Assert.Empty(events);
    }

    // ── Test 4: Merge marks lighter body inactive ────────────────────────────

    [Fact]
    public void Merge_LighterBodyBecomesInactive()
    {
        var bodies = new[]
        {
            MakeBody(0, 100.0, new Vec3d(0, 0, 0), Vec3d.Zero, radius: 1.0),
            MakeBody(1, 10.0, new Vec3d(1.5, 0, 0), Vec3d.Zero, radius: 1.0),
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        Assert.False(soa.IsActive[1], "Lighter body should be inactive after merge");
        Assert.True(soa.IsActive[0], "Heavier body should remain active");
    }

    // ── Test 5: Black hole absorption ────────────────────────────────────────

    [Fact]
    public void Merge_BlackHoleAbsorbs_TypePromoted()
    {
        var bodies = new[]
        {
            MakeBody(0, 100.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star, radius: 1.0),
            MakeBody(1, 50.0, new Vec3d(1.0, 0, 0), Vec3d.Zero, BodyType.BlackHole, radius: 1.0),
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        // Body 0 is heavier but body 1 is a BH
        MergePolicy.Merge(soa, 0, 1);

        Assert.Equal((int)BodyType.BlackHole, soa.BodyTypeIndex[0]);
        Assert.Equal(150.0, soa.Mass[0], 1e-10);
    }

    // ── Test 6: Full pipeline via NBodySolver with collisions enabled ────────

    [Fact]
    public void Solver_WithCollisions_MergesOverlappingBodies()
    {
        var bodies = new[]
        {
            MakeBody(0, 100.0, new Vec3d(0, 0, 0), Vec3d.Zero, radius: 1.0),
            MakeBody(1, 50.0, new Vec3d(1.5, 0, 0), Vec3d.Zero, radius: 1.0),
        };

        var solver = new CelestialMechanics.Physics.Solvers.NBodySolver();
        solver.AddForce(new CelestialMechanics.Physics.Forces.NewtonianGravity());
        solver.ConfigureSoA(enabled: true, softening: 0.01,
            deterministic: true, enableCollisions: true);

        var state = solver.Step(bodies, 0.001);

        // After merge, one body should be inactive
        Assert.Equal(1, state.CollisionCount);
        Assert.Equal(1, state.ActiveBodyCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests that total mass is conserved across collision merges.
/// </summary>
public class MassConservationAfterMergeTests
{
    // ── Test 1: Two-body merge conserves mass ────────────────────────────────

    [Fact]
    public void TwoBodyMerge_TotalMassConserved()
    {
        double m1 = 100.0, m2 = 50.0;
        var bodies = new[]
        {
            new PhysicsBody(0, m1, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m2, new Vec3d(1.0, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        Assert.Equal(m1 + m2, soa.Mass[0], 1e-12);
    }

    // ── Test 2: Three-body chain merge conserves mass ────────────────────────

    [Fact]
    public void ThreeBodyChainMerge_TotalMassConserved()
    {
        double m1 = 100.0, m2 = 50.0, m3 = 25.0;
        double totalMass = m1 + m2 + m3;

        var bodies = new[]
        {
            new PhysicsBody(0, m1, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m2, new Vec3d(0.5, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(2, m3, new Vec3d(0.8, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        // Merge in sequence
        MergePolicy.Merge(soa, 0, 1);
        MergePolicy.Merge(soa, 0, 2);

        Assert.Equal(totalMass, soa.Mass[0], 1e-12);
    }

    // ── Test 3: All mass should be in active bodies after merge ──────────────

    [Fact]
    public void AfterMerge_InactiveBodiesHaveOriginalMass_ActiveHasTotal()
    {
        double m1 = 100.0, m2 = 50.0;

        var bodies = new[]
        {
            new PhysicsBody(0, m1, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m2, new Vec3d(1.0, 0, 0), Vec3d.Zero, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        Assert.True(soa.IsActive[0]);
        Assert.False(soa.IsActive[1]);
        Assert.Equal(m1 + m2, soa.Mass[0], 1e-12);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests that linear momentum is conserved across collision merges.
/// </summary>
public class MomentumAfterMergeTests
{
    // ── Test 1: Head-on collision conserves momentum ─────────────────────────

    [Fact]
    public void HeadOnCollision_MomentumConserved()
    {
        double m1 = 100.0, m2 = 50.0;
        var v1 = new Vec3d(1.0, 0, 0);
        var v2 = new Vec3d(-2.0, 0, 0);

        Vec3d totalP_before = v1 * m1 + v2 * m2;

        var bodies = new[]
        {
            new PhysicsBody(0, m1, new Vec3d(0, 0, 0), v1, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m2, new Vec3d(1.0, 0, 0), v2, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        double mTotal = soa.Mass[0];
        Vec3d v_merged = new Vec3d(soa.VelX[0], soa.VelY[0], soa.VelZ[0]);
        Vec3d totalP_after = v_merged * mTotal;

        Assert.True(System.Math.Abs(totalP_before.X - totalP_after.X) < 1e-10,
            $"X momentum: before={totalP_before.X}, after={totalP_after.X}");
        Assert.True(System.Math.Abs(totalP_before.Y - totalP_after.Y) < 1e-10);
        Assert.True(System.Math.Abs(totalP_before.Z - totalP_after.Z) < 1e-10);
    }

    // ── Test 2: 3D velocity merge conserves all components ──────────────────

    [Fact]
    public void ThreeDimensionalMerge_AllComponentsConserved()
    {
        double m1 = 75.0, m2 = 25.0;
        var v1 = new Vec3d(1.0, 2.0, -3.0);
        var v2 = new Vec3d(-4.0, 5.0, 6.0);

        Vec3d totalP_before = v1 * m1 + v2 * m2;

        var bodies = new[]
        {
            new PhysicsBody(0, m1, Vec3d.Zero, v1, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m2, new Vec3d(0.5, 0, 0), v2, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        double mTotal = soa.Mass[0];
        Vec3d v_merged = new Vec3d(soa.VelX[0], soa.VelY[0], soa.VelZ[0]);
        Vec3d totalP_after = v_merged * mTotal;

        double err = System.Math.Sqrt(
            (totalP_before.X - totalP_after.X) * (totalP_before.X - totalP_after.X) +
            (totalP_before.Y - totalP_after.Y) * (totalP_before.Y - totalP_after.Y) +
            (totalP_before.Z - totalP_after.Z) * (totalP_before.Z - totalP_after.Z));

        Assert.True(err < 1e-10, $"Momentum error: {err:E3}");
    }

    // ── Test 3: Equal mass head-on → zero momentum result ───────────────────

    [Fact]
    public void EqualMass_OppositeVelocity_ResultsInZeroMomentum()
    {
        double m = 50.0;
        var v1 = new Vec3d(5.0, 0, 0);
        var v2 = new Vec3d(-5.0, 0, 0);

        var bodies = new[]
        {
            new PhysicsBody(0, m, new Vec3d(-0.5, 0, 0), v1, BodyType.Star) { Radius = 1.0 },
            new PhysicsBody(1, m, new Vec3d(0.5, 0, 0), v2, BodyType.Star) { Radius = 1.0 },
        };

        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        MergePolicy.Merge(soa, 0, 1);

        Assert.True(System.Math.Abs(soa.VelX[0]) < 1e-12, "X velocity should be zero");
        Assert.True(System.Math.Abs(soa.VelY[0]) < 1e-12, "Y velocity should be zero");
        Assert.True(System.Math.Abs(soa.VelZ[0]) < 1e-12, "Z velocity should be zero");
    }
}
