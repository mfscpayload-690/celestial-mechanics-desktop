using System.Numerics;

namespace CelestialMechanics.Math;

/// <summary>
/// 4x4 double-precision matrix stored in row-major order.
/// </summary>
public readonly struct Mat4d
{
    // Row 0
    public readonly double M00, M01, M02, M03;
    // Row 1
    public readonly double M10, M11, M12, M13;
    // Row 2
    public readonly double M20, M21, M22, M23;
    // Row 3
    public readonly double M30, M31, M32, M33;

    public Mat4d(
        double m00, double m01, double m02, double m03,
        double m10, double m11, double m12, double m13,
        double m20, double m21, double m22, double m23,
        double m30, double m31, double m32, double m33)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
        M30 = m30; M31 = m31; M32 = m32; M33 = m33;
    }

    // --- Static properties ---

    public static Mat4d Identity => new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    );

    // --- Factory methods ---

    /// <summary>
    /// Creates a perspective projection matrix.
    /// </summary>
    /// <param name="fovRadians">Vertical field of view in radians.</param>
    /// <param name="aspect">Aspect ratio (width / height).</param>
    /// <param name="near">Near clipping plane distance.</param>
    /// <param name="far">Far clipping plane distance.</param>
    public static Mat4d CreatePerspectiveFieldOfView(double fovRadians, double aspect, double near, double far)
    {
        double tanHalfFov = System.Math.Tan(fovRadians * 0.5);
        double range = far - near;

        return new Mat4d(
            1.0 / (aspect * tanHalfFov), 0, 0, 0,
            0, 1.0 / tanHalfFov, 0, 0,
            0, 0, -(far + near) / range, -(2.0 * far * near) / range,
            0, 0, -1, 0
        );
    }

    /// <summary>
    /// Creates a view matrix looking from <paramref name="eye"/> toward <paramref name="target"/>.
    /// </summary>
    public static Mat4d CreateLookAt(Vec3d eye, Vec3d target, Vec3d up)
    {
        Vec3d f = (target - eye).Normalized();   // forward
        Vec3d r = Vec3d.Cross(f, up).Normalized(); // right
        Vec3d u = Vec3d.Cross(r, f);               // recalculated up

        return new Mat4d(
             r.X,  r.Y,  r.Z, -Vec3d.Dot(r, eye),
             u.X,  u.Y,  u.Z, -Vec3d.Dot(u, eye),
            -f.X, -f.Y, -f.Z,  Vec3d.Dot(f, eye),
                0,    0,    0,  1
        );
    }

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    public static Mat4d CreateTranslation(Vec3d v) => new(
        1, 0, 0, v.X,
        0, 1, 0, v.Y,
        0, 0, 1, v.Z,
        0, 0, 0, 1
    );

    /// <summary>
    /// Creates a non-uniform scale matrix.
    /// </summary>
    public static Mat4d CreateScale(Vec3d v) => new(
        v.X, 0,   0,   0,
        0,   v.Y, 0,   0,
        0,   0,   v.Z, 0,
        0,   0,   0,   1
    );

    // --- Operators ---

    /// <summary>
    /// Matrix multiplication (row-major convention).
    /// </summary>
    public static Mat4d operator *(Mat4d a, Mat4d b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32,
        a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33,

        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
        a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
        a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

        a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30,
        a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
        a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
        a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
    );

    // --- Instance methods ---

    /// <summary>
    /// Transforms a 3D point by this matrix (assumes w = 1, performs perspective divide).
    /// </summary>
    public Vec3d TransformPoint(Vec3d p)
    {
        double x = M00 * p.X + M01 * p.Y + M02 * p.Z + M03;
        double y = M10 * p.X + M11 * p.Y + M12 * p.Z + M13;
        double z = M20 * p.X + M21 * p.Y + M22 * p.Z + M23;
        double w = M30 * p.X + M31 * p.Y + M32 * p.Z + M33;

        if (System.Math.Abs(w) > double.Epsilon)
        {
            double invW = 1.0 / w;
            return new Vec3d(x * invW, y * invW, z * invW);
        }

        return new Vec3d(x, y, z);
    }

    /// <summary>
    /// Converts to single-precision <see cref="Matrix4x4"/> for GL interop.
    /// Note: <see cref="Matrix4x4"/> uses a column-major field naming convention
    /// (M{row}{col} with 1-based indexing), so we map accordingly.
    /// </summary>
    public Matrix4x4 ToMatrix4x4() => new(
        (float)M00, (float)M01, (float)M02, (float)M03,
        (float)M10, (float)M11, (float)M12, (float)M13,
        (float)M20, (float)M21, (float)M22, (float)M23,
        (float)M30, (float)M31, (float)M32, (float)M33
    );
}
