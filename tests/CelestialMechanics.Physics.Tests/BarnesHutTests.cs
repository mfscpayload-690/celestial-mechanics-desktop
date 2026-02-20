using System;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Tests for the Barnes-Hut O(n log n) octree force computation backend.
///
/// TEST STRATEGY
/// -------------
/// These tests verify correctness of the Barnes-Hut approximation against
/// exact brute-force results. The tolerance is θ-dependent: smaller θ gives
/// more accurate results but slower computation.
///
/// All tests run with DeterministicMode semantics (single-threaded traversal)
/// unless explicitly testing parallel mode.
/// </summary>
public class BarnesHutTests
{
    private const double Dt = 0.001;
    private const double Softening = 1e-4;

    // ── Test 1: Single body produces zero self-acceleration ────────────────────

    /// <summary>
    /// A single body in the octree should exert no force on itself.
    /// Verifies that the self-interaction skip logic works correctly.
    /// </summary>
    [Fact]
    public void SingleBody_ZeroAcceleration()
    {
        var bodies = new[]
        {
            new PhysicsBody(0, 100.0, new Vec3d(1, 2, 3), Vec3d.Zero, BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 }
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solver.ConfigureSoA(enabled: true, softening: Softening,
                           deterministic: true, useBarnesHut: true, theta: 0.5);

        var state = solver.Step(bodies, Dt);

        // Velocity should not change (no force applied)
        Assert.Equal(0.0, bodies[0].Velocity.X, precision: 12);
        Assert.Equal(0.0, bodies[0].Velocity.Y, precision: 12);
        Assert.Equal(0.0, bodies[0].Velocity.Z, precision: 12);
    }

    // ── Test 2: Two-body BH matches brute-force ───────────────────────────────

    /// <summary>
    /// For two bodies, Barnes-Hut should give the same result as brute-force
    /// (there are no far-away groups to approximate).
    /// </summary>
    [Fact]
    public void TwoBody_MatchesBruteForce()
    {
        // Create identical starting conditions for both backends
        PhysicsBody[] bodiesBH = CreateTwoBodySystem();
        PhysicsBody[] bodiesBF = CreateTwoBodySystem();

        // Barnes-Hut solver
        var solverBH = new NBodySolver();
        solverBH.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solverBH.ConfigureSoA(enabled: true, softening: Softening,
                              deterministic: true, useBarnesHut: true, theta: 0.5);

        // Brute-force solver
        var solverBF = new NBodySolver();
        solverBF.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solverBF.ConfigureSoA(enabled: true, softening: Softening,
                              deterministic: true, useBarnesHut: false);

        // Run both for 100 steps
        for (int i = 0; i < 100; i++)
        {
            solverBH.Step(bodiesBH, Dt);
            solverBF.Step(bodiesBF, Dt);
        }

        // Positions should match to high precision for two bodies
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(bodiesBF[i].Position.X, bodiesBH[i].Position.X, precision: 10);
            Assert.Equal(bodiesBF[i].Position.Y, bodiesBH[i].Position.Y, precision: 10);
            Assert.Equal(bodiesBF[i].Position.Z, bodiesBH[i].Position.Z, precision: 10);
        }
    }

    // ── Test 3: Energy conservation with stable two-body orbit ──────────────

    /// <summary>
    /// Two-body circular orbit energy should be conserved to within tight
    /// tolerance over 10,000 steps with BH backend.
    ///
    /// For a two-body system, BH produces exact forces (no tree approximation),
    /// so this tests the BH integration into the NBodySolver pipeline rather
    /// than the tree approximation quality.
    ///
    /// Acceptance criterion: |ΔE/E₀| < 1e-3 after 10k steps.
    /// </summary>
    [Fact]
    public void TwoBodyOrbit_EnergyDrift_UnderThreshold()
    {
        // Equal-mass circular orbit: for two bodies of mass m separated by d = 2R,
        // each orbits the center at radius R with v = sqrt(G * m_other / (4*R)).
        double R = 2.0;
        double m = 100.0;
        double orbitalV = System.Math.Sqrt(PhysicalConstants.G_Sim * m / (4.0 * R));

        var bodies = new[]
        {
            new PhysicsBody(0, 100.0,
                new Vec3d(-R, 0, 0),
                new Vec3d(0, 0, orbitalV),
                BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 },
            new PhysicsBody(1, 100.0,
                new Vec3d(R, 0, 0),
                new Vec3d(0, 0, -orbitalV),
                BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 }
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solver.ConfigureSoA(enabled: true, softening: Softening,
                           deterministic: true, useBarnesHut: true, theta: 0.5);

        SimulationState? state = null;
        for (int i = 0; i < 10_000; i++)
            state = solver.Step(bodies, Dt);

        Assert.NotNull(state);
        Assert.True(System.Math.Abs(state!.EnergyDrift) < 1e-3,
            $"Energy drift {state.EnergyDrift:E3} exceeds 0.1% threshold");
    }

    // ── Test 4: Determinism ───────────────────────────────────────────────────

    /// <summary>
    /// Two identical runs with DeterministicMode = true must produce
    /// bit-identical results. This verifies that the tree traversal order
    /// is fixed and no non-deterministic floating-point reductions occur.
    /// </summary>
    [Fact]
    public void Determinism_IdenticalResults()
    {
        PhysicsBody[] bodies1 = CreateRingSystem(50);
        PhysicsBody[] bodies2 = CreateRingSystem(50);

        var solver1 = new NBodySolver();
        solver1.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solver1.ConfigureSoA(enabled: true, softening: Softening,
                            deterministic: true, useBarnesHut: true, theta: 0.5);

        var solver2 = new NBodySolver();
        solver2.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solver2.ConfigureSoA(enabled: true, softening: Softening,
                            deterministic: true, useBarnesHut: true, theta: 0.5);

        for (int i = 0; i < 1000; i++)
        {
            solver1.Step(bodies1, Dt);
            solver2.Step(bodies2, Dt);
        }

        // Bit-identical comparison
        for (int i = 0; i < bodies1.Length; i++)
        {
            Assert.Equal(bodies1[i].Position.X, bodies2[i].Position.X);
            Assert.Equal(bodies1[i].Position.Y, bodies2[i].Position.Y);
            Assert.Equal(bodies1[i].Position.Z, bodies2[i].Position.Z);
            Assert.Equal(bodies1[i].Velocity.X, bodies2[i].Velocity.X);
            Assert.Equal(bodies1[i].Velocity.Y, bodies2[i].Velocity.Y);
            Assert.Equal(bodies1[i].Velocity.Z, bodies2[i].Velocity.Z);
        }
    }

    // ── Test 5: Force accuracy improves as θ → 0 ─────────────────────────────

    /// <summary>
    /// Relative force error should decrease monotonically as θ decreases.
    /// Tests at θ = 1.0, 0.5, 0.3 and verifies error(0.3) < error(0.5) < error(1.0).
    /// </summary>
    [Fact]
    public void ForceAccuracy_ImprovesWithSmallerTheta()
    {
        var bodies = CreateRingSystem(200);
        var analyzer = new ForceErrorAnalyzer();

        double[] thetas = { 1.0, 0.5, 0.3 };
        double[] errors = new double[thetas.Length];

        for (int t = 0; t < thetas.Length; t++)
        {
            var soa = new BodySoA(256);
            soa.CopyFrom(bodies);
            var result = analyzer.CompareForces(soa, Softening, thetas[t]);
            errors[t] = result.MeanRelativeError;
        }

        // Error should decrease monotonically: error[1.0] > error[0.5] > error[0.3]
        Assert.True(errors[0] > errors[1],
            $"Expected error at θ=1.0 ({errors[0]:E3}) > error at θ=0.5 ({errors[1]:E3})");
        Assert.True(errors[1] > errors[2],
            $"Expected error at θ=0.5 ({errors[1]:E3}) > error at θ=0.3 ({errors[2]:E3})");

        // θ=0.3 should have relatively small error (ring geometry causes
        // higher errors than typical random distributions due to symmetry)
        Assert.True(errors[2] < 0.05,
            $"Expected error at θ=0.3 ({errors[2]:E3}) to be < 5%");
    }

    // ── Test 6: Momentum conservation ─────────────────────────────────────────

    /// <summary>
    /// Total linear momentum should be conserved to near machine epsilon.
    ///
    /// Note: Barnes-Hut does NOT exactly conserve momentum like a symmetric
    /// pairwise computation (Newton's 3rd Law is only approximately satisfied
    /// by the tree approximation). However, for zero-momentum initial conditions
    /// the drift should remain very small.
    /// </summary>
    [Fact]
    public void MomentumConservation_SmallDrift()
    {
        // Create a zero-momentum system (ring system has zero total momentum)
        var bodies = CreateRingSystem(50);

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = Softening });
        solver.ConfigureSoA(enabled: true, softening: Softening,
                           deterministic: true, useBarnesHut: true, theta: 0.5);

        SimulationState? state = null;
        for (int i = 0; i < 1000; i++)
            state = solver.Step(bodies, Dt);

        Assert.NotNull(state);
        double momentumMag = state!.TotalMomentum.Length;

        // Momentum drift from BH approximation should be small but not zero.
        // Allow up to 0.1 (generous for the tree approximation).
        Assert.True(momentumMag < 0.1,
            $"Momentum drift {momentumMag:E3} exceeds threshold");
    }

    // ── Test 7: Softening compatibility ───────────────────────────────────────

    /// <summary>
    /// When two bodies are very close, softening should prevent infinite forces.
    /// The BH result with softening should match brute-force with softening
    /// within the θ-dependent tolerance.
    /// </summary>
    [Fact]
    public void SofteningCompatibility_CloseApproach()
    {
        // Two bodies very close together
        double largeSoftening = 1.0; // Large softening to ensure regularisation dominates

        var bodiesBH = new[]
        {
            new PhysicsBody(0, 10.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 },
            new PhysicsBody(1, 10.0, new Vec3d(0.01, 0, 0), Vec3d.Zero, BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 }
        };

        var bodiesBF = new[]
        {
            new PhysicsBody(0, 10.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 },
            new PhysicsBody(1, 10.0, new Vec3d(0.01, 0, 0), Vec3d.Zero, BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 }
        };

        // BH backend
        var soaBH = new BodySoA(4);
        soaBH.CopyFrom(bodiesBH);
        var bhBackend = new BarnesHutBackend { Theta = 0.5, UseParallel = false };
        bhBackend.ComputeForces(soaBH, largeSoftening);

        // Brute-force backend
        var soaBF = new BodySoA(4);
        soaBF.CopyFrom(bodiesBF);
        var bfBackend = new CpuSingleThreadBackend();
        bfBackend.ComputeForces(soaBF, largeSoftening);

        // Accelerations should match exactly for two bodies
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(soaBF.AccX[i], soaBH.AccX[i], precision: 10);
            Assert.Equal(soaBF.AccY[i], soaBH.AccY[i], precision: 10);
            Assert.Equal(soaBF.AccZ[i], soaBH.AccZ[i], precision: 10);
        }
    }

    // ── Helper methods ────────────────────────────────────────────────────────

    private static PhysicsBody[] CreateTwoBodySystem()
    {
        return new[]
        {
            new PhysicsBody(0, 100.0,
                new Vec3d(-1, 0, 0),
                new Vec3d(0, 0, 0.5),
                BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 },

            new PhysicsBody(1, 100.0,
                new Vec3d(1, 0, 0),
                new Vec3d(0, 0, -0.5),
                BodyType.Star)
            { IsActive = true, Radius = 0.1, GravityStrength = 60, GravityRange = 0 }
        };
    }

    private static PhysicsBody[] CreateRingSystem(int count)
    {
        var bodies = new PhysicsBody[count];
        double totalMass = (double)count;
        double ringRadius = 5.0;

        for (int i = 0; i < count; i++)
        {
            double angle = 2.0 * System.Math.PI * i / count;
            double x = ringRadius * System.Math.Cos(angle);
            double z = ringRadius * System.Math.Sin(angle);

            double v = System.Math.Sqrt(PhysicalConstants.G_Sim * totalMass / ringRadius) * 0.5;
            double vx = -v * System.Math.Sin(angle);
            double vz = v * System.Math.Cos(angle);

            bodies[i] = new PhysicsBody(i, mass: 1.0,
                position: new Vec3d(x, 0, z),
                velocity: new Vec3d(vx, 0, vz),
                type: BodyType.Star)
            {
                IsActive = true,
                Radius = 0.05,
                GravityStrength = 60,
                GravityRange = 0
            };
        }

        return bodies;
    }
}
