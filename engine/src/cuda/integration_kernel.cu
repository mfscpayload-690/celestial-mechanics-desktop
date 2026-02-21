#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// Leapfrog Phase 1+2: Half-kick + Drift (before gravity kernel)
// --------------------------------------------------------------------------

__global__ void leapfrog_kick_drift_kernel(
    double* __restrict__ pos_x, double* __restrict__ pos_y, double* __restrict__ pos_z,
    double* __restrict__ vel_x, double* __restrict__ vel_y, double* __restrict__ vel_z,
    const double* __restrict__ old_acc_x, const double* __restrict__ old_acc_y, const double* __restrict__ old_acc_z,
    const uint8_t* __restrict__ is_active,
    int32_t n, double dt)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) return;

    double half_dt = 0.5 * dt;

    // Half-kick: vel(t+dt/2) = vel(t) + 0.5 * a(t) * dt
    double vx = vel_x[i] + half_dt * old_acc_x[i];
    double vy = vel_y[i] + half_dt * old_acc_y[i];
    double vz = vel_z[i] + half_dt * old_acc_z[i];

    // Drift: pos(t+dt) = pos(t) + vel(t+dt/2) * dt
    pos_x[i] += dt * vx;
    pos_y[i] += dt * vy;
    pos_z[i] += dt * vz;

    // Store updated velocity
    vel_x[i] = vx;
    vel_y[i] = vy;
    vel_z[i] = vz;
}

// --------------------------------------------------------------------------
// Leapfrog Phase 4+5: Second half-kick + Rotate (after gravity kernel)
// --------------------------------------------------------------------------

__global__ void leapfrog_kick_rotate_kernel(
    double* __restrict__ vel_x, double* __restrict__ vel_y, double* __restrict__ vel_z,
    const double* __restrict__ acc_x, const double* __restrict__ acc_y, const double* __restrict__ acc_z,
    double* __restrict__ old_acc_x, double* __restrict__ old_acc_y, double* __restrict__ old_acc_z,
    const uint8_t* __restrict__ is_active,
    int32_t n, double dt)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) return;

    double half_dt = 0.5 * dt;

    // Second half-kick: vel(t+dt) = vel(t+dt/2) + 0.5 * a(t+dt) * dt
    vel_x[i] += half_dt * acc_x[i];
    vel_y[i] += half_dt * acc_y[i];
    vel_z[i] += half_dt * acc_z[i];

    // Rotate: old_acc = acc (for next step's first half-kick)
    old_acc_x[i] = acc_x[i];
    old_acc_y[i] = acc_y[i];
    old_acc_z[i] = acc_z[i];
}

// --------------------------------------------------------------------------
// Host-side launch functions
// --------------------------------------------------------------------------

void launch_kick_drift(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
    uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream)
{
    if (n <= 0) return;
    int block = IntegrationKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    leapfrog_kick_drift_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z,
        d_vel_x, d_vel_y, d_vel_z,
        d_old_acc_x, d_old_acc_y, d_old_acc_z,
        d_is_active, n, dt);

    CUDA_CHECK(cudaGetLastError());
}

void launch_kick_rotate(
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
    uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream)
{
    if (n <= 0) return;
    int block = IntegrationKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    leapfrog_kick_rotate_kernel<<<grid, block, 0, stream>>>(
        d_vel_x, d_vel_y, d_vel_z,
        d_acc_x, d_acc_y, d_acc_z,
        d_old_acc_x, d_old_acc_y, d_old_acc_z,
        d_is_active, n, dt);

    CUDA_CHECK(cudaGetLastError());
}

} // namespace celestial::cuda
