#include <celestial/cuda/gpu_reduction.hpp>
#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <vector>
#include <cmath>

namespace celestial::cuda {

// ═════════════════════════════════════════════════════════════════════════
// HOST-SIDE DETERMINISTIC SUMMATION
// ═════════════════════════════════════════════════════════════════════════

double host_pairwise_sum(const double* data, int count) {
    if (count <= 0) return 0.0;
    if (count == 1) return data[0];
    if (count == 2) return data[0] + data[1];
    int mid = count / 2;
    return host_pairwise_sum(data, mid) + host_pairwise_sum(data + mid, count - mid);
}

double host_kahan_sum(const double* data, int count) {
    double sum = 0.0;
    double comp = 0.0;  // compensation for lost low-order bits
    for (int i = 0; i < count; i++) {
        double y = data[i] - comp;
        double t = sum + y;
        comp = (t - sum) - y;
        sum = t;
    }
    return sum;
}

/// Internal: finalize block results to scalar using configured summation method.
static double finalize_sum(const double* h_blocks, int count) {
    if (ReductionConfig::KAHAN_COMPENSATION) {
        return host_kahan_sum(h_blocks, count);
    }
    return host_pairwise_sum(h_blocks, count);
}

static double finalize_max(const double* h_blocks, int count) {
    double result = -1e308;
    for (int i = 0; i < count; i++) {
        if (h_blocks[i] > result) result = h_blocks[i];
    }
    return result;
}

#if CELESTIAL_HAS_CUDA

// ═════════════════════════════════════════════════════════════════════════
// DEVICE-SIDE WARP & BLOCK PRIMITIVES
// Deterministic: fixed 256-thread blocks, pairwise shuffle-down
// ═════════════════════════════════════════════════════════════════════════

__device__ __forceinline__ double det_warp_sum(double val) {
    // Pairwise reduction within warp using shuffle-down.
    // Order is deterministic: offset 16,8,4,2,1 always in same sequence.
    for (int offset = warpSize / 2; offset > 0; offset /= 2) {
        val += __shfl_down_sync(0xFFFFFFFF, val, offset);
    }
    return val;
}

__device__ __forceinline__ double det_warp_max(double val) {
    for (int offset = warpSize / 2; offset > 0; offset /= 2) {
        double other = __shfl_down_sync(0xFFFFFFFF, val, offset);
        val = fmax(val, other);
    }
    return val;
}

__device__ __forceinline__ double det_block_sum(double val) {
    __shared__ double s_warp[ReductionConfig::WARPS_PER_BLOCK]; // 8 warps for 256 threads
    int lane = threadIdx.x % warpSize;
    int warp_id = threadIdx.x / warpSize;

    val = det_warp_sum(val);
    if (lane == 0) s_warp[warp_id] = val;
    __syncthreads();

    // First warp reduces all partial sums (deterministic: exactly WARPS_PER_BLOCK values)
    val = (threadIdx.x < ReductionConfig::WARPS_PER_BLOCK) ? s_warp[threadIdx.x] : 0.0;
    if (warp_id == 0) val = det_warp_sum(val);
    return val;
}

__device__ __forceinline__ double det_block_max(double val) {
    __shared__ double s_warp[ReductionConfig::WARPS_PER_BLOCK];
    int lane = threadIdx.x % warpSize;
    int warp_id = threadIdx.x / warpSize;

    val = det_warp_max(val);
    if (lane == 0) s_warp[warp_id] = val;
    __syncthreads();

    val = (threadIdx.x < ReductionConfig::WARPS_PER_BLOCK) ? s_warp[threadIdx.x] : -1e308;
    if (warp_id == 0) val = det_warp_max(val);
    return val;
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Generic scalar sum reduction
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_sum(
    const double* __restrict__ input,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ block_out)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double val = 0.0;
    if (i < n && (!active || active[i])) {
        val = input[i];
    }
    val = det_block_sum(val);
    if (threadIdx.x == 0) block_out[blockIdx.x] = val;
}

__global__ void k_reduce_max(
    const double* __restrict__ input,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ block_out)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double val = -1e308;
    if (i < n && (!active || active[i])) {
        val = input[i];
    }
    val = det_block_max(val);
    if (threadIdx.x == 0) block_out[blockIdx.x] = val;
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Kinetic energy
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_ke(
    const double* __restrict__ vel_x,
    const double* __restrict__ vel_y,
    const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ block_out)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double ke = 0.0;
    if (i < n && active[i]) {
        double v2 = vel_x[i] * vel_x[i] + vel_y[i] * vel_y[i] + vel_z[i] * vel_z[i];
        ke = 0.5 * mass[i] * v2;
    }
    ke = det_block_sum(ke);
    if (threadIdx.x == 0) block_out[blockIdx.x] = ke;
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Linear momentum (3-component)
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_momentum(
    const double* __restrict__ vel_x,
    const double* __restrict__ vel_y,
    const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ out_px,
    double* __restrict__ out_py,
    double* __restrict__ out_pz)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double px = 0.0, py = 0.0, pz = 0.0;
    if (i < n && active[i]) {
        double mi = mass[i];
        px = mi * vel_x[i];
        py = mi * vel_y[i];
        pz = mi * vel_z[i];
    }
    px = det_block_sum(px);
    py = det_block_sum(py);
    pz = det_block_sum(pz);
    if (threadIdx.x == 0) {
        out_px[blockIdx.x] = px;
        out_py[blockIdx.x] = py;
        out_pz[blockIdx.x] = pz;
    }
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Angular momentum (3-component)
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_angular_momentum(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ vel_x,
    const double* __restrict__ vel_y,
    const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ out_lx,
    double* __restrict__ out_ly,
    double* __restrict__ out_lz)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double lx = 0.0, ly = 0.0, lz = 0.0;
    if (i < n && active[i]) {
        double mi = mass[i];
        lx = mi * (pos_y[i] * vel_z[i] - pos_z[i] * vel_y[i]);
        ly = mi * (pos_z[i] * vel_x[i] - pos_x[i] * vel_z[i]);
        lz = mi * (pos_x[i] * vel_y[i] - pos_y[i] * vel_x[i]);
    }
    lx = det_block_sum(lx);
    ly = det_block_sum(ly);
    lz = det_block_sum(lz);
    if (threadIdx.x == 0) {
        out_lx[blockIdx.x] = lx;
        out_ly[blockIdx.x] = ly;
        out_lz[blockIdx.x] = lz;
    }
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Center of mass (weighted position + total mass)
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_com(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ out_mx,
    double* __restrict__ out_my,
    double* __restrict__ out_mz,
    double* __restrict__ out_m)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double mx = 0.0, my = 0.0, mz = 0.0, m = 0.0;
    if (i < n && active[i]) {
        double mi = mass[i];
        mx = mi * pos_x[i];
        my = mi * pos_y[i];
        mz = mi * pos_z[i];
        m = mi;
    }
    mx = det_block_sum(mx);
    my = det_block_sum(my);
    mz = det_block_sum(mz);
    m  = det_block_sum(m);
    if (threadIdx.x == 0) {
        out_mx[blockIdx.x] = mx;
        out_my[blockIdx.x] = my;
        out_mz[blockIdx.x] = mz;
        out_m[blockIdx.x]  = m;
    }
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Max acceleration magnitude
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_max_accel(
    const double* __restrict__ acc_x,
    const double* __restrict__ acc_y,
    const double* __restrict__ acc_z,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ block_out)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double a_mag = 0.0;
    if (i < n && active[i]) {
        double ax = acc_x[i], ay = acc_y[i], az = acc_z[i];
        a_mag = sqrt(ax * ax + ay * ay + az * az);
    }
    a_mag = det_block_max(a_mag);
    if (threadIdx.x == 0) block_out[blockIdx.x] = a_mag;
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Weighted vec3 sum (generic)
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_weighted_vec3(
    const double* __restrict__ x,
    const double* __restrict__ y,
    const double* __restrict__ z,
    const double* __restrict__ weight,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ out_x,
    double* __restrict__ out_y,
    double* __restrict__ out_z)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double vx = 0.0, vy = 0.0, vz = 0.0;
    if (i < n && (!active || active[i])) {
        double w = weight ? weight[i] : 1.0;
        vx = w * x[i];
        vy = w * y[i];
        vz = w * z[i];
    }
    vx = det_block_sum(vx);
    vy = det_block_sum(vy);
    vz = det_block_sum(vz);
    if (threadIdx.x == 0) {
        out_x[blockIdx.x] = vx;
        out_y[blockIdx.x] = vy;
        out_z[blockIdx.x] = vz;
    }
}

// ═════════════════════════════════════════════════════════════════════════
// KERNEL: Total mass
// ═════════════════════════════════════════════════════════════════════════

__global__ void k_reduce_total_mass(
    const double* __restrict__ mass,
    const uint8_t* __restrict__ active,
    int32_t n,
    double* __restrict__ block_out)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    double m = 0.0;
    if (i < n && active[i]) {
        m = mass[i];
    }
    m = det_block_sum(m);
    if (threadIdx.x == 0) block_out[blockIdx.x] = m;
}

// ═════════════════════════════════════════════════════════════════════════
// HOST LAUNCH FUNCTIONS
// Pattern: launch kernel → sync → download block results → finalize on host
// This guarantees deterministic ordering regardless of GPU scheduling.
// ═════════════════════════════════════════════════════════════════════════

void gpu_reduce_sum(
    const double* d_input, const uint8_t* d_active, int32_t n,
    double* d_block_out, double& out_result, cudaStream_t stream)
{
    if (n <= 0) { out_result = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_sum<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_input, d_active, n, d_block_out);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_blocks(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_blocks.data(), d_block_out,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_result = finalize_sum(h_blocks.data(), grid);
}

void gpu_reduce_max(
    const double* d_input, const uint8_t* d_active, int32_t n,
    double* d_block_out, double& out_result, cudaStream_t stream)
{
    if (n <= 0) { out_result = -1e308; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_max<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_input, d_active, n, d_block_out);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_blocks(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_blocks.data(), d_block_out,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_result = finalize_max(h_blocks.data(), grid);
}

void gpu_reduce_sum_vec3(
    const double* d_x, const double* d_y, const double* d_z,
    const double* d_weight, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_x, double& out_y, double& out_z, cudaStream_t stream)
{
    if (n <= 0) { out_x = out_y = out_z = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_weighted_vec3<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_x, d_y, d_z, d_weight, d_active, n,
        d_block_out_x, d_block_out_y, d_block_out_z);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> hx(grid), hy(grid), hz(grid);
    CUDA_CHECK(cudaMemcpyAsync(hx.data(), d_block_out_x, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hy.data(), d_block_out_y, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hz.data(), d_block_out_z, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_x = finalize_sum(hx.data(), grid);
    out_y = finalize_sum(hy.data(), grid);
    out_z = finalize_sum(hz.data(), grid);
}

void gpu_reduce_kinetic_energy(
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out, double& out_ke, cudaStream_t stream)
{
    if (n <= 0) { out_ke = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_ke<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_vel_x, d_vel_y, d_vel_z, d_mass, d_active, n, d_block_out);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_blocks(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_blocks.data(), d_block_out,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_ke = finalize_sum(h_blocks.data(), grid);
}

void gpu_reduce_momentum(
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_px, double& out_py, double& out_pz, cudaStream_t stream)
{
    if (n <= 0) { out_px = out_py = out_pz = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_momentum<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_vel_x, d_vel_y, d_vel_z, d_mass, d_active, n,
        d_block_out_x, d_block_out_y, d_block_out_z);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> hx(grid), hy(grid), hz(grid);
    CUDA_CHECK(cudaMemcpyAsync(hx.data(), d_block_out_x, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hy.data(), d_block_out_y, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hz.data(), d_block_out_z, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_px = finalize_sum(hx.data(), grid);
    out_py = finalize_sum(hy.data(), grid);
    out_pz = finalize_sum(hz.data(), grid);
}

void gpu_reduce_angular_momentum(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double& out_lx, double& out_ly, double& out_lz, cudaStream_t stream)
{
    if (n <= 0) { out_lx = out_ly = out_lz = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_angular_momentum<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z,
        d_vel_x, d_vel_y, d_vel_z,
        d_mass, d_active, n,
        d_block_out_x, d_block_out_y, d_block_out_z);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> hx(grid), hy(grid), hz(grid);
    CUDA_CHECK(cudaMemcpyAsync(hx.data(), d_block_out_x, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hy.data(), d_block_out_y, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hz.data(), d_block_out_z, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_lx = finalize_sum(hx.data(), grid);
    out_ly = finalize_sum(hy.data(), grid);
    out_lz = finalize_sum(hz.data(), grid);
}

void gpu_reduce_com(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
    double* d_block_out_m,
    double& out_cx, double& out_cy, double& out_cz, double& out_total_mass,
    cudaStream_t stream)
{
    if (n <= 0) { out_cx = out_cy = out_cz = out_total_mass = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_com<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_active, n,
        d_block_out_x, d_block_out_y, d_block_out_z, d_block_out_m);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> hx(grid), hy(grid), hz(grid), hm(grid);
    CUDA_CHECK(cudaMemcpyAsync(hx.data(), d_block_out_x, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hy.data(), d_block_out_y, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hz.data(), d_block_out_z, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hm.data(), d_block_out_m, sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    double sum_mx = finalize_sum(hx.data(), grid);
    double sum_my = finalize_sum(hy.data(), grid);
    double sum_mz = finalize_sum(hz.data(), grid);
    double sum_m  = finalize_sum(hm.data(), grid);

    out_total_mass = sum_m;
    if (sum_m > 0.0) {
        out_cx = sum_mx / sum_m;
        out_cy = sum_my / sum_m;
        out_cz = sum_mz / sum_m;
    } else {
        out_cx = out_cy = out_cz = 0.0;
    }
}

void gpu_reduce_max_accel(
    const double* d_acc_x, const double* d_acc_y, const double* d_acc_z,
    const uint8_t* d_active, int32_t n,
    double* d_block_out, double& out_max_accel, cudaStream_t stream)
{
    if (n <= 0) { out_max_accel = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_max_accel<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_acc_x, d_acc_y, d_acc_z, d_active, n, d_block_out);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_blocks(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_blocks.data(), d_block_out,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_max_accel = finalize_max(h_blocks.data(), grid);
}

void gpu_reduce_total_mass(
    const double* d_mass, const uint8_t* d_active, int32_t n,
    double* d_block_out, double& out_total_mass, cudaStream_t stream)
{
    if (n <= 0) { out_total_mass = 0.0; return; }
    int grid = (n + ReductionConfig::BLOCK_SIZE - 1) / ReductionConfig::BLOCK_SIZE;

    k_reduce_total_mass<<<grid, ReductionConfig::BLOCK_SIZE, 0, stream>>>(
        d_mass, d_active, n, d_block_out);
    CUDA_CHECK(cudaGetLastError());

    std::vector<double> h_blocks(grid);
    CUDA_CHECK(cudaMemcpyAsync(h_blocks.data(), d_block_out,
        sizeof(double) * grid, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaStreamSynchronize(stream));

    out_total_mass = finalize_sum(h_blocks.data(), grid);
}

#endif // CELESTIAL_HAS_CUDA

} // namespace celestial::cuda
