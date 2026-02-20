using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Numerical stability validation for the SoA execution path (Step 3 of the
/// Physics Core upgrade).
///
/// PURPOSE
/// -------
/// After migrating force computation to the SoA layout + backend pipeline, we
/// must verify that:
///   1. Energy conservation is maintained at or below the AoS baseline.
///   2. Momentum conservation is maintained to floating-point precision.
///   3. The parallel backend produces the same stability class of results as
///      the single-thread backend (energy drift within the same order of
///      magnitude, though bit results may differ due to FP reordering).
///
/// EXPECTED DRIFT VALUES (two-body circular orbit, dt=0.001, 10 000 steps)
/// -------------------------------------------------------------------------
///   AoS Verlet (baseline)    :  |ΔE/E₀|  < 0.01%  (< 1 × 10⁻⁴)
///   SoA single-thread Verlet :  |ΔE/E₀|  < 0.01%  (< 1 × 10⁻⁴)
///   SoA parallel Verlet      :  |ΔE/E₀|  < 0.01%  (< 1 × 10⁻⁴)
///
/// Both SoA backends implement mathematically identical algorithms to the AoS
/// Verlet. Any difference is purely from floating-point rounding order and is
/// below single-precision noise.
/// </summary>
public class SoAStabilityTests
{
    // ── Setup helpers ──────────────────────────────────────────────────────────

    private static PhysicsBody MakeBody(int id, double mass, Vec3d pos, Vec3d vel) =>
        new PhysicsBody(id, mass, pos, vel, BodyType.Star)
        {
            IsActive        = true,
            GravityStrength = 60,
            GravityRange    = 0,
            Radius          = 0.05,
        };

    /// <summary>
    /// Two-body circular orbit: both bodies at distance r from the origin,
    /// orbiting their common centre of mass.  GravityRange = 0 means infinite
    /// range; softening = 0 means pure Newtonian (no Plummer kernel).
    /// </summary>
    private static PhysicsBody[] TwoBodyOrbit()
    {
        double mass = 1.0;
        double r    = 1.0;
        double v    = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        return new[]
        {
            MakeBody(0, mass, new Vec3d( r, 0, 0), new Vec3d(0, 0,  v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };
    }

    // ── Step 3: SoA single-thread ──────────────────────────────────────────────

    [Fact]
    public void SoAVerlet_SingleThread_TwoBodyOrbit_EnergyDriftBelow001Percent()
    {
        var bodies = TwoBodyOrbit();
        var solver = new NBodySolver();

        // Force calculator needed for energy diagnostics (PE = -G·m₁m₂/r).
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });

        // Enable SoA path, deterministic (single-thread), no softening —
        // matches the AoS baseline test in IntegratorTests exactly.
        solver.ConfigureSoA(enabled: true, softening: 0,
                            deterministic: true, useParallel: false);

        SimulationState? state = null;
        for (int i = 0; i < 10_000; i++)
            state = solver.Step(bodies, 0.001);

        Assert.NotNull(state);
        double drift = System.Math.Abs(state.EnergyDrift);
        Assert.True(drift < 0.0001,
            $"SoA single-thread energy drift {drift:E4} exceeds 0.01% " +
            $"(same threshold as AoS Verlet baseline)");
    }

    [Fact]
    public void SoAVerlet_SingleThread_TwoBodyOrbit_BodiesRemainBound()
    {
        var bodies = TwoBodyOrbit();
        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });
        solver.ConfigureSoA(enabled: true, softening: 0,
                            deterministic: true, useParallel: false);

        for (int i = 0; i < 10_000; i++)
            solver.Step(bodies, 0.001);

        double separation = bodies[0].Position.DistanceTo(bodies[1].Position);
        Assert.True(separation < 10.0,
            $"SoA: Bodies separated to {separation} AU — orbit is unbound");
        Assert.True(separation > 0.1,
            $"SoA: Bodies collapsed to {separation} AU — unrealistic");
    }

    // ── Step 3: SoA parallel ───────────────────────────────────────────────────

    [Fact]
    public void SoAVerlet_Parallel_TwoBodyOrbit_EnergyDriftBelow001Percent()
    {
        var bodies = TwoBodyOrbit();
        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });

        // Parallel backend: non-deterministic FP order, but same numerical
        // quality as single-thread.
        solver.ConfigureSoA(enabled: true, softening: 0,
                            deterministic: false, useParallel: true);

        SimulationState? state = null;
        for (int i = 0; i < 10_000; i++)
            state = solver.Step(bodies, 0.001);

        Assert.NotNull(state);
        double drift = System.Math.Abs(state.EnergyDrift);
        Assert.True(drift < 0.0001,
            $"SoA parallel energy drift {drift:E4} exceeds 0.01%");
    }

    // ── Step 3: Momentum conservation ─────────────────────────────────────────

    [Fact]
    public void SoAVerlet_ThreeBody_MomentumConserved()
    {
        var bodies = new[]
        {
            MakeBody(0, 1.0, new Vec3d(2.0, 0, 0), new Vec3d(0, 0.3, 0)),
            MakeBody(1, 1.5, new Vec3d(-1.0, 1.0, 0), new Vec3d(-0.2, -0.1, 0)),
            MakeBody(2, 0.8, new Vec3d(0, -2.0, 0), new Vec3d(0.1, 0, 0.1)),
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solver.ConfigureSoA(enabled: true, softening: 1e-4,
                            deterministic: true, useParallel: false);

        // Record initial momentum from bodies directly.
        static Vec3d TotalMomentum(PhysicsBody[] bs) =>
            bs.Aggregate(Vec3d.Zero, (acc, b) => b.IsActive ? acc + b.Velocity * b.Mass : acc);

        Vec3d initialMomentum = TotalMomentum(bodies);

        SimulationState? state = null;
        for (int i = 0; i < 10_000; i++)
            state = solver.Step(bodies, 0.001);

        Assert.NotNull(state);

        // Momentum should be conserved to near machine-epsilon times number of steps.
        Vec3d finalMomentum = TotalMomentum(bodies);
        double momentumDrift =
            (finalMomentum - initialMomentum).Length / (initialMomentum.Length + 1e-30);

        Assert.True(momentumDrift < 1e-8,
            $"SoA three-body momentum drift {momentumDrift:E4} exceeds 1e-8");
    }

    // ── Step 3: AoS vs SoA drift comparison ───────────────────────────────────

    [Fact]
    public void SoAVerlet_EnergyDrift_ComparableToAoSBaseline()
    {
        const int steps = 10_000;
        const double dt = 0.001;

        // ── AoS baseline ──────────────────────────────────────────────────────
        var aosBodies = TwoBodyOrbit();
        var energy    = new EnergyCalculator();
        var forces    = new IForceCalculator[] { new NewtonianGravity { SofteningEpsilon = 0 } };

        double aosInitialE = energy.ComputeKE(aosBodies) + energy.ComputePE(aosBodies, forces);
        var aosIntegrator  = new VerletIntegrator();
        for (int i = 0; i < steps; i++)
            aosIntegrator.Step(aosBodies, dt, forces);
        double aosFinalE  = energy.ComputeKE(aosBodies) + energy.ComputePE(aosBodies, forces);
        double aosDrift   = System.Math.Abs((aosFinalE - aosInitialE) / aosInitialE);

        // ── SoA path ──────────────────────────────────────────────────────────
        var soaBodies = TwoBodyOrbit();
        var soaSolver = new NBodySolver();
        soaSolver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });
        soaSolver.ConfigureSoA(enabled: true, softening: 0,
                               deterministic: true, useParallel: false);

        SimulationState? soaState = null;
        for (int i = 0; i < steps; i++)
            soaState = soaSolver.Step(soaBodies, dt);

        Assert.NotNull(soaState);
        double soaDrift = System.Math.Abs(soaState.EnergyDrift);

        // Both paths should be < 0.01%.
        Assert.True(aosDrift  < 0.0001, $"AoS  drift {aosDrift:E4} exceeds 0.01%");
        Assert.True(soaDrift  < 0.0001, $"SoA  drift {soaDrift:E4} exceeds 0.01%");

        // SoA drift should be within 100x of AoS drift (typically within 10x).
        // This guards against regressions where refactoring accidentally
        // introduces a factor-of-2 timestep error or similar logic bug.
        Assert.True(soaDrift < aosDrift * 100 + 1e-12,
            $"SoA drift ({soaDrift:E4}) is more than 100x the AoS baseline ({aosDrift:E4}) — " +
            $"possible integration logic error in the SoA path");
    }
}
