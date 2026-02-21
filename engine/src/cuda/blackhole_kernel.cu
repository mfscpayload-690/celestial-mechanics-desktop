#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <celestial/core/constants.hpp>

namespace celestial::cuda {

// Device-side constants
__constant__ double d_G_Sim = 1.0;
__constant__ double d_C_Sim2 = celestial::core::C_Sim * celestial::core::C_Sim;
__constant__ double d_Schwarzschild_Factor = 2.0 * 1.0 / (celestial::core::C_Sim * celestial::core::C_Sim);

// --------------------------------------------------------------------------
// 1PN (Einstein-Infeld-Hoffmann) correction kernel
// Port of PostNewtonian1Correction.ComputeCorrection to GPU
// --------------------------------------------------------------------------

__global__ void pn_correction_kernel(
    const double* __restrict__ pos_x, const double* __restrict__ pos_y, const double* __restrict__ pos_z,
    const double* __restrict__ vel_x, const double* __restrict__ vel_y, const double* __restrict__ vel_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    double* __restrict__ acc_x, double* __restrict__ acc_y, double* __restrict__ acc_z,
    int32_t n,
    double eps2,
    double inv_c2,
    double max_vel2,
    double schwarz_factor,
    double schwarz_warning_factor)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) return;

    double xi = pos_x[i], yi = pos_y[i], zi = pos_z[i];
    double vxi = vel_x[i], vyi = vel_y[i], vzi = vel_z[i];
    double mi = mass[i];
    double vi2 = vxi * vxi + vyi * vyi + vzi * vzi;

    double dax = 0.0, day = 0.0, daz = 0.0;

    for (int j = 0; j < n; j++) {
        if (j == i || !is_active[j]) continue;

        double mj = mass[j];
        if (mj <= 0.0) continue;

        double dx = pos_x[j] - xi;
        double dy = pos_y[j] - yi;
        double dz = pos_z[j] - zi;

        double dist2 = dx * dx + dy * dy + dz * dz + eps2;
        double dist = sqrt(dist2);
        double inv_dist = 1.0 / dist;

        // Relative velocity check
        double dvx = vxi - vel_x[j];
        double dvy = vyi - vel_y[j];
        double dvz = vzi - vel_z[j];
        double v_rel2 = dvx * dvx + dvy * dvy + dvz * dvz;
        if (v_rel2 > max_vel2) continue;

        // Schwarzschild proximity check
        double rs = schwarz_factor * (mi + mj);
        if (dist < schwarz_warning_factor * rs) continue;

        // Unit vector from i to j
        double nx = dx * inv_dist;
        double ny = dy * inv_dist;
        double nz = dz * inv_dist;

        double vxj = vel_x[j], vyj = vel_y[j], vzj = vel_z[j];
        double vj2 = vxj * vxj + vyj * vyj + vzj * vzj;

        double vi_dot_vj = vxi * vxj + vyi * vyj + vzi * vzj;
        double n_dot_vi = nx * vxi + ny * vyi + nz * vzi;
        double n_dot_vj = nx * vxj + ny * vyj + nz * vzj;

        double gmi_over_r = d_G_Sim * mi * inv_dist;
        double gmj_over_r = d_G_Sim * mj * inv_dist;

        double prefactor = gmj_over_r * inv_dist * inv_c2;

        double term1 = -vi2 - 2.0 * vj2 + 4.0 * vi_dot_vj
                       + 1.5 * n_dot_vj * n_dot_vj
                       + 5.0 * gmi_over_r + 4.0 * gmj_over_r;

        double term2 = 4.0 * n_dot_vi - 3.0 * n_dot_vj;

        dax += prefactor * (term1 * nx + term2 * dvx);
        day += prefactor * (term1 * ny + term2 * dvy);
        daz += prefactor * (term1 * nz + term2 * dvz);
    }

    // Add corrections to existing accelerations
    acc_x[i] += dax;
    acc_y[i] += day;
    acc_z[i] += daz;
}

// --------------------------------------------------------------------------
// Host-side launch function
// --------------------------------------------------------------------------

void launch_pn_correction(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_mass, uint8_t* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    int32_t n, double softening,
    double max_velocity_fraction_c,
    double schwarz_warning_factor,
    cudaStream_t stream)
{
    if (n <= 0) return;

    double eps2 = softening * softening;
    double inv_c2 = 1.0 / celestial::core::C_Sim2;
    double max_vel = max_velocity_fraction_c * celestial::core::C_Sim;
    double max_vel2 = max_vel * max_vel;
    double schwarz_factor = celestial::core::SchwarzschildFactorSim;

    int block = PNKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    pn_correction_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z,
        d_vel_x, d_vel_y, d_vel_z,
        d_mass, d_is_active,
        d_acc_x, d_acc_y, d_acc_z,
        n, eps2, inv_c2, max_vel2, schwarz_factor, schwarz_warning_factor);

    CUDA_CHECK(cudaGetLastError());
}

} // namespace celestial::cuda
