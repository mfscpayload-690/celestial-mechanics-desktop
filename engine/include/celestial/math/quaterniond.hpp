#pragma once

#include <cmath>
#include <celestial/core/platform.hpp>
#include <celestial/math/vec3d.hpp>

namespace celestial::math {

/// Double-precision quaternion for rotations. Header-only, CUDA-compatible.
struct Quaterniond {
    double w = 1.0;
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;

    CELESTIAL_HOST_DEVICE constexpr Quaterniond() = default;
    CELESTIAL_HOST_DEVICE constexpr Quaterniond(double w_, double x_, double y_, double z_)
        : w(w_), x(x_), y(y_), z(z_) {}

    CELESTIAL_HOST_DEVICE static constexpr Quaterniond identity() {
        return {1.0, 0.0, 0.0, 0.0};
    }

    /// Create from axis-angle (axis must be normalized).
    CELESTIAL_HOST_DEVICE static Quaterniond from_axis_angle(const Vec3d& axis, double angle) {
        double half = angle * 0.5;
        double s = std::sin(half);
        return {std::cos(half), axis.x * s, axis.y * s, axis.z * s};
    }

    CELESTIAL_HOST_DEVICE constexpr double length_squared() const {
        return w * w + x * x + y * y + z * z;
    }

    CELESTIAL_HOST_DEVICE double length() const {
        return std::sqrt(length_squared());
    }

    CELESTIAL_HOST_DEVICE Quaterniond normalized() const {
        double len = length();
        if (len < 1e-15) return identity();
        double inv = 1.0 / len;
        return {w * inv, x * inv, y * inv, z * inv};
    }

    CELESTIAL_HOST_DEVICE constexpr Quaterniond conjugate() const {
        return {w, -x, -y, -z};
    }

    /// Hamilton product.
    CELESTIAL_HOST_DEVICE constexpr Quaterniond operator*(const Quaterniond& q) const {
        return {
            w * q.w - x * q.x - y * q.y - z * q.z,
            w * q.x + x * q.w + y * q.z - z * q.y,
            w * q.y - x * q.z + y * q.w + z * q.x,
            w * q.z + x * q.y - y * q.x + z * q.w
        };
    }

    /// Rotate a vector.
    CELESTIAL_HOST_DEVICE constexpr Vec3d rotate(const Vec3d& v) const {
        // q * (0, v) * q^-1  — optimized form
        Vec3d qv{x, y, z};
        Vec3d uv = qv.cross(v);
        Vec3d uuv = qv.cross(uv);
        return v + (uv * w + uuv) * 2.0;
    }

    /// Spherical linear interpolation.
    CELESTIAL_HOST_DEVICE static Quaterniond slerp(const Quaterniond& a, const Quaterniond& b, double t) {
        double dot_val = a.w * b.w + a.x * b.x + a.y * b.y + a.z * b.z;

        Quaterniond b2 = b;
        if (dot_val < 0.0) {
            b2 = {-b.w, -b.x, -b.y, -b.z};
            dot_val = -dot_val;
        }

        if (dot_val > 0.9995) {
            // Quaternions are very close — lerp to avoid division by zero
            return Quaterniond{
                a.w + t * (b2.w - a.w),
                a.x + t * (b2.x - a.x),
                a.y + t * (b2.y - a.y),
                a.z + t * (b2.z - a.z)
            }.normalized();
        }

        double theta = std::acos(dot_val);
        double sin_theta = std::sin(theta);
        double wa = std::sin((1.0 - t) * theta) / sin_theta;
        double wb = std::sin(t * theta) / sin_theta;

        return {
            wa * a.w + wb * b2.w,
            wa * a.x + wb * b2.x,
            wa * a.y + wb * b2.y,
            wa * a.z + wb * b2.z
        };
    }
};

} // namespace celestial::math
