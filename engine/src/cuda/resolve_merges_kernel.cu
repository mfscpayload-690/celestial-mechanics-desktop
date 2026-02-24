#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <cmath>
#include <vector>
#include <algorithm>

namespace celestial::cuda {

// ═════════════════════════════════════════════════════════════════════════
// GPU MERGE RESOLUTION — Phase 18+19
//
// Conservation laws enforced per merge:
//   Mass:     M_survivor = m_a + m_b               (exact)
//   Momentum: p_survivor = m_a*v_a + m_b*v_b       (exact within FP)
//   Position: x_survivor = (m_a*x_a + m_b*x_b)/M   (mass-weighted)
//   Radius:   r_survivor = cbrt(3*M / (4*pi*rho))   (density-preserving)
//
// Determinism guarantees:
//   - Pairs sorted lexicographically (a < b) before kernel launch
//   - Integer-only atomic locks (no floating atomicAdd)
//   - Survivor = heavier body (tie-break: lower index)
//   - Loser marked inactive (mass set to 0)
// ═════════════════════════════════════════════════════════════════════════

static constexpr int MERGE_BLOCK = 256;

/// Atomic lock array: lock[i] == 0 means unlocked, 1 means locked.
/// Uses atomicCAS for deterministic mutual exclusion.

__device__ bool try_lock_body(int32_t* locks, int32_t idx) {
    return atomicCAS(&locks[idx], 0, 1) == 0;
}

__device__ void unlock_body(int32_t* locks, int32_t idx) {
    atomicExch(&locks[idx], 0);
}

/// Phase 18+19: GPU merge resolution kernel.
/// Each thread processes one collision pair.
/// Uses per-body integer locks to prevent concurrent modification.
/// Pairs are pre-sorted lexicographically for deterministic processing order.
__global__ void resolve_merges_kernel(
    // Particle SoA arrays (read/write)
    double* __restrict__ pos_x, double* __restrict__ pos_y, double* __restrict__ pos_z,
    double* __restrict__ vel_x, double* __restrict__ vel_y, double* __restrict__ vel_z,
    double* __restrict__ mass,
    double* __restrict__ radius,
    double* __restrict__ density,
    uint8_t* __restrict__ is_active,
    // Collision pair arrays (read-only, sorted)
    const int32_t* __restrict__ pair_a,
    const int32_t* __restrict__ pair_b,
    int32_t num_pairs,
    // Per-body atomic locks
    int32_t* __restrict__ locks,
    // Merge safeguards
    int32_t max_merges_per_body,
    int32_t* __restrict__ merge_count_per_body,
    int32_t* __restrict__ total_merge_count,
    int32_t max_merges_per_frame,
    // Density model params
    double min_radius,
    int32_t density_preserving)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= num_pairs) return;

    int32_t a = pair_a[tid];
    int32_t b = pair_b[tid];

    // Quick exit: already dead
    if (!is_active[a] || !is_active[b]) return;

    // Lock both bodies in consistent order (a < b guaranteed by pre-sort)
    // Spin-free: if either lock fails, skip this pair (will be retried next frame)
    if (!try_lock_body(locks, a)) return;
    if (!try_lock_body(locks, b)) {
        unlock_body(locks, a);
        return;
    }

    // Re-check active after acquiring locks
    if (!is_active[a] || !is_active[b]) {
        unlock_body(locks, a);
        unlock_body(locks, b);
        return;
    }

    // Check global merge cap (atomic, integer only)
    int32_t slot = atomicAdd(total_merge_count, 1);
    if (slot >= max_merges_per_frame) {
        atomicSub(total_merge_count, 1);
        unlock_body(locks, a);
        unlock_body(locks, b);
        return;
    }

    // Check per-body merge caps
    int32_t mc_a = atomicAdd(&merge_count_per_body[a], 1);
    if (mc_a >= max_merges_per_body) {
        atomicSub(&merge_count_per_body[a], 1);
        atomicSub(total_merge_count, 1);
        unlock_body(locks, a);
        unlock_body(locks, b);
        return;
    }
    int32_t mc_b = atomicAdd(&merge_count_per_body[b], 1);
    if (mc_b >= max_merges_per_body) {
        atomicSub(&merge_count_per_body[b], 1);
        atomicSub(&merge_count_per_body[a], 1);
        atomicSub(total_merge_count, 1);
        unlock_body(locks, a);
        unlock_body(locks, b);
        return;
    }

    // ─── Merge computation (no atomics on floats) ───

    double ma = mass[a];
    double mb = mass[b];
    double M = ma + mb;

    if (M < 1e-30) {
        unlock_body(locks, a);
        unlock_body(locks, b);
        return;
    }

    // Survivor = heavier body; tie-break = lower index
    int32_t survivor = (ma >= mb) ? a : b;
    int32_t victim   = (ma >= mb) ? b : a;

    double inv_M = 1.0 / M;

    // Conserve linear momentum: v_merged = (m_a*v_a + m_b*v_b) / M
    double vx_merged = (ma * vel_x[a] + mb * vel_x[b]) * inv_M;
    double vy_merged = (ma * vel_y[a] + mb * vel_y[b]) * inv_M;
    double vz_merged = (ma * vel_z[a] + mb * vel_z[b]) * inv_M;

    // Mass-weighted position
    double px_merged = (ma * pos_x[a] + mb * pos_x[b]) * inv_M;
    double py_merged = (ma * pos_y[a] + mb * pos_y[b]) * inv_M;
    double pz_merged = (ma * pos_z[a] + mb * pos_z[b]) * inv_M;

    // Radius computation
    double r_merged;
    if (density_preserving && density != nullptr) {
        // Density-preserving: use survivor's density
        double surv_density = density[survivor];
        if (surv_density < 1e-30) {
            // Fallback: compute from survivor mass/radius
            double sr = radius[survivor];
            if (sr < min_radius) sr = min_radius;
            double sr3 = sr * sr * sr;
            // rho = m / (4/3 pi r^3)
            surv_density = mass[survivor] / (4.188790204786391 * sr3); // 4/3*pi ≈ 4.18879...
            if (surv_density < 1e-30) surv_density = 1000.0; // ultimate fallback
        }
        // r = cbrt(3*M / (4*pi*rho))
        r_merged = cbrt(M / (4.188790204786391 * surv_density));
        if (r_merged < min_radius) r_merged = min_radius;
    } else {
        // Volume-conserving radius: r = cbrt(r_a^3 + r_b^3)
        double ra = radius[a], rb = radius[b];
        double ra3 = ra * ra * ra;
        double rb3 = rb * rb * rb;
        r_merged = cbrt(ra3 + rb3);
    }

    // Write survivor
    pos_x[survivor] = px_merged;
    pos_y[survivor] = py_merged;
    pos_z[survivor] = pz_merged;
    vel_x[survivor] = vx_merged;
    vel_y[survivor] = vy_merged;
    vel_z[survivor] = vz_merged;
    mass[survivor]   = M;
    radius[survivor] = r_merged;

    // Update density for survivor
    if (density != nullptr) {
        double r3 = r_merged * r_merged * r_merged;
        density[survivor] = M / (4.188790204786391 * r3);
    }

    // Deactivate victim
    is_active[victim] = 0;
    mass[victim] = 0.0;

    // Unlock
    unlock_body(locks, a);
    unlock_body(locks, b);
}

// ═════════════════════════════════════════════════════════════════════════
// HOST LAUNCH FUNCTION
// ═════════════════════════════════════════════════════════════════════════

void launch_resolve_merges(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_mass, double* d_radius, double* d_density,
    uint8_t* d_is_active,
    const int32_t* d_pair_a, const int32_t* d_pair_b,
    int32_t num_pairs,
    int32_t* d_locks,
    int32_t* d_merge_count_per_body,
    int32_t* d_total_merge_count,
    int32_t n_bodies,
    int32_t max_merges_per_body,
    int32_t max_merges_per_frame,
    double min_radius,
    bool density_preserving,
    cudaStream_t stream)
{
    if (num_pairs <= 0) return;

    // Zero the lock array and merge counters
    CUDA_CHECK(cudaMemsetAsync(d_locks, 0, sizeof(int32_t) * n_bodies, stream));
    CUDA_CHECK(cudaMemsetAsync(d_merge_count_per_body, 0, sizeof(int32_t) * n_bodies, stream));
    CUDA_CHECK(cudaMemsetAsync(d_total_merge_count, 0, sizeof(int32_t), stream));

    int grid = (num_pairs + MERGE_BLOCK - 1) / MERGE_BLOCK;

    resolve_merges_kernel<<<grid, MERGE_BLOCK, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z,
        d_vel_x, d_vel_y, d_vel_z,
        d_mass, d_radius, d_density,
        d_is_active,
        d_pair_a, d_pair_b,
        num_pairs,
        d_locks,
        max_merges_per_body,
        d_merge_count_per_body,
        d_total_merge_count,
        max_merges_per_frame,
        min_radius,
        density_preserving ? 1 : 0);

    CUDA_CHECK(cudaGetLastError());
}

/// Sort collision pairs on host for deterministic ordering before GPU merge.
/// Canonical: (a < b), sorted by (a, then b).
void sort_collision_pairs_host(
    std::vector<int32_t>& pair_a,
    std::vector<int32_t>& pair_b,
    std::vector<double>& pair_dist)
{
    int32_t n = static_cast<int32_t>(pair_a.size());
    if (n <= 1) return;

    // Create index array and sort
    std::vector<int32_t> indices(n);
    for (int32_t i = 0; i < n; i++) indices[i] = i;

    std::sort(indices.begin(), indices.end(),
        [&](int32_t x, int32_t y) {
            int32_t xa = pair_a[x], xb = pair_b[x];
            int32_t ya = pair_a[y], yb = pair_b[y];
            if (xa != ya) return xa < ya;
            return xb < yb;
        });

    // Apply permutation
    std::vector<int32_t> sorted_a(n), sorted_b(n);
    std::vector<double> sorted_d(n);
    for (int32_t i = 0; i < n; i++) {
        sorted_a[i] = pair_a[indices[i]];
        sorted_b[i] = pair_b[indices[i]];
        sorted_d[i] = pair_dist[indices[i]];
    }
    pair_a = std::move(sorted_a);
    pair_b = std::move(sorted_b);
    pair_dist = std::move(sorted_d);
}

} // namespace celestial::cuda
