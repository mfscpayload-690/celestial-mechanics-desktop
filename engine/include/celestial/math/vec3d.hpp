#pragma once

#include <cmath>
#include <celestial/core/platform.hpp>
#include <celestial/core/types.hpp>

namespace celestial::math {

/// Double-precision 3D vector. Header-only, constexpr, CUDA-compatible.
/// Memory layout: 24 bytes (x, y, z) — matches C# Vec3d exactly.
struct alignas(8) Vec3d {
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;

    CELESTIAL_HOST_DEVICE constexpr Vec3d() = default;
    CELESTIAL_HOST_DEVICE constexpr Vec3d(double x_, double y_, double z_)
        : x(x_), y(y_), z(z_) {}

    // Arithmetic operators
    CELESTIAL_HOST_DEVICE constexpr Vec3d operator+(const Vec3d& o) const {
        return {x + o.x, y + o.y, z + o.z};
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d operator-(const Vec3d& o) const {
        return {x - o.x, y - o.y, z - o.z};
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d operator*(double s) const {
        return {x * s, y * s, z * s};
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d operator/(double s) const {
        double inv = 1.0 / s;
        return {x * inv, y * inv, z * inv};
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d operator-() const {
        return {-x, -y, -z};
    }

    // Compound assignment
    CELESTIAL_HOST_DEVICE constexpr Vec3d& operator+=(const Vec3d& o) {
        x += o.x; y += o.y; z += o.z; return *this;
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d& operator-=(const Vec3d& o) {
        x -= o.x; y -= o.y; z -= o.z; return *this;
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d& operator*=(double s) {
        x *= s; y *= s; z *= s; return *this;
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d& operator/=(double s) {
        double inv = 1.0 / s;
        x *= inv; y *= inv; z *= inv; return *this;
    }

    // Vector operations
    CELESTIAL_HOST_DEVICE constexpr double dot(const Vec3d& o) const {
        return x * o.x + y * o.y + z * o.z;
    }
    CELESTIAL_HOST_DEVICE constexpr Vec3d cross(const Vec3d& o) const {
        return {
            y * o.z - z * o.y,
            z * o.x - x * o.z,
            x * o.y - y * o.x
        };
    }
    CELESTIAL_HOST_DEVICE constexpr double length_squared() const {
        return x * x + y * y + z * z;
    }
    CELESTIAL_HOST_DEVICE double length() const {
        return std::sqrt(length_squared());
    }
    CELESTIAL_HOST_DEVICE Vec3d normalized() const {
        double len = length();
        return len > 1e-15 ? *this / len : Vec3d{};
    }

    // Comparison
    CELESTIAL_HOST_DEVICE constexpr bool operator==(const Vec3d& o) const {
        return x == o.x && y == o.y && z == o.z;
    }
    CELESTIAL_HOST_DEVICE constexpr bool operator!=(const Vec3d& o) const {
        return !(*this == o);
    }

    // Named constructors
    CELESTIAL_HOST_DEVICE static constexpr Vec3d zero()   { return {0.0, 0.0, 0.0}; }
    CELESTIAL_HOST_DEVICE static constexpr Vec3d unit_x() { return {1.0, 0.0, 0.0}; }
    CELESTIAL_HOST_DEVICE static constexpr Vec3d unit_y() { return {0.0, 1.0, 0.0}; }
    CELESTIAL_HOST_DEVICE static constexpr Vec3d unit_z() { return {0.0, 0.0, 1.0}; }
    CELESTIAL_HOST_DEVICE static constexpr Vec3d one()    { return {1.0, 1.0, 1.0}; }

    /// Distance between two points.
    CELESTIAL_HOST_DEVICE static double distance(const Vec3d& a, const Vec3d& b) {
        return (a - b).length();
    }
    CELESTIAL_HOST_DEVICE static constexpr double distance_squared(const Vec3d& a, const Vec3d& b) {
        Vec3d d = a - b;
        return d.length_squared();
    }

    /// Linear interpolation.
    CELESTIAL_HOST_DEVICE static constexpr Vec3d lerp(const Vec3d& a, const Vec3d& b, double t) {
        return a + (b - a) * t;
    }
};

// Free scalar-on-left multiplication
CELESTIAL_HOST_DEVICE inline constexpr Vec3d operator*(double s, const Vec3d& v) {
    return v * s;
}

} // namespace celestial::math
