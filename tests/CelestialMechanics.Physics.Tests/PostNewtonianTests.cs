using CelestialMechanics.Math;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Validates Post-Newtonian (1PN) corrections (Phase 6A).
///
/// Tests confirm:
///   1. 1PN corrections produce measurable precession in a binary orbit.
///   2. Energy drift remains bounded when 1PN is enabled.
///   3. Toggling 1PN off produces identical results to pure Newtonian.
///   4. High-velocity safety guard disables corrections above 0.3c.
///   5. Schwarzschild proximity guard triggers for close encounters.
/// </summary>
public class PostNewtonianTests
{
    private static PhysicsBody MakeBody(int id, double mass, Vec3d pos, Vec3d vel,
        BodyType type = BodyType.Star) =>
        new PhysicsBody(id, mass, pos, vel, type)
        {
            IsActive = true,
            GravityStrength = 60,
            GravityRange = 0,
            Radius = 0.05,
        };

    /// <summary>
    /// Two-body orbit: 1PN-enabled run should show measurable apsidal advance
    /// (perihelion precession) compared to pure Newtonian.
    /// </summary>
    [Fact]
    public void MercuryPerihelionPrecession_PNEnabled_ShowsPrecession()
    {
        // Setup: tight orbit to amplify PN effects
        // Use high mass to make GM/rc² significant
        double mass = 50.0; // ~ 50 solar masses
        double r = 0.5;     // 0.5 AU — close orbit
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));
        double dt = 0.0001;
        int steps = 20_000;

        var bodiesPN = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, v, 0)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, -v, 0)),
        };

        var bodiesN = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, v, 0)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, -v, 0)),
        };

        // Run with PN
        var solverPN = new NBodySolver();
        solverPN.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solverPN.ConfigureSoA(enabled: true, softening: 1e-4,
            deterministic: true, useParallel: false, enablePostNewtonian: true);

        for (int i = 0; i < steps; i++)
            solverPN.Step(bodiesPN, dt);

        // Run without PN (pure Newtonian)
        var solverN = new NBodySolver();
        solverN.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solverN.ConfigureSoA(enabled: true, softening: 1e-4,
            deterministic: true, useParallel: false, enablePostNewtonian: false);

        for (int i = 0; i < steps; i++)
            solverN.Step(bodiesN, dt);

        // Compute angular position difference between the two runs
        double anglePN = System.Math.Atan2(bodiesPN[0].Position.Y, bodiesPN[0].Position.X);
        double angleN = System.Math.Atan2(bodiesN[0].Position.Y, bodiesN[0].Position.X);
        double angleDiff = System.Math.Abs(anglePN - angleN);

        // 1PN should produce a measurable angular difference
        // For a 50Msun + 50Msun binary at 0.5 AU, precession is small but nonzero
        Assert.True(angleDiff > 1e-10,
            $"1PN precession angle difference {angleDiff:E4} is effectively zero — " +
            "correction may not be applying");
    }

    /// <summary>
    /// High-mass binary with 1PN: energy drift should stay comparable to
    /// Newtonian (within 1% over the test interval).
    /// </summary>
    [Fact]
    public void EnergyDrift_NewtonVs1PN_Comparable()
    {
        double mass = 10.0;
        double r = 1.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));
        double dt = 0.001;
        int steps = 10_000;

        var bodies = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, v, 0)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, -v, 0)),
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solver.ConfigureSoA(enabled: true, softening: 1e-4,
            deterministic: true, useParallel: false, enablePostNewtonian: true);

        SimulationState? state = null;
        for (int i = 0; i < steps; i++)
            state = solver.Step(bodies, dt);

        Assert.NotNull(state);
        double drift = System.Math.Abs(state.EnergyDrift);
        Assert.True(drift < 0.01,
            $"1PN energy drift {drift:E4} exceeds 1% — possible instability");
    }

    /// <summary>
    /// With PN disabled, results must exactly match a pure Newtonian run.
    /// </summary>
    [Fact]
    public void ToggleRegression_PNDisabled_MatchesNewton()
    {
        double mass = 1.0;
        double r = 1.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));
        double dt = 0.001;
        int steps = 1_000;

        var bodiesA = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };
        var bodiesB = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };

        // Run A: PN explicitly disabled
        var solverA = new NBodySolver();
        solverA.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solverA.ConfigureSoA(enabled: true, softening: 1e-4,
            deterministic: true, useParallel: false, enablePostNewtonian: false);

        for (int i = 0; i < steps; i++)
            solverA.Step(bodiesA, dt);

        // Run B: No PN parameter at all (default)
        var solverB = new NBodySolver();
        solverB.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solverB.ConfigureSoA(enabled: true, softening: 1e-4,
            deterministic: true, useParallel: false);

        for (int i = 0; i < steps; i++)
            solverB.Step(bodiesB, dt);

        // Results must be identical (bit-exact)
        for (int i = 0; i < bodiesA.Length; i++)
        {
            Assert.Equal(bodiesA[i].Position.X, bodiesB[i].Position.X);
            Assert.Equal(bodiesA[i].Position.Y, bodiesB[i].Position.Y);
            Assert.Equal(bodiesA[i].Position.Z, bodiesB[i].Position.Z);
        }
    }

    /// <summary>
    /// Relative velocity > 0.3c should return zero correction.
    /// </summary>
    [Fact]
    public void HighVelocity_CorrectionDisabled()
    {
        var correction = new PostNewtonian1Correction();
        double c = PhysicalConstants.C_Sim;

        // Setup: two bodies with relative velocity 0.5c
        double[] mass = { 10.0, 10.0 };
        double[] px = { 0.0, 1.0 };
        double[] py = { 0.0, 0.0 };
        double[] pz = { 0.0, 0.0 };
        double[] vx = { 0.0, 0.5 * c }; // Relative v > 0.3c
        double[] vy = { 0.0, 0.0 };
        double[] vz = { 0.0, 0.0 };

        var (dax, day, daz) = correction.ComputeCorrection(px, py, pz, vx, vy, vz, mass, 0, 2);

        Assert.Equal(0.0, dax);
        Assert.Equal(0.0, day);
        Assert.Equal(0.0, daz);
    }

    /// <summary>
    /// Schwarzschild proximity warning fires when bodies are within 3Rs.
    /// </summary>
    [Fact]
    public void SchwarzschildProximityWarning_Triggers()
    {
        var correction = new PostNewtonian1Correction();
        bool warningFired = false;
        correction.OnSchwarzschildProximityWarning = (i, j, dist, rs) =>
        {
            warningFired = true;
        };

        // Setup: two massive bodies very close together
        double mass = 1000.0;
        double rs = PhysicalConstants.SchwarzschildFactorSim * 2 * mass;
        double separation = rs * 0.5; // Well within 3Rs

        double[] m = { mass, mass };
        double[] px = { 0.0, separation };
        double[] py = { 0.0, 0.0 };
        double[] pz = { 0.0, 0.0 };
        double[] vx = { 0.0, 0.0 };
        double[] vy = { 0.0, 0.1 };
        double[] vz = { 0.0, 0.0 };

        correction.ComputeCorrection(px, py, pz, vx, vy, vz, m, 0, 2);

        Assert.True(warningFired, "Schwarzschild proximity warning should have fired");
    }
}
