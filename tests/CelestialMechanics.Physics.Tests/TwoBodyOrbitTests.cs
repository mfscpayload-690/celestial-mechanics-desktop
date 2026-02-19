using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

public class TwoBodyOrbitTests
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

    [Fact]
    public void VerletOrbit_10000Steps_EnergyDriftBelow001Percent()
    {
        double mass = 1.0;
        double r = 1.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        var bodies = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });
        solver.SetIntegrator(new VerletIntegrator());

        double dt = 0.001;
        SimulationState? state = null;

        for (int i = 0; i < 10000; i++)
        {
            state = solver.Step(bodies, dt);
        }

        Assert.NotNull(state);
        double drift = System.Math.Abs(state.EnergyDrift);
        Assert.True(drift < 0.0001, $"Energy drift {drift:E4} exceeds 0.01%");
    }

    [Fact]
    public void EulerOrbit_10000Steps_EnergyDriftExceeds1Percent()
    {
        double mass = 1.0;
        double r = 1.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        var bodies = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 0 });
        solver.SetIntegrator(new EulerIntegrator());

        double dt = 0.001;
        SimulationState? state = null;

        for (int i = 0; i < 10000; i++)
        {
            state = solver.Step(bodies, dt);
        }

        Assert.NotNull(state);
        double drift = System.Math.Abs(state.EnergyDrift);
        Assert.True(drift > 0.001, $"Euler drift {drift:E4} is suspiciously low for 10000 steps");
    }

    [Fact]
    public void TwoBodyOrbit_BodiesRemainBound()
    {
        double mass = 1.0;
        double r = 1.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        var bodies = new[]
        {
            MakeBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v)),
            MakeBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v)),
        };

        var integrator = new VerletIntegrator();
        var forces = new IForceCalculator[] { new NewtonianGravity { SofteningEpsilon = 0 } };
        double dt = 0.001;

        for (int i = 0; i < 10000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        // Bodies should remain within a reasonable distance of each other
        double separation = bodies[0].Position.DistanceTo(bodies[1].Position);
        Assert.True(separation < 10.0, $"Bodies separated to {separation} AU — orbit is unbound");
        Assert.True(separation > 0.1, $"Bodies collapsed to {separation} AU — unrealistic");
    }
}
