#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// GPU broad-phase collision detection using Morton-code spatial proximity.
// After Morton sorting, spatially close bodies are adjacent in the sorted
// array. Each thread checks K neighbors for sphere-sphere overlap.
// --------------------------------------------------------------------------

static constexpr int COLLISION_BLOCK = 256;
static constexpr int COLLISION_WINDOW = 32;

__global__ void broad_phase_collision_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ radius,
    const uint8_t* __restrict__ is_active,
    const uint8_t* __restrict__ is_collidable,
    const int32_t* __restrict__ sorted_indices,
    int32_t n,
    int32_t* __restrict__ out_pair_a,
    int32_t* __restrict__ out_pair_b,
    double* __restrict__ out_pair_dist,
    int32_t* __restrict__ out_pair_count,
    int32_t max_pairs)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= n) return;

    int i = sorted_indices[tid];
    if (!is_active[i] || !is_collidable[i]) return;

    double xi = pos_x[i], yi = pos_y[i], zi = pos_z[i];
    double ri = radius[i];

    int end = min(tid + COLLISION_WINDOW, n);

    for (int jt = tid + 1; jt < end; jt++) {
        int j = sorted_indices[jt];
        if (!is_active[j] || !is_collidable[j]) continue;

        double dx = xi - pos_x[j];
        double dy = yi - pos_y[j];
        double dz = zi - pos_z[j];
        double dist2 = dx * dx + dy * dy + dz * dz;
        double sum_r = ri + radius[j];

        if (dist2 < sum_r * sum_r) {
            int idx = atomicAdd(out_pair_count, 1);
            if (idx < max_pairs) {
                // Store with canonical ordering for deterministic post-sort
                int lo = min(i, j);
                int hi = max(i, j);
                out_pair_a[idx] = lo;
                out_pair_b[idx] = hi;
                out_pair_dist[idx] = sqrt(dist2);
            }
        }
    }
}

// --------------------------------------------------------------------------
// Host-side launch function
// --------------------------------------------------------------------------

void launch_broad_phase_collision(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_radius,
    const uint8_t* d_is_active, const uint8_t* d_is_collidable,
    const int32_t* d_sorted_indices,
    int32_t n,
    int32_t* d_pair_a, int32_t* d_pair_b, double* d_pair_dist,
    int32_t* d_pair_count,
    int32_t max_pairs,
    cudaStream_t stream)
{
    if (n <= 0) return;

    int grid = (n + COLLISION_BLOCK - 1) / COLLISION_BLOCK;

    // Reset pair count
    CUDA_CHECK(cudaMemsetAsync(d_pair_count, 0, sizeof(int32_t), stream));

    broad_phase_collision_kernel<<<grid, COLLISION_BLOCK, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_radius,
        d_is_active, d_is_collidable,
        d_sorted_indices, n,
        d_pair_a, d_pair_b, d_pair_dist,
        d_pair_count, max_pairs);

    CUDA_CHECK(cudaGetLastError());
}

} // namespace celestial::cuda
