#pragma once

#include <cmath>
#include <cstring>
#include <celestial/core/platform.hpp>
#include <celestial/math/vec3d.hpp>

namespace celestial::math {

/// Double-precision 4x4 matrix. Row-major storage. Header-only, CUDA-compatible.
struct Mat4d {
    double m[4][4]{};

    CELESTIAL_HOST_DEVICE constexpr Mat4d() = default;

    /// Identity matrix.
    CELESTIAL_HOST_DEVICE static constexpr Mat4d identity() {
        Mat4d result{};
        result.m[0][0] = 1.0;
        result.m[1][1] = 1.0;
        result.m[2][2] = 1.0;
        result.m[3][3] = 1.0;
        return result;
    }

    /// Translation matrix.
    CELESTIAL_HOST_DEVICE static constexpr Mat4d translation(double tx, double ty, double tz) {
        Mat4d result = identity();
        result.m[0][3] = tx;
        result.m[1][3] = ty;
        result.m[2][3] = tz;
        return result;
    }

    CELESTIAL_HOST_DEVICE static constexpr Mat4d translation(const Vec3d& t) {
        return translation(t.x, t.y, t.z);
    }

    /// Uniform scale matrix.
    CELESTIAL_HOST_DEVICE static constexpr Mat4d scale(double sx, double sy, double sz) {
        Mat4d result{};
        result.m[0][0] = sx;
        result.m[1][1] = sy;
        result.m[2][2] = sz;
        result.m[3][3] = 1.0;
        return result;
    }

    CELESTIAL_HOST_DEVICE static constexpr Mat4d uniform_scale(double s) {
        return scale(s, s, s);
    }

    /// Matrix multiply.
    CELESTIAL_HOST_DEVICE constexpr Mat4d operator*(const Mat4d& b) const {
        Mat4d result{};
        for (int i = 0; i < 4; i++) {
            for (int j = 0; j < 4; j++) {
                double sum = 0.0;
                for (int k = 0; k < 4; k++) {
                    sum += m[i][k] * b.m[k][j];
                }
                result.m[i][j] = sum;
            }
        }
        return result;
    }

    /// Transform a point (w=1).
    CELESTIAL_HOST_DEVICE constexpr Vec3d transform_point(const Vec3d& p) const {
        double w = m[3][0] * p.x + m[3][1] * p.y + m[3][2] * p.z + m[3][3];
        double inv_w = (w != 0.0) ? 1.0 / w : 1.0;
        return {
            (m[0][0] * p.x + m[0][1] * p.y + m[0][2] * p.z + m[0][3]) * inv_w,
            (m[1][0] * p.x + m[1][1] * p.y + m[1][2] * p.z + m[1][3]) * inv_w,
            (m[2][0] * p.x + m[2][1] * p.y + m[2][2] * p.z + m[2][3]) * inv_w
        };
    }

    /// Transform a direction (w=0).
    CELESTIAL_HOST_DEVICE constexpr Vec3d transform_direction(const Vec3d& d) const {
        return {
            m[0][0] * d.x + m[0][1] * d.y + m[0][2] * d.z,
            m[1][0] * d.x + m[1][1] * d.y + m[1][2] * d.z,
            m[2][0] * d.x + m[2][1] * d.y + m[2][2] * d.z
        };
    }

    /// Perspective projection matrix.
    static Mat4d perspective(double fov_radians, double aspect, double near_plane, double far_plane) {
        double f = 1.0 / std::tan(fov_radians * 0.5);
        double range_inv = 1.0 / (near_plane - far_plane);

        Mat4d result{};
        result.m[0][0] = f / aspect;
        result.m[1][1] = f;
        result.m[2][2] = (far_plane + near_plane) * range_inv;
        result.m[2][3] = 2.0 * far_plane * near_plane * range_inv;
        result.m[3][2] = -1.0;
        return result;
    }

    /// Look-at view matrix.
    static Mat4d look_at(const Vec3d& eye, const Vec3d& target, const Vec3d& up) {
        Vec3d f = (target - eye).normalized();
        Vec3d s = f.cross(up).normalized();
        Vec3d u = s.cross(f);

        Mat4d result = identity();
        result.m[0][0] =  s.x; result.m[0][1] =  s.y; result.m[0][2] =  s.z;
        result.m[1][0] =  u.x; result.m[1][1] =  u.y; result.m[1][2] =  u.z;
        result.m[2][0] = -f.x; result.m[2][1] = -f.y; result.m[2][2] = -f.z;
        result.m[0][3] = -s.dot(eye);
        result.m[1][3] = -u.dot(eye);
        result.m[2][3] =  f.dot(eye);
        return result;
    }
};

} // namespace celestial::math
