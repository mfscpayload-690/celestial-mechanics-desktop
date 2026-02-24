#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/profile/memory_tracker.hpp>
#include <vector>
#include <mutex>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::memory {

/// GPU memory pool with pre-allocated blocks.
/// Initial capacity: 1M bodies. Expandable in blocks of 250k.
/// Fragmentation-free: allocates contiguous SoA arrays.
class CELESTIAL_API GpuPool {
public:
    static constexpr i32 INITIAL_CAPACITY = 1048576;    // 1M
    static constexpr i32 EXPANSION_BLOCK  = 262144;     // 250k
    static constexpr i32 MAX_CAPACITY     = 4194304;    // 4M

    GpuPool() = default;
    ~GpuPool();

    GpuPool(const GpuPool&) = delete;
    GpuPool& operator=(const GpuPool&) = delete;
    GpuPool(GpuPool&&) noexcept;
    GpuPool& operator=(GpuPool&&) noexcept;

    /// Initialize the pool with given initial capacity.
    /// Allocates all device arrays up front.
    void init(i32 initial_capacity = INITIAL_CAPACITY);

    /// Initialize with both particle capacity and scratch pool size.
    void init(i32 initial_capacity, usize scratch_bytes);

    /// Release all device memory.
    void destroy();

    /// Expand the pool to accommodate at least `required_capacity` bodies.
    /// Expands in blocks of EXPANSION_BLOCK, preserving existing data.
    void expand(i32 required_capacity);

    /// Get the current capacity (number of bodies that can be stored).
    i32 capacity() const { return capacity_; }

    /// Get the current active count.
    i32 count() const { return count_; }

    /// Set the active count (must be <= capacity).
    void set_count(i32 n);

    bool is_initialized() const { return initialized_; }

    // ── SoA device pointers ──
    // Position
    double* d_pos_x = nullptr;
    double* d_pos_y = nullptr;
    double* d_pos_z = nullptr;

    // Velocity
    double* d_vel_x = nullptr;
    double* d_vel_y = nullptr;
    double* d_vel_z = nullptr;

    // Acceleration (current)
    double* d_acc_x = nullptr;
    double* d_acc_y = nullptr;
    double* d_acc_z = nullptr;

    // Acceleration (previous step, for Verlet)
    double* d_old_acc_x = nullptr;
    double* d_old_acc_y = nullptr;
    double* d_old_acc_z = nullptr;

    // Scalar fields
    double* d_mass       = nullptr;
    double* d_radius     = nullptr;
    u8*     d_is_active  = nullptr;
    i32*    d_body_type  = nullptr;

    // Phase 13: Per-particle adaptive softening (mode == Adaptive)
    double* d_softening  = nullptr;

    // Phase 13: Collidable flag for GPU broad-phase collision detection
    u8*     d_is_collidable = nullptr;

    // Morton codes for spatial sorting
    u64*    d_morton_codes = nullptr;
    i32*    d_sorted_indices = nullptr;

    // Tree auxiliary buffers
    double* d_bbox_min = nullptr;   // [6] min_x, min_y, min_z, max_x, max_y, max_z
    double* d_bbox_max = nullptr;

    /// Total device memory consumed by this pool (bytes).
    i64 device_memory_bytes() const { return device_bytes_; }

    // ── Scratch allocator (bump allocator for per-frame temporary allocations) ──

    /// Initialize the scratch pool with a fixed byte budget.
    /// Must be called after init(). No device malloc occurs after this.
    void init_scratch(usize bytes);

    /// O(1) bump-allocate from the scratch pool. Returns aligned device pointer.
    /// Alignment is rounded up to 256 bytes for coalesced access.
    /// Returns nullptr if insufficient space remains.
    void* scratch_alloc(usize bytes, usize alignment = 256);

    /// Reset the scratch allocator offset to zero. Call once per frame.
    void scratch_reset() { scratch_offset_ = 0; }

    /// Get remaining scratch space in bytes.
    usize scratch_remaining() const {
        return (scratch_size_ > scratch_offset_) ? (scratch_size_ - scratch_offset_) : 0;
    }

    usize scratch_capacity() const { return scratch_size_; }

#if CELESTIAL_HAS_CUDA
    /// Upload SoA data from host to device (async).
    void upload_all(
        const double* hpx, const double* hpy, const double* hpz,
        const double* hvx, const double* hvy, const double* hvz,
        const double* hax, const double* hay, const double* haz,
        const double* hoax, const double* hoay, const double* hoaz,
        const double* hm, const double* hr,
        const u8* h_active, i32 n, cudaStream_t stream);

    /// Download positions, velocities, accelerations from device (async).
    void download_state(
        double* hpx, double* hpy, double* hpz,
        double* hvx, double* hvy, double* hvz,
        double* hax, double* hay, double* haz,
        i32 n, cudaStream_t stream);

    /// Download old_acc for Verlet continuation (async).
    void download_old_acc(
        double* hoax, double* hoay, double* hoaz,
        i32 n, cudaStream_t stream);

    /// Zero all acceleration arrays on device.
    void zero_accelerations(i32 n, cudaStream_t stream);
#endif

private:
    void allocate_arrays(i32 cap);
    void free_arrays();
    void reallocate_arrays(i32 old_cap, i32 new_cap);

    i32 capacity_ = 0;
    i32 count_ = 0;
    i64 device_bytes_ = 0;
    bool initialized_ = false;

    // Scratch allocator state
    void* d_scratch_ = nullptr;
    usize scratch_size_ = 0;
    usize scratch_offset_ = 0;
};

} // namespace celestial::memory
