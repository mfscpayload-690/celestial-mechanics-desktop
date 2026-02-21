#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <cstdio>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// Warp-level reduction primitives
// --------------------------------------------------------------------------

__device__ double warp_reduce_sum(double val) {
    for (int offset = warpSize / 2; offset > 0; offset /= 2) {
        val += __shfl_down_sync(0xFFFFFFFF, val, offset);
    }
    return val;
}

__device__ double warp_reduce_min(double val) {
    for (int offset = warpSize / 2; offset > 0; offset /= 2) {
        double other = __shfl_down_sync(0xFFFFFFFF, val, offset);
        val = fmin(val, other);
    }
    return val;
}

__device__ double warp_reduce_max(double val) {
    for (int offset = warpSize / 2; offset > 0; offset /= 2) {
        double other = __shfl_down_sync(0xFFFFFFFF, val, offset);
        val = fmax(val, other);
    }
    return val;
}

__device__ double block_reduce_sum(double val) {
    __shared__ double shared[32]; // One per warp
    int lane = threadIdx.x % warpSize;
    int warp_id = threadIdx.x / warpSize;

    val = warp_reduce_sum(val);
    if (lane == 0) shared[warp_id] = val;
    __syncthreads();

    val = (threadIdx.x < blockDim.x / warpSize) ? shared[lane] : 0.0;
    if (warp_id == 0) val = warp_reduce_sum(val);
    return val;
}

// --------------------------------------------------------------------------
// Bounding box reduction kernel
// --------------------------------------------------------------------------

__global__ void compute_bounding_box_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_min_x, double* __restrict__ out_min_y, double* __restrict__ out_min_z,
    double* __restrict__ out_max_x, double* __restrict__ out_max_y, double* __restrict__ out_max_z)
{
    __shared__ double s_min_x[32], s_min_y[32], s_min_z[32];
    __shared__ double s_max_x[32], s_max_y[32], s_max_z[32];

    int i = blockIdx.x * blockDim.x + threadIdx.x;
    int lane = threadIdx.x % warpSize;
    int warp_id = threadIdx.x / warpSize;

    double local_min_x = 1e308, local_min_y = 1e308, local_min_z = 1e308;
    double local_max_x = -1e308, local_max_y = -1e308, local_max_z = -1e308;

    if (i < n && is_active[i]) {
        local_min_x = local_max_x = pos_x[i];
        local_min_y = local_max_y = pos_y[i];
        local_min_z = local_max_z = pos_z[i];
    }

    // Warp reduction
    local_min_x = warp_reduce_min(local_min_x);
    local_min_y = warp_reduce_min(local_min_y);
    local_min_z = warp_reduce_min(local_min_z);
    local_max_x = warp_reduce_max(local_max_x);
    local_max_y = warp_reduce_max(local_max_y);
    local_max_z = warp_reduce_max(local_max_z);

    if (lane == 0) {
        s_min_x[warp_id] = local_min_x; s_min_y[warp_id] = local_min_y; s_min_z[warp_id] = local_min_z;
        s_max_x[warp_id] = local_max_x; s_max_y[warp_id] = local_max_y; s_max_z[warp_id] = local_max_z;
    }
    __syncthreads();

    // Block reduction (first warp only)
    if (warp_id == 0) {
        local_min_x = (lane < blockDim.x / warpSize) ? s_min_x[lane] : 1e308;
        local_min_y = (lane < blockDim.x / warpSize) ? s_min_y[lane] : 1e308;
        local_min_z = (lane < blockDim.x / warpSize) ? s_min_z[lane] : 1e308;
        local_max_x = (lane < blockDim.x / warpSize) ? s_max_x[lane] : -1e308;
        local_max_y = (lane < blockDim.x / warpSize) ? s_max_y[lane] : -1e308;
        local_max_z = (lane < blockDim.x / warpSize) ? s_max_z[lane] : -1e308;

        local_min_x = warp_reduce_min(local_min_x);
        local_min_y = warp_reduce_min(local_min_y);
        local_min_z = warp_reduce_min(local_min_z);
        local_max_x = warp_reduce_max(local_max_x);
        local_max_y = warp_reduce_max(local_max_y);
        local_max_z = warp_reduce_max(local_max_z);

        if (lane == 0) {
            // Atomic min/max using atomicCAS on doubles
            // (simplified: write per-block results; host does final reduction)
            out_min_x[blockIdx.x] = local_min_x;
            out_min_y[blockIdx.x] = local_min_y;
            out_min_z[blockIdx.x] = local_min_z;
            out_max_x[blockIdx.x] = local_max_x;
            out_max_y[blockIdx.x] = local_max_y;
            out_max_z[blockIdx.x] = local_max_z;
        }
    }
}

// --------------------------------------------------------------------------
// Total energy reduction kernel
// --------------------------------------------------------------------------

__global__ void compute_kinetic_energy_kernel(
    const double* __restrict__ vel_x,
    const double* __restrict__ vel_y,
    const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_ke_per_block)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double ke = 0.0;
    if (i < n && is_active[i]) {
        double v2 = vel_x[i] * vel_x[i] + vel_y[i] * vel_y[i] + vel_z[i] * vel_z[i];
        ke = 0.5 * mass[i] * v2;
    }

    ke = block_reduce_sum(ke);
    if (threadIdx.x == 0) {
        out_ke_per_block[blockIdx.x] = ke;
    }
}

// --------------------------------------------------------------------------
// Total momentum reduction kernel
// --------------------------------------------------------------------------

__global__ void compute_momentum_kernel(
    const double* __restrict__ vel_x,
    const double* __restrict__ vel_y,
    const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_px, double* __restrict__ out_py, double* __restrict__ out_pz)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double px_val = 0.0, py_val = 0.0, pz_val = 0.0;
    if (i < n && is_active[i]) {
        px_val = mass[i] * vel_x[i];
        py_val = mass[i] * vel_y[i];
        pz_val = mass[i] * vel_z[i];
    }

    px_val = block_reduce_sum(px_val);
    py_val = block_reduce_sum(py_val);
    pz_val = block_reduce_sum(pz_val);

    if (threadIdx.x == 0) {
        out_px[blockIdx.x] = px_val;
        out_py[blockIdx.x] = py_val;
        out_pz[blockIdx.x] = pz_val;
    }
}

} // namespace celestial::cuda
