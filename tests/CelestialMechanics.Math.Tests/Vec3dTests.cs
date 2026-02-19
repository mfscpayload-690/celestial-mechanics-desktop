using System.Numerics;

namespace CelestialMechanics.Math.Tests;

public class Vec3dTests
{
    private const double Tolerance = 1e-10;

    // ---------------------------------------------------------------
    // 1. Constructor sets values correctly
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_SetsXYZ()
    {
        var v = new Vec3d(1.5, -2.7, 3.14);

        Assert.Equal(1.5, v.X, Tolerance);
        Assert.Equal(-2.7, v.Y, Tolerance);
        Assert.Equal(3.14, v.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 2. Addition operator
    // ---------------------------------------------------------------

    [Fact]
    public void Addition_ReturnsCorrectSum()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        var result = a + b;

        Assert.Equal(5.0, result.X, Tolerance);
        Assert.Equal(7.0, result.Y, Tolerance);
        Assert.Equal(9.0, result.Z, Tolerance);
    }

    [Fact]
    public void Addition_WithNegativeComponents_ReturnsCorrectSum()
    {
        var a = new Vec3d(-1.0, 2.0, -3.0);
        var b = new Vec3d(4.0, -5.0, 6.0);

        var result = a + b;

        Assert.Equal(3.0, result.X, Tolerance);
        Assert.Equal(-3.0, result.Y, Tolerance);
        Assert.Equal(3.0, result.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 3. Subtraction operator
    // ---------------------------------------------------------------

    [Fact]
    public void Subtraction_ReturnsCorrectDifference()
    {
        var a = new Vec3d(10.0, 20.0, 30.0);
        var b = new Vec3d(1.0, 2.0, 3.0);

        var result = a - b;

        Assert.Equal(9.0, result.X, Tolerance);
        Assert.Equal(18.0, result.Y, Tolerance);
        Assert.Equal(27.0, result.Z, Tolerance);
    }

    [Fact]
    public void UnaryNegation_NegatesAllComponents()
    {
        var v = new Vec3d(1.0, -2.0, 3.0);

        var result = -v;

        Assert.Equal(-1.0, result.X, Tolerance);
        Assert.Equal(2.0, result.Y, Tolerance);
        Assert.Equal(-3.0, result.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 4. Scalar multiplication
    // ---------------------------------------------------------------

    [Fact]
    public void ScalarMultiplication_VectorTimesScalar_ReturnsScaledVector()
    {
        var v = new Vec3d(1.0, 2.0, 3.0);

        var result = v * 3.0;

        Assert.Equal(3.0, result.X, Tolerance);
        Assert.Equal(6.0, result.Y, Tolerance);
        Assert.Equal(9.0, result.Z, Tolerance);
    }

    [Fact]
    public void ScalarMultiplication_ScalarTimesVector_ReturnsScaledVector()
    {
        var v = new Vec3d(1.0, 2.0, 3.0);

        var result = 3.0 * v;

        Assert.Equal(3.0, result.X, Tolerance);
        Assert.Equal(6.0, result.Y, Tolerance);
        Assert.Equal(9.0, result.Z, Tolerance);
    }

    [Fact]
    public void ScalarMultiplication_ByZero_ReturnsZeroVector()
    {
        var v = new Vec3d(1.0, 2.0, 3.0);

        var result = v * 0.0;

        Assert.Equal(0.0, result.X, Tolerance);
        Assert.Equal(0.0, result.Y, Tolerance);
        Assert.Equal(0.0, result.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 5. Scalar division
    // ---------------------------------------------------------------

    [Fact]
    public void ScalarDivision_ReturnsCorrectResult()
    {
        var v = new Vec3d(6.0, 9.0, 12.0);

        var result = v / 3.0;

        Assert.Equal(2.0, result.X, Tolerance);
        Assert.Equal(3.0, result.Y, Tolerance);
        Assert.Equal(4.0, result.Z, Tolerance);
    }

    [Fact]
    public void ScalarDivision_ByOne_ReturnsSameVector()
    {
        var v = new Vec3d(1.5, -2.7, 3.14);

        var result = v / 1.0;

        Assert.Equal(v.X, result.X, Tolerance);
        Assert.Equal(v.Y, result.Y, Tolerance);
        Assert.Equal(v.Z, result.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 6. Dot product correctness (known values)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0)]   // perpendicular => 0
    [InlineData(1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0)]     // parallel same dir => 1
    [InlineData(1.0, 0.0, 0.0, -1.0, 0.0, 0.0, -1.0)]   // parallel opposite => -1
    [InlineData(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 32.0)]    // 1*4 + 2*5 + 3*6 = 32
    [InlineData(2.0, -3.0, 1.0, 1.0, 4.0, -2.0, -12.0)] // 2*1 + (-3)*4 + 1*(-2) = -12
    public void Dot_ReturnsExpectedValue(
        double ax, double ay, double az,
        double bx, double by, double bz,
        double expected)
    {
        var a = new Vec3d(ax, ay, az);
        var b = new Vec3d(bx, by, bz);

        Assert.Equal(expected, a.Dot(b), Tolerance);
    }

    [Fact]
    public void Dot_IsCommutative()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        Assert.Equal(a.Dot(b), b.Dot(a), Tolerance);
    }

    [Fact]
    public void Dot_StaticMethod_MatchesInstanceMethod()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        Assert.Equal(a.Dot(b), Vec3d.Dot(a, b), Tolerance);
    }

    // ---------------------------------------------------------------
    // 7. Cross product correctness (known values + anti-commutativity)
    // ---------------------------------------------------------------

    [Fact]
    public void Cross_UnitXCrossUnitY_ReturnsUnitZ()
    {
        var result = Vec3d.UnitX.Cross(Vec3d.UnitY);

        Assert.Equal(0.0, result.X, Tolerance);
        Assert.Equal(0.0, result.Y, Tolerance);
        Assert.Equal(1.0, result.Z, Tolerance);
    }

    [Fact]
    public void Cross_UnitYCrossUnitZ_ReturnsUnitX()
    {
        var result = Vec3d.UnitY.Cross(Vec3d.UnitZ);

        Assert.Equal(1.0, result.X, Tolerance);
        Assert.Equal(0.0, result.Y, Tolerance);
        Assert.Equal(0.0, result.Z, Tolerance);
    }

    [Fact]
    public void Cross_UnitZCrossUnitX_ReturnsUnitY()
    {
        var result = Vec3d.UnitZ.Cross(Vec3d.UnitX);

        Assert.Equal(0.0, result.X, Tolerance);
        Assert.Equal(1.0, result.Y, Tolerance);
        Assert.Equal(0.0, result.Z, Tolerance);
    }

    [Fact]
    public void Cross_KnownValues_ReturnsCorrectResult()
    {
        // (1,2,3) x (4,5,6) = (2*6-3*5, 3*4-1*6, 1*5-2*4) = (-3, 6, -3)
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        var result = a.Cross(b);

        Assert.Equal(-3.0, result.X, Tolerance);
        Assert.Equal(6.0, result.Y, Tolerance);
        Assert.Equal(-3.0, result.Z, Tolerance);
    }

    [Fact]
    public void Cross_IsAntiCommutative()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        var axb = a.Cross(b);
        var bxa = b.Cross(a);

        Assert.Equal(-axb.X, bxa.X, Tolerance);
        Assert.Equal(-axb.Y, bxa.Y, Tolerance);
        Assert.Equal(-axb.Z, bxa.Z, Tolerance);
    }

    [Fact]
    public void Cross_ParallelVectors_ReturnsZero()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(2.0, 4.0, 6.0);

        var result = a.Cross(b);

        Assert.Equal(0.0, result.X, Tolerance);
        Assert.Equal(0.0, result.Y, Tolerance);
        Assert.Equal(0.0, result.Z, Tolerance);
    }

    [Fact]
    public void Cross_StaticMethod_MatchesInstanceMethod()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 5.0, 6.0);

        var instance = a.Cross(b);
        var staticResult = Vec3d.Cross(a, b);

        Assert.Equal(instance.X, staticResult.X, Tolerance);
        Assert.Equal(instance.Y, staticResult.Y, Tolerance);
        Assert.Equal(instance.Z, staticResult.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 8. Length of (3,4,0) = 5
    // ---------------------------------------------------------------

    [Fact]
    public void Length_3_4_0_Returns5()
    {
        var v = new Vec3d(3.0, 4.0, 0.0);

        Assert.Equal(5.0, v.Length, Tolerance);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 1.0)]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0, 1.7320508075688772)] // sqrt(3)
    public void Length_ReturnsExpectedValue(double x, double y, double z, double expected)
    {
        var v = new Vec3d(x, y, z);

        Assert.Equal(expected, v.Length, Tolerance);
    }

    // ---------------------------------------------------------------
    // 9. LengthSquared of (3,4,0) = 25
    // ---------------------------------------------------------------

    [Fact]
    public void LengthSquared_3_4_0_Returns25()
    {
        var v = new Vec3d(3.0, 4.0, 0.0);

        Assert.Equal(25.0, v.LengthSquared, Tolerance);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 1.0)]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 2.0, 3.0, 14.0)] // 1 + 4 + 9
    public void LengthSquared_ReturnsExpectedValue(double x, double y, double z, double expected)
    {
        var v = new Vec3d(x, y, z);

        Assert.Equal(expected, v.LengthSquared, Tolerance);
    }

    // ---------------------------------------------------------------
    // 10. Normalize produces unit vector
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(3.0, 4.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(10.0, 0.0, 0.0)]
    [InlineData(-5.0, 3.0, 7.0)]
    public void Normalized_ProducesUnitLength(double x, double y, double z)
    {
        var v = new Vec3d(x, y, z);

        var normalized = v.Normalized();

        Assert.Equal(1.0, normalized.Length, Tolerance);
    }

    [Fact]
    public void Normalized_PreservesDirection()
    {
        var v = new Vec3d(3.0, 4.0, 0.0);

        var normalized = v.Normalized();

        Assert.Equal(3.0 / 5.0, normalized.X, Tolerance);
        Assert.Equal(4.0 / 5.0, normalized.Y, Tolerance);
        Assert.Equal(0.0, normalized.Z, Tolerance);
    }

    [Fact]
    public void Normalized_ZeroVector_ReturnsZero()
    {
        var normalized = Vec3d.Zero.Normalized();

        Assert.Equal(0.0, normalized.X, Tolerance);
        Assert.Equal(0.0, normalized.Y, Tolerance);
        Assert.Equal(0.0, normalized.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 11. DistanceTo correctness
    // ---------------------------------------------------------------

    [Fact]
    public void DistanceTo_KnownValues_ReturnsCorrectDistance()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 6.0, 3.0);

        // distance = sqrt((4-1)^2 + (6-2)^2 + (3-3)^2) = sqrt(9+16+0) = 5
        Assert.Equal(5.0, a.DistanceTo(b), Tolerance);
    }

    [Fact]
    public void DistanceTo_SamePoint_ReturnsZero()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);

        Assert.Equal(0.0, a.DistanceTo(a), Tolerance);
    }

    [Fact]
    public void DistanceTo_IsSymmetric()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 6.0, 8.0);

        Assert.Equal(a.DistanceTo(b), b.DistanceTo(a), Tolerance);
    }

    [Fact]
    public void DistanceSquaredTo_KnownValues_ReturnsCorrectValue()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(4.0, 6.0, 3.0);

        // distanceSq = (4-1)^2 + (6-2)^2 + (3-3)^2 = 9+16+0 = 25
        Assert.Equal(25.0, a.DistanceSquaredTo(b), Tolerance);
    }

    // ---------------------------------------------------------------
    // 12. Vec3d.Zero is (0,0,0)
    // ---------------------------------------------------------------

    [Fact]
    public void Zero_ReturnsOrigin()
    {
        var zero = Vec3d.Zero;

        Assert.Equal(0.0, zero.X, Tolerance);
        Assert.Equal(0.0, zero.Y, Tolerance);
        Assert.Equal(0.0, zero.Z, Tolerance);
    }

    [Fact]
    public void One_ReturnsAllOnes()
    {
        var one = Vec3d.One;

        Assert.Equal(1.0, one.X, Tolerance);
        Assert.Equal(1.0, one.Y, Tolerance);
        Assert.Equal(1.0, one.Z, Tolerance);
    }

    [Fact]
    public void UnitVectors_AreCorrect()
    {
        Assert.Equal(1.0, Vec3d.UnitX.X, Tolerance);
        Assert.Equal(0.0, Vec3d.UnitX.Y, Tolerance);
        Assert.Equal(0.0, Vec3d.UnitX.Z, Tolerance);

        Assert.Equal(0.0, Vec3d.UnitY.X, Tolerance);
        Assert.Equal(1.0, Vec3d.UnitY.Y, Tolerance);
        Assert.Equal(0.0, Vec3d.UnitY.Z, Tolerance);

        Assert.Equal(0.0, Vec3d.UnitZ.X, Tolerance);
        Assert.Equal(0.0, Vec3d.UnitZ.Y, Tolerance);
        Assert.Equal(1.0, Vec3d.UnitZ.Z, Tolerance);
    }

    // ---------------------------------------------------------------
    // 13. ToVector3 converts correctly (lossy conversion)
    // ---------------------------------------------------------------

    [Fact]
    public void ToVector3_ConvertsToSinglePrecision()
    {
        var v = new Vec3d(1.5, -2.7, 3.14);

        Vector3 result = v.ToVector3();

        Assert.Equal(1.5f, result.X);
        Assert.Equal(-2.7f, result.Y);
        Assert.Equal(3.14f, result.Z);
    }

    [Fact]
    public void ToVector3_LossyConversion_LosesPrecision()
    {
        // A value that cannot be exactly represented in float
        double precise = 1.0 / 3.0;
        var v = new Vec3d(precise, precise, precise);

        Vector3 result = v.ToVector3();

        // The float conversion should equal (float)(1.0/3.0), not the exact double
        Assert.Equal((float)precise, result.X);
        Assert.Equal((float)precise, result.Y);
        Assert.Equal((float)precise, result.Z);

        // Verify precision loss: the float value differs from the double
        Assert.NotEqual(precise, (double)result.X);
    }

    [Fact]
    public void ToVector3_Zero_ConvertsCorrectly()
    {
        Vector3 result = Vec3d.Zero.ToVector3();

        Assert.Equal(0f, result.X);
        Assert.Equal(0f, result.Y);
        Assert.Equal(0f, result.Z);
    }

    // ---------------------------------------------------------------
    // 14. Equality and HashCode
    // ---------------------------------------------------------------

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(1.0, 2.0, 3.0);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(1.0, 2.0, 3.1);

        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equals_ObjectOverload_WorksCorrectly()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        object b = new Vec3d(1.0, 2.0, 3.0);
        object c = "not a vec3d";

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(1.0, 2.0, 3.0);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_ReturnsDifferentHash()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(3.0, 2.0, 1.0);

        // Hash collisions are theoretically possible but extremely unlikely for these values
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EqualVectors_WorkInHashSet()
    {
        var a = new Vec3d(1.0, 2.0, 3.0);
        var b = new Vec3d(1.0, 2.0, 3.0);

        var set = new HashSet<Vec3d> { a };

        Assert.Contains(b, set);
    }
}
