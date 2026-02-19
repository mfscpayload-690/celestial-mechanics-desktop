using System.Numerics;
using System.Runtime.InteropServices;

namespace CelestialMechanics.Math;

/// <summary>
/// Double-precision 3D vector, immutable value type with sequential memory layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vec3d : IEquatable<Vec3d>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    // --- Static properties ---

    public static Vec3d Zero => new(0.0, 0.0, 0.0);
    public static Vec3d One => new(1.0, 1.0, 1.0);
    public static Vec3d UnitX => new(1.0, 0.0, 0.0);
    public static Vec3d UnitY => new(0.0, 1.0, 0.0);
    public static Vec3d UnitZ => new(0.0, 0.0, 1.0);

    // --- Operator overloads ---

    public static Vec3d operator +(Vec3d a, Vec3d b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3d operator -(Vec3d a, Vec3d b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3d operator *(Vec3d v, double s) =>
        new(v.X * s, v.Y * s, v.Z * s);

    public static Vec3d operator *(double s, Vec3d v) =>
        new(v.X * s, v.Y * s, v.Z * s);

    public static Vec3d operator /(Vec3d v, double s) =>
        new(v.X / s, v.Y / s, v.Z / s);

    public static Vec3d operator -(Vec3d v) =>
        new(-v.X, -v.Y, -v.Z);

    public static bool operator ==(Vec3d left, Vec3d right) =>
        left.Equals(right);

    public static bool operator !=(Vec3d left, Vec3d right) =>
        !left.Equals(right);

    // --- Instance methods ---

    public double Dot(Vec3d other) =>
        X * other.X + Y * other.Y + Z * other.Z;

    public Vec3d Cross(Vec3d other) =>
        new(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => System.Math.Sqrt(LengthSquared);

    public Vec3d Normalized()
    {
        double len = Length;
        if (len < double.Epsilon)
            return Zero;
        return this / len;
    }

    public double DistanceTo(Vec3d other) =>
        (this - other).Length;

    public double DistanceSquaredTo(Vec3d other) =>
        (this - other).LengthSquared;

    // --- Static helpers ---

    public static double Dot(Vec3d a, Vec3d b) =>
        a.Dot(b);

    public static Vec3d Cross(Vec3d a, Vec3d b) =>
        a.Cross(b);

    /// <summary>
    /// Creates a vector from spherical coordinates.
    /// </summary>
    /// <param name="r">Radius (distance from origin).</param>
    /// <param name="theta">Polar angle from the positive Z axis, in radians.</param>
    /// <param name="phi">Azimuthal angle from the positive X axis in the XY plane, in radians.</param>
    public static Vec3d FromSpherical(double r, double theta, double phi) =>
        new(
            r * System.Math.Sin(theta) * System.Math.Cos(phi),
            r * System.Math.Sin(theta) * System.Math.Sin(phi),
            r * System.Math.Cos(theta)
        );

    /// <summary>
    /// Converts to single-precision <see cref="Vector3"/> for GL interop.
    /// </summary>
    public Vector3 ToVector3() =>
        new((float)X, (float)Y, (float)Z);

    // --- IEquatable / object overrides ---

    public bool Equals(Vec3d other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) =>
        obj is Vec3d other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z);

    public override string ToString() =>
        $"<{X}, {Y}, {Z}>";
}
