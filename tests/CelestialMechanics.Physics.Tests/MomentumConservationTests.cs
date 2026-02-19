using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

public class MomentumConservationTests
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
    public void ThreeBody_VerletMomentumConserved()
    {
        var bodies = new[]
        {
            MakeBody(0, 1.0, new Vec3d(1, 0, 0), new Vec3d(0, 0.1, 0)),
            MakeBody(1, 2.0, new Vec3d(-1, 0, 0), new Vec3d(0, -0.05, 0.1)),
            MakeBody(2, 0.5, new Vec3d(0, 2, 0), new Vec3d(-0.1, 0, -0.2)),
        };

        var integrator = new VerletIntegrator();
        var forces = new IForceCalculator[] { new NewtonianGravity { SofteningEpsilon = 1e-4 } };
        var energy = new EnergyCalculator();

        Vec3d initialMomentum = energy.ComputeMomentum(bodies);

        double dt = 0.001;
        for (int i = 0; i < 1000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        Vec3d finalMomentum = energy.ComputeMomentum(bodies);
        Vec3d diff = finalMomentum - initialMomentum;
        double drift = diff.Length;

        // Momentum should be conserved to near machine epsilon
        Assert.True(drift < 1e-10, $"Momentum drift {drift:E4} exceeds tolerance");
    }

    [Fact]
    public void TwoBody_CenterOfMassUnchanged()
    {
        double mass1 = 1.0, mass2 = 2.0;
        var bodies = new[]
        {
            MakeBody(0, mass1, new Vec3d(2, 0, 0), new Vec3d(0, 0, 0.3)),
            MakeBody(1, mass2, new Vec3d(-1, 0, 0), new Vec3d(0, 0, -0.15)),
        };

        double totalMass = mass1 + mass2;
        Vec3d initialCom = (bodies[0].Position * mass1 + bodies[1].Position * mass2) * (1.0 / totalMass);

        var integrator = new VerletIntegrator();
        var forces = new IForceCalculator[] { new NewtonianGravity { SofteningEpsilon = 0 } };

        double dt = 0.001;
        for (int i = 0; i < 1000; i++)
        {
            integrator.Step(bodies, dt, forces);
        }

        Vec3d finalCom = (bodies[0].Position * mass1 + bodies[1].Position * mass2) * (1.0 / totalMass);
        Vec3d diff = finalCom - initialCom;

        // Center of mass velocity = total momentum / total mass
        // If momentum is conserved, CoM should move linearly
        // With net momentum ~0, CoM should barely move
        Assert.True(diff.Length < 1.0, $"Center of mass shifted by {diff.Length}");
    }
}
