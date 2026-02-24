#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <cstring>

namespace celestial::physics {

/// Structure-of-Arrays particle storage on host.
/// Mirrors C# BodySoA exactly: 18 parallel arrays.
/// Uses pinned (page-locked) memory when CUDA is available for async transfers.
struct CELESTIAL_API ParticleSystem {
    // Position arrays
    double* pos_x = nullptr;
    double* pos_y = nullptr;
    double* pos_z = nullptr;

    // Velocity arrays
    double* vel_x = nullptr;
    double* vel_y = nullptr;
    double* vel_z = nullptr;

    // Current acceleration arrays (filled by force computation)
    double* acc_x = nullptr;
    double* acc_y = nullptr;
    double* acc_z = nullptr;

    // Previous-step acceleration arrays (required by Velocity Verlet)
    double* old_acc_x = nullptr;
    double* old_acc_y = nullptr;
    double* old_acc_z = nullptr;

    // Scalar per-body fields
    double* mass       = nullptr;
    double* radius     = nullptr;
    double* density    = nullptr;
    u8*     is_active  = nullptr;       // bool as uint8_t for CUDA compat
    u8*     is_collidable = nullptr;
    i32*    body_type_index = nullptr;

    i32 count    = 0;
    i32 capacity = 0;

    /// Allocate all 18 arrays for the given capacity.
    /// Uses cudaMallocHost if CUDA is available, else aligned_alloc.
    void allocate(i32 cap);

    /// Free all arrays.
    void free();

    /// Set count (must be <= capacity).
    void set_count(i32 n);

    /// Zero the current acceleration arrays (acc_x/y/z).
    void zero_accelerations();

    /// Rotate: old_acc = acc (preparation for next Verlet step).
    void rotate_accelerations();

    /// Zero all arrays.
    void clear();

    /// Compact: shift all active bodies to front, remove inactive gaps.
    /// Returns new count (number of active bodies). Updates count field.
    i32 compact();

    ~ParticleSystem() { free(); }

    // Move-only
    ParticleSystem() = default;
    ParticleSystem(ParticleSystem&& other) noexcept;
    ParticleSystem& operator=(ParticleSystem&& other) noexcept;
    ParticleSystem(const ParticleSystem&) = delete;
    ParticleSystem& operator=(const ParticleSystem&) = delete;

private:
    bool using_pinned_ = false;

    void* alloc_array(usize bytes);
    void  free_array(void* ptr);
};

} // namespace celestial::physics
