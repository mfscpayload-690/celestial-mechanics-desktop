namespace CelestialMechanics.Math;

/// <summary>
/// Double-precision quaternion for rotation calculations.
/// </summary>
public readonly struct Quaterniond
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;
    public readonly double W;

    public Quaterniond(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    // --- Static properties ---

    /// <summary>
    /// The identity quaternion (no rotation).
    /// </summary>
    public static Quaterniond Identity => new(0, 0, 0, 1);

    // --- Factory methods ---

    /// <summary>
    /// Creates a quaternion representing a rotation of <paramref name="angle"/> radians
    /// around the given <paramref name="axis"/> (which will be normalized internally).
    /// </summary>
    public static Quaterniond FromAxisAngle(Vec3d axis, double angle)
    {
        Vec3d n = axis.Normalized();
        double halfAngle = angle * 0.5;
        double s = System.Math.Sin(halfAngle);
        return new Quaterniond(
            n.X * s,
            n.Y * s,
            n.Z * s,
            System.Math.Cos(halfAngle)
        );
    }

    // --- Operators ---

    /// <summary>
    /// Hamilton product of two quaternions.
    /// </summary>
    public static Quaterniond operator *(Quaterniond a, Quaterniond b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
    );

    // --- Instance methods ---

    /// <summary>
    /// Rotates a vector by this quaternion using the sandwich product q * v * q^(-1).
    /// </summary>
    public Vec3d Rotate(Vec3d v)
    {
        // Optimized rotation: v' = v + 2w(u x v) + 2(u x (u x v))
        // where u = (X, Y, Z), w = W
        Vec3d u = new(X, Y, Z);
        Vec3d uv = Vec3d.Cross(u, v);
        Vec3d uuv = Vec3d.Cross(u, uv);
        return v + 2.0 * (W * uv + uuv);
    }

    /// <summary>
    /// Returns the quaternion normalized to unit length.
    /// </summary>
    public Quaterniond Normalized()
    {
        double len = System.Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        if (len < double.Epsilon)
            return Identity;
        double invLen = 1.0 / len;
        return new Quaterniond(X * invLen, Y * invLen, Z * invLen, W * invLen);
    }

    /// <summary>
    /// Converts this quaternion to a 4x4 rotation matrix.
    /// </summary>
    public Mat4d ToRotationMatrix()
    {
        double xx = X * X, yy = Y * Y, zz = Z * Z;
        double xy = X * Y, xz = X * Z, yz = Y * Z;
        double wx = W * X, wy = W * Y, wz = W * Z;

        return new Mat4d(
            1 - 2 * (yy + zz),     2 * (xy - wz),     2 * (xz + wy), 0,
                2 * (xy + wz), 1 - 2 * (xx + zz),     2 * (yz - wx), 0,
                2 * (xz - wy),     2 * (yz + wx), 1 - 2 * (xx + yy), 0,
                            0,                  0,                  0, 1
        );
    }
}
