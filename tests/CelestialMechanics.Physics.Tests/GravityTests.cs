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

    [Fact]
    public void ShellTheorem_OutsideBody_MatchesSoftenedNewtonian()
    {
        var gravity = new NewtonianGravity
        {
            SofteningEpsilon = 1e-4,
            EnableShellTheorem = true
        };

        var a = MakeBody(0, 2.0, Vec3d.Zero, Vec3d.Zero);
        var b = MakeBody(1, 3.0, new Vec3d(5.0, 0, 0), Vec3d.Zero);
        b.Radius = 1.0;

        Vec3d shell = gravity.ComputeForce(a, b);

        var reference = new NewtonianGravity
        {
            SofteningEpsilon = 1e-4,
            EnableShellTheorem = false
        }.ComputeForce(a, b);

        Assert.Equal(reference.X, shell.X, 1e-10);
        Assert.Equal(reference.Y, shell.Y, 1e-10);
        Assert.Equal(reference.Z, shell.Z, 1e-10);
    }

    [Fact]
    public void ShellTheorem_InsideBody_ForceScalesLinearlyWithRadius()
    {
        var gravity = new NewtonianGravity
        {
            SofteningEpsilon = 0,
            EnableShellTheorem = true
        };

        var source = MakeBody(1, 5.0, Vec3d.Zero, Vec3d.Zero);
        source.Radius = 2.0;

        var probeNear = MakeBody(0, 1.0, new Vec3d(0.5, 0, 0), Vec3d.Zero);
        var probeFar = MakeBody(2, 1.0, new Vec3d(1.0, 0, 0), Vec3d.Zero);

        Vec3d fNear = gravity.ComputeForce(probeNear, source);
        Vec3d fFar = gravity.ComputeForce(probeFar, source);

        // Interior force should be proportional to displacement from source center.
        Assert.Equal(2.0, fFar.X / fNear.X, 1e-6);
    }

    [Fact]
    public void ShellTheorem_AtCenter_ForceIsFiniteAndZeroVector()
    {
        var gravity = new NewtonianGravity
        {
            SofteningEpsilon = 1e-3,
            EnableShellTheorem = true
        };

        var source = MakeBody(1, 5.0, Vec3d.Zero, Vec3d.Zero);
        source.Radius = 2.0;
        var probe = MakeBody(0, 1.0, Vec3d.Zero, Vec3d.Zero);

        Vec3d f = gravity.ComputeForce(probe, source);

        Assert.False(double.IsNaN(f.X));
        Assert.False(double.IsInfinity(f.X));
        Assert.Equal(0.0, f.Length, 1e-12);
    }

    [Fact]
    public void ShellTheorem_BoundaryTransition_IsContinuous()
    {
        var gravity = new NewtonianGravity
        {
            SofteningEpsilon = 1e-4,
            EnableShellTheorem = true
        };

        var source = MakeBody(1, 4.0, Vec3d.Zero, Vec3d.Zero);
        source.Radius = 1.0;

        var inside = MakeBody(0, 1.0, new Vec3d(0.999, 0, 0), Vec3d.Zero);
        var outside = MakeBody(2, 1.0, new Vec3d(1.001, 0, 0), Vec3d.Zero);

        double fInside = gravity.ComputeForce(inside, source).Length;
        double fOutside = gravity.ComputeForce(outside, source).Length;
        double relativeJump = System.Math.Abs(fOutside - fInside) / System.Math.Max(fOutside, fInside);

        Assert.True(relativeJump < 0.02, $"Boundary discontinuity too large: {relativeJump:P2}");
    }
}
