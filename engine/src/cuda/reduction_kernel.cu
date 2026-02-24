#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <cstdio>
#include <vector>

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

__device__ double block_reduce_max(double val) {
    __shared__ double shared[32]; // One per warp
    int lane = threadIdx.x % warpSize;
    int warp_id = threadIdx.x / warpSize;

    val = warp_reduce_max(val);
    if (lane == 0) shared[warp_id] = val;
    __syncthreads();

    val = (threadIdx.x < blockDim.x / warpSize) ? shared[lane] : 0.0;
    if (warp_id == 0) val = warp_reduce_max(val);
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

// --------------------------------------------------------------------------
// Max acceleration magnitude reduction kernel (Phase 13 — adaptive timestep)
// --------------------------------------------------------------------------

__global__ void compute_max_accel_kernel(
    const double* __restrict__ acc_x,
    const double* __restrict__ acc_y,
    const double* __restrict__ acc_z,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_max_per_block)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double a_mag = 0.0;
    if (i < n && is_active[i]) {
        double ax = acc_x[i], ay = acc_y[i], az = acc_z[i];
        a_mag = sqrt(ax * ax + ay * ay + az * az);
    }

    a_mag = block_reduce_max(a_mag);
    if (threadIdx.x == 0) {
        out_max_per_block[blockIdx.x] = a_mag;
    }
}

// --------------------------------------------------------------------------
// Angular momentum reduction kernel (Phase 13 — energy tracker)
// L = sum_i[ m_i * (r_i x v_i) ]
// --------------------------------------------------------------------------

__global__ void compute_angular_momentum_kernel(
    const double* __restrict__ pos_x, const double* __restrict__ pos_y, const double* __restrict__ pos_z,
    const double* __restrict__ vel_x, const double* __restrict__ vel_y, const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_lx, double* __restrict__ out_ly, double* __restrict__ out_lz)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double lx = 0.0, ly = 0.0, lz = 0.0;
    if (i < n && is_active[i]) {
        double mi = mass[i];
        // L = m * (r x v)
        lx = mi * (pos_y[i] * vel_z[i] - pos_z[i] * vel_y[i]);
        ly = mi * (pos_z[i] * vel_x[i] - pos_x[i] * vel_z[i]);
        lz = mi * (pos_x[i] * vel_y[i] - pos_y[i] * vel_x[i]);
    }

    lx = block_reduce_sum(lx);
    ly = block_reduce_sum(ly);
    lz = block_reduce_sum(lz);

    if (threadIdx.x == 0) {
        out_lx[blockIdx.x] = lx;
        out_ly[blockIdx.x] = ly;
        out_lz[blockIdx.x] = lz;
    }
}

// --------------------------------------------------------------------------
// Center-of-mass reduction kernel (Phase 13 — energy tracker)
// COM = sum(m_i * r_i) / sum(m_i)
// --------------------------------------------------------------------------

__global__ void compute_com_kernel(
    const double* __restrict__ pos_x, const double* __restrict__ pos_y, const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    double* __restrict__ out_mx, double* __restrict__ out_my, double* __restrict__ out_mz,
    double* __restrict__ out_total_mass)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double mx = 0.0, my = 0.0, mz = 0.0, m = 0.0;
    if (i < n && is_active[i]) {
        double mi = mass[i];
        mx = mi * pos_x[i];
        my = mi * pos_y[i];
        mz = mi * pos_z[i];
        m = mi;
    }

    mx = block_reduce_sum(mx);
    my = block_reduce_sum(my);
    mz = block_reduce_sum(mz);
    m = block_reduce_sum(m);

    if (threadIdx.x == 0) {
        out_mx[blockIdx.x] = mx;
        out_my[blockIdx.x] = my;
        out_mz[blockIdx.x] = mz;
        out_total_mass[blockIdx.x] = m;
    }
}

// --------------------------------------------------------------------------
// Host-side launch functions
// --------------------------------------------------------------------------

void launch_max_accel_reduction(
    const double* d_acc_x, const double* d_acc_y, const double* d_acc_z,
    const uint8_t* d_is_active, int32_t n,
    double* d_scratch,
    double& out_max_accel,
    cudaStream_t stream)
{
    if (n <= 0) { out_max_accel = 0.0; return; }

    int block = ReductionKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    compute_max_accel_kernel<<<grid, block, 0, stream>>>(
        d_acc_x, d_acc_y, d_acc_z, d_is_active, n, d_scratch);
    CUDA_CHECK(cudaGetLastError());

    // Download per-block results and finalize on host (deterministic)
    std::vector<double> h_block_max(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_block_max.data(), d_scratch,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_max_accel = 0.0;
    for (int b = 0; b < grid; b++) {
        if (h_block_max[b] > out_max_accel) out_max_accel = h_block_max[b];
    }
}

void launch_angular_momentum_reduction(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_is_active, int32_t n,
    double* d_scratch_lx, double* d_scratch_ly, double* d_scratch_lz,
    double& out_lx, double& out_ly, double& out_lz,
    cudaStream_t stream)
{
    if (n <= 0) { out_lx = out_ly = out_lz = 0.0; return; }

    int block = ReductionKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    compute_angular_momentum_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z,
        d_vel_x, d_vel_y, d_vel_z,
        d_mass, d_is_active, n,
        d_scratch_lx, d_scratch_ly, d_scratch_lz);
    CUDA_CHECK(cudaGetLastError());

    // Download and sum on host (deterministic: fixed block order)
    std::vector<double> h_lx(grid), h_ly(grid), h_lz(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_lx.data(), d_scratch_lx, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(h_ly.data(), d_scratch_ly, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(h_lz.data(), d_scratch_lz, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_lx = 0.0; out_ly = 0.0; out_lz = 0.0;
    for (int b = 0; b < grid; b++) {
        out_lx += h_lx[b];
        out_ly += h_ly[b];
        out_lz += h_lz[b];
    }
}

void launch_com_reduction(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_mass, const uint8_t* d_is_active, int32_t n,
    double* d_scratch_mx, double* d_scratch_my, double* d_scratch_mz, double* d_scratch_m,
    double& out_com_x, double& out_com_y, double& out_com_z, double& out_total_mass,
    cudaStream_t stream)
{
    if (n <= 0) { out_com_x = out_com_y = out_com_z = out_total_mass = 0.0; return; }

    int block = ReductionKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    compute_com_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active, n,
        d_scratch_mx, d_scratch_my, d_scratch_mz, d_scratch_m);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_mx(grid), h_my(grid), h_mz(grid), h_m(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_mx.data(), d_scratch_mx, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(h_my.data(), d_scratch_my, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(h_mz.data(), d_scratch_mz, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(h_m.data(), d_scratch_m, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    double sum_mx = 0.0, sum_my = 0.0, sum_mz = 0.0, sum_m = 0.0;
    for (int b = 0; b < grid; b++) {
        sum_mx += h_mx[b];
        sum_my += h_my[b];
        sum_mz += h_mz[b];
        sum_m += h_m[b];
    }

    out_total_mass = sum_m;
    if (sum_m > 0.0) {
        out_com_x = sum_mx / sum_m;
        out_com_y = sum_my / sum_m;
        out_com_z = sum_mz / sum_m;
    } else {
        out_com_x = out_com_y = out_com_z = 0.0;
    }
}

} // namespace celestial::cuda
