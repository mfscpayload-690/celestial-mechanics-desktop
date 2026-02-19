using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

public class IntegratorTests
{
    private static PhysicsBody MakeBody(int id, double mass, Vec3d pos, Vec3d vel)
    {
        return new PhysicsBody(id, mass, pos, vel, BodyType.Star)
        {
            IsActive = true,
            GravityStrength = 60,
            GravityRange = 0,
            Radius = 0.05,
        };
    }

    private static (PhysicsBody[], IForceCalculator[]) SetupTwoBodyOrbit()
    {
        double mass = 1.0;
        double r = 1.0; // each body at distance r from center
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        var bodies = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };

        var forces = new IForceCalculator[] { new NewtonianGravity { SofteningEpsilon = 0 } };
        return (bodies, forces);
    }

    [Fact]
    public void VerletIntegrator_TwoBodyOrbit_EnergyConserved()
    {
        var (bodies, forces) = SetupTwoBodyOrbit();
        var integrator = new VerletIntegrator();
        var energy = new EnergyCalculator();

        double dt = 0.001;
        double initialKE = energy.ComputeKE(bodies);
        double initialPE = energy.ComputePE(bodies, forces);
        double initialE = initialKE + initialPE;

        // Run for 10000 steps
        for (int i = 0; i < 10000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        double finalKE = energy.ComputeKE(bodies);
        double finalPE = energy.ComputePE(bodies, forces);
        double finalE = finalKE + finalPE;

        double drift = System.Math.Abs((finalE - initialE) / initialE);

        // Verlet should conserve energy to < 0.01% over 10000 steps
        Assert.True(drift < 0.0001, $"Verlet energy drift {drift:E4} exceeds 0.01%");
    }

    [Fact]
    public void EulerIntegrator_TwoBodyOrbit_EnergyDrifts()
    {
        var (bodies, forces) = SetupTwoBodyOrbit();
        var integrator = new EulerIntegrator();
        var energy = new EnergyCalculator();

        double dt = 0.001;
        double initialKE = energy.ComputeKE(bodies);
        double initialPE = energy.ComputePE(bodies, forces);
        double initialE = initialKE + initialPE;

        // Run for 10000 steps
        for (int i = 0; i < 10000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        double finalKE = energy.ComputeKE(bodies);
        double finalPE = energy.ComputePE(bodies, forces);
        double finalE = finalKE + finalPE;

        double drift = System.Math.Abs((finalE - initialE) / initialE);

        // Euler should show significant energy drift (> 0.1%)
        Assert.True(drift > 0.001, $"Euler energy drift {drift:E4} is suspiciously low");
    }

    [Fact]
    public void RK4Integrator_TwoBodyOrbit_ReasonableAccuracy()
    {
        var (bodies, forces) = SetupTwoBodyOrbit();
        var integrator = new RK4Integrator();
        var energy = new EnergyCalculator();

        double dt = 0.001;
        double initialKE = energy.ComputeKE(bodies);
        double initialPE = energy.ComputePE(bodies, forces);
        double initialE = initialKE + initialPE;

        // Run for 1000 steps
        for (int i = 0; i < 1000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        double finalKE = energy.ComputeKE(bodies);
        double finalPE = energy.ComputePE(bodies, forces);
        double finalE = finalKE + finalPE;

        double drift = System.Math.Abs((finalE - initialE) / initialE);

        // RK4 should be accurate over short runs (< 1% drift for 1000 steps)
        Assert.True(drift < 0.01, $"RK4 energy drift {drift:E4} exceeds 1%");
    }
}
