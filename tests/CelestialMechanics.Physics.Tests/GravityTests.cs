using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

public class GravityTests
{
    private static PhysicsBody MakeBody(int id, double mass, Vec3d pos, Vec3d vel)
    {
        return new PhysicsBody(id, mass, pos, vel, BodyType.Star)
        {
            IsActive = true,
            GravityStrength = 60,
            GravityRange = 0, // infinite range
            Radius = 0.05,
        };
    }

    [Fact]
    public void TwoUnitMassesAtUnitDistance_ForceEqualsGSim()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 0 };
        var a = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);
        var b = MakeBody(1, 1.0, new Vec3d(1, 0, 0), Vec3d.Zero);

        Vec3d force = gravity.ComputeForce(a, b);

        // F = G_Sim * 1 * 1 / 1^2 = 1.0, along +X
        Assert.Equal(PhysicalConstants.G_Sim, force.X, 1e-10);
        Assert.Equal(0.0, force.Y, 1e-10);
        Assert.Equal(0.0, force.Z, 1e-10);
    }

    [Fact]
    public void Force_IsAttractive_PointsFromAToB()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 0 };
        var a = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);
        var b = MakeBody(1, 1.0, new Vec3d(5, 0, 0), Vec3d.Zero);

        Vec3d force = gravity.ComputeForce(a, b);

        // Force on a should point toward b (positive X)
        Assert.True(force.X > 0);
    }

    [Fact]
    public void NewtonsThirdLaw_ForceOnBIsNegativeOfForceOnA()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 0 };
        var a = MakeBody(0, 2.0, new Vec3d(1, 2, 3), Vec3d.Zero);
        var b = MakeBody(1, 3.0, new Vec3d(4, 5, 6), Vec3d.Zero);

        Vec3d fab = gravity.ComputeForce(a, b);
        Vec3d fba = gravity.ComputeForce(b, a);

        Assert.Equal(-fab.X, fba.X, 1e-10);
        Assert.Equal(-fab.Y, fba.Y, 1e-10);
        Assert.Equal(-fab.Z, fba.Z, 1e-10);
    }

    [Fact]
    public void Softening_PreventsInfinityAtZeroDistance()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 1e-2 };
        var a = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);
        var b = MakeBody(1, 1.0, Vec3d.Zero, Vec3d.Zero);

        Vec3d force = gravity.ComputeForce(a, b);

        // Should not be infinite or NaN
        Assert.False(double.IsNaN(force.X));
        Assert.False(double.IsInfinity(force.X));
    }

    [Fact]
    public void PotentialEnergy_IsNegative()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 0 };
        var a = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);
        var b = MakeBody(1, 1.0, new Vec3d(2, 0, 0), Vec3d.Zero);

        double pe = gravity.ComputePotentialEnergy(a, b);

        Assert.True(pe < 0);
        // PE = -G*m1*m2/r = -1*1*1/2 = -0.5
        Assert.Equal(-0.5, pe, 1e-10);
    }

    [Fact]
    public void InverseSquareLaw_ForceQuartersWhenDistanceDoubles()
    {
        var gravity = new NewtonianGravity { SofteningEpsilon = 0 };
        var a = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);
        var b1 = MakeBody(1, 1.0, new Vec3d(1, 0, 0), Vec3d.Zero);
        var b2 = MakeBody(2, 1.0, new Vec3d(2, 0, 0), Vec3d.Zero);

        Vec3d f1 = gravity.ComputeForce(a, b1);
        Vec3d f2 = gravity.ComputeForce(a, b2);

        // F at 2x distance should be 1/4 of F at 1x distance
        Assert.Equal(f1.X / 4.0, f2.X, 1e-10);
    }
}
