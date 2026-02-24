#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// Deterministic GPU reduction framework for Phase 18+19.
///
/// Design:
///   - Two-level reduction: per-block shared-memory tree, then single-block final pass
///   - Fixed block size (256 threads) for deterministic warp scheduling
///   - Pairwise summation within warps (shuffle-down) for reproducible ordering
///   - No unordered global atomicAdd for floating-point values
///   - Final host-side reduction uses sequential pairwise summation
///   - Optional Kahan compensated summation for high-precision accumulation
///
/// Used by: KE, PE, momentum, angular momentum, max acceleration, total mass

struct ReductionConfig {
    static constexpr int BLOCK_SIZE = 256;
    static constexpr int WARPS_PER_BLOCK = BLOCK_SIZE / 32;

    /// Enable Kahan compensated summation on the host-side final reduction.
    /// This adds ~2x cost to the final pass but improves precision for large N.
    static constexpr bool KAHAN_COMPENSATION = true;
};

#if CELESTIAL_HAS_CUDA

// ─── Scalar reductions ──────────────────────────────────────────────────

/// Deterministic sum reduction. Per-block results written to d_block_out.
/// Final summation on host with optional Kahan compensation.
/// Returns total sum in out_result. Synchronizes stream.
void gpu_reduce_sum(
    const double* d_input, const uint8_t* d_active, int32_t n,
    double* d_block_out,
    double& out_result,
    cudaStream_t stream);

/// Deterministic max reduction.
void gpu_reduce_max(
    const double* d_input, const uint8_t* d_active, int32_t n,
    double* d_block_out,
    double& out_result,
    cudaStream_t stream);

// ─── Vector3 reductions ─────────────────────────────────────────────────

/// Deterministic vector3 sum: computes sum(f(i)) for each component.
/// Caller provides d_block_out_{x,y,z} scratch arrays of size ceil(n/256).
void gpu_reduce_sum_vec3(
    const double* d_x, const double* d_y, const double* d_z,
    const double* d_weight,     // per-element weight (e.g., mass), or nullptr for weight=1
    const uint8_t* d_active,
    int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_x, double& out_y, double& out_z,
    cudaStream_t stream);

// ─── Specialized reductions ─────────────────────────────────────────────

/// Kinetic energy: sum(0.5 * m_i * |v_i|^2) for active bodies.
void gpu_reduce_kinetic_energy(
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out,
    double& out_ke,
    cudaStream_t stream);

/// Linear momentum: sum(m_i * v_i) for active bodies.
void gpu_reduce_momentum(
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_px, double& out_py, double& out_pz,
    cudaStream_t stream);

/// Angular momentum: sum(m_i * (r_i x v_i)) for active bodies.
void gpu_reduce_angular_momentum(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_lx, double& out_ly, double& out_lz,
    cudaStream_t stream);

/// Center of mass: sum(m_i * r_i) / sum(m_i) for active bodies.
void gpu_reduce_com(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double* d_block_out_m,
    double& out_cx, double& out_cy, double& out_cz, double& out_total_mass,
    cudaStream_t stream);

/// Max acceleration magnitude: max(|a_i|) for active bodies.
void gpu_reduce_max_accel(
    const double* d_acc_x, const double* d_acc_y, const double* d_acc_z,
    const uint8_t* d_active, int32_t n,
    double* d_block_out,
    double& out_max_accel,
    cudaStream_t stream);

/// Total mass: sum(m_i) for active bodies.
void gpu_reduce_total_mass(
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out,
    double& out_total_mass,
    cudaStream_t stream);

#endif // CELESTIAL_HAS_CUDA

// ─── Host-side deterministic summation ──────────────────────────────────

/// Pairwise summation of an array (deterministic ordering).
double host_pairwise_sum(const double* data, int count);

/// Kahan compensated summation of an array.
double host_kahan_sum(const double* data, int count);

} // namespace celestial::cuda
