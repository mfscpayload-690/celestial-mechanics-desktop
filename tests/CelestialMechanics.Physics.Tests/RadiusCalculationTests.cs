using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Tests for physical radius computation from mass and density.
/// </summary>
public class RadiusCalculationTests
{
    // ── Test 1: Uniform sphere radius formula ────────────────────────────────

    /// <summary>
    /// Verify r = (3m / 4πρ)^(1/3) for known values.
    /// m = 4/3 π r³ ρ  → for r=1, ρ=1: m = 4.1888
    /// So ComputeRadius(4.1888, 1.0) should return ≈ 1.0.
    /// </summary>
    [Fact]
    public void ComputeRadius_UnitSphere_ReturnsOne()
    {
        double mass = (4.0 / 3.0) * System.Math.PI; // sphere of r=1, ρ=1
        double radius = DensityModel.ComputeRadius(mass, 1.0);

        Assert.True(System.Math.Abs(radius - 1.0) < 1e-10,
            $"Expected radius ≈ 1.0, got {radius}");
    }

    // ── Test 2: Radius scales with cube root of mass ─────────────────────────

    [Fact]
    public void ComputeRadius_DoubleMass_IncreasesRadiusByCubeRootOfTwo()
    {
        double r1 = DensityModel.ComputeRadius(1.0, 1000.0);
        double r2 = DensityModel.ComputeRadius(2.0, 1000.0);

        double ratio = r2 / r1;
        double expected = System.Math.Cbrt(2.0);

        Assert.True(System.Math.Abs(ratio - expected) < 1e-10,
            $"Expected ratio {expected:F6}, got {ratio:F6}");
    }

    // ── Test 3: Zero/negative mass returns zero ──────────────────────────────

    [Theory]
    [InlineData(0.0, 1000.0)]
    [InlineData(-1.0, 1000.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.0, -1.0)]
    public void ComputeRadius_InvalidInput_ReturnsZero(double mass, double density)
    {
        double radius = DensityModel.ComputeRadius(mass, density);
        Assert.Equal(0.0, radius);
    }

    // ── Test 4: Black hole uses Schwarzschild radius ─────────────────────────

    [Fact]
    public void ComputeBodyRadius_BlackHole_UsesSchwarzschild()
    {
        double mass = 10.0;
        double radiusBH = DensityModel.ComputeBodyRadius(mass, 1.0, BodyType.BlackHole);
        double radiusStar = DensityModel.ComputeBodyRadius(mass, 1.0, BodyType.Star);

        // BH should use Schwarzschild formula, not uniform sphere
        Assert.NotEqual(radiusStar, radiusBH);
        Assert.True(radiusBH > 0.0, "Black hole radius must be positive");
    }

    // ── Test 5: PhysicsBody constructor computes radius ──────────────────────

    [Fact]
    public void PhysicsBody_Constructor_ComputesRadius()
    {
        var body = new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star);

        Assert.True(body.Radius > 0.0,
            $"Expected positive radius, got {body.Radius}");
        Assert.True(body.IsCollidable, "Bodies should be collidable by default");
        Assert.True(body.Density > 0.0, "Density should be set from defaults");
    }

    // ── Test 6: Default densities per body type ──────────────────────────────

    [Theory]
    [InlineData(BodyType.Star)]
    [InlineData(BodyType.RockyPlanet)]
    [InlineData(BodyType.GasGiant)]
    [InlineData(BodyType.NeutronStar)]
    [InlineData(BodyType.Asteroid)]
    public void GetDefaultDensity_AllTypes_ReturnPositive(BodyType type)
    {
        double density = DensityModel.GetDefaultDensity(type);
        Assert.True(density > 0.0, $"Default density for {type} must be positive");
    }

    // ── Test 7: RecalculateRadius updates after mass change ──────────────────

    [Fact]
    public void RecalculateRadius_AfterMassChange_UpdatesRadius()
    {
        var body = new PhysicsBody(0, 1.0, Vec3d.Zero, Vec3d.Zero, BodyType.Star);
        double originalRadius = body.Radius;

        body.Mass = 8.0; // 8× mass → 2× radius (cube root)
        body.RecalculateRadius();

        double expectedRatio = System.Math.Cbrt(8.0);
        double actualRatio = body.Radius / originalRadius;

        Assert.True(System.Math.Abs(actualRatio - expectedRatio) < 1e-10,
            $"Expected ratio {expectedRatio:F6}, got {actualRatio:F6}");
    }
}
