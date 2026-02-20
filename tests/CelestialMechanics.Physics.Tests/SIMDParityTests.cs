using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Tests that the SIMD backend produces forces identical (within tolerance)
/// to the scalar CpuSingleThreadBackend.
/// </summary>
public class SIMDParityTests
{
    private static PhysicsBody[] MakeRing(int n, double radius, double mass)
    {
        var bodies = new PhysicsBody[n];
        for (int i = 0; i < n; i++)
        {
            double angle = 2.0 * System.Math.PI * i / n;
            double x = radius * System.Math.Cos(angle);
            double y = radius * System.Math.Sin(angle);
            bodies[i] = new PhysicsBody(i, mass,
                new Vec3d(x, y, 0), Vec3d.Zero, BodyType.Star);
        }
        return bodies;
    }

    // ── Test 1: SIMD matches scalar for small N ─────────────────────────────

    [Fact]
    public void SimdForces_Match_ScalarForces_SmallN()
    {
        var bodies = MakeRing(10, 10.0, 1.0);
        double softening = 0.01;

        var scalarBackend = new CpuSingleThreadBackend();
        var simdBackend = new SimdSingleThreadBackend();

        var soaScalar = new BodySoA(16);
        var soaSimd = new BodySoA(16);

        soaScalar.CopyFrom(bodies);
        soaSimd.CopyFrom(bodies);

        scalarBackend.ComputeForces(soaScalar, softening);
        simdBackend.ComputeForces(soaSimd, softening);

        for (int i = 0; i < bodies.Length; i++)
        {
            Assert.True(System.Math.Abs(soaScalar.AccX[i] - soaSimd.AccX[i]) < 1e-10,
                $"AccX mismatch at body {i}: scalar={soaScalar.AccX[i]:E6}, simd={soaSimd.AccX[i]:E6}");
            Assert.True(System.Math.Abs(soaScalar.AccY[i] - soaSimd.AccY[i]) < 1e-10,
                $"AccY mismatch at body {i}: scalar={soaScalar.AccY[i]:E6}, simd={soaSimd.AccY[i]:E6}");
            Assert.True(System.Math.Abs(soaScalar.AccZ[i] - soaSimd.AccZ[i]) < 1e-10,
                $"AccZ mismatch at body {i}: scalar={soaScalar.AccZ[i]:E6}, simd={soaSimd.AccZ[i]:E6}");
        }
    }

    // ── Test 2: SIMD matches scalar for larger N (not SIMD-width aligned) ───

    [Fact]
    public void SimdForces_Match_ScalarForces_LargeN_NotAligned()
    {
        // 67 bodies — not a multiple of any SIMD width
        var bodies = MakeRing(67, 20.0, 5.0);
        double softening = 0.001;

        var scalarBackend = new CpuSingleThreadBackend();
        var simdBackend = new SimdSingleThreadBackend();

        var soaScalar = new BodySoA(128);
        var soaSimd = new BodySoA(128);

        soaScalar.CopyFrom(bodies);
        soaSimd.CopyFrom(bodies);

        scalarBackend.ComputeForces(soaScalar, softening);
        simdBackend.ComputeForces(soaSimd, softening);

        double maxRelErr = 0.0;
        for (int i = 0; i < bodies.Length; i++)
        {
            double scalarMag = System.Math.Sqrt(
                soaScalar.AccX[i] * soaScalar.AccX[i] +
                soaScalar.AccY[i] * soaScalar.AccY[i] +
                soaScalar.AccZ[i] * soaScalar.AccZ[i]);

            if (scalarMag < 1e-15) continue;

            double errX = System.Math.Abs(soaScalar.AccX[i] - soaSimd.AccX[i]);
            double errY = System.Math.Abs(soaScalar.AccY[i] - soaSimd.AccY[i]);
            double errZ = System.Math.Abs(soaScalar.AccZ[i] - soaSimd.AccZ[i]);
            double relErr = System.Math.Sqrt(errX * errX + errY * errY + errZ * errZ) / scalarMag;

            if (relErr > maxRelErr) maxRelErr = relErr;
        }

        Assert.True(maxRelErr < 1e-12,
            $"Max relative acceleration error {maxRelErr:E3} exceeds 1e-12 tolerance");
    }

    // ── Test 3: SIMD handles single body (no-op) ────────────────────────────

    [Fact]
    public void SimdForces_SingleBody_ZeroAcceleration()
    {
        var bodies = new[] { new PhysicsBody(0, 100.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star) };

        var simdBackend = new SimdSingleThreadBackend();
        var soa = new BodySoA(16);
        soa.CopyFrom(bodies);

        simdBackend.ComputeForces(soa, 0.01);

        Assert.Equal(0.0, soa.AccX[0], 1e-15);
        Assert.Equal(0.0, soa.AccY[0], 1e-15);
        Assert.Equal(0.0, soa.AccZ[0], 1e-15);
    }
}
