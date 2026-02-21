#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <cmath>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// Fibonacci-sphere ejecta spawn kernel
// Port of CatastrophicEventSystem ejecta generation
// --------------------------------------------------------------------------

__global__ void spawn_ejecta_kernel(
    double* __restrict__ out_px, double* __restrict__ out_py, double* __restrict__ out_pz,
    double* __restrict__ out_vx, double* __restrict__ out_vy, double* __restrict__ out_vz,
    double* __restrict__ out_mass,
    uint8_t* __restrict__ out_active,
    double center_x, double center_y, double center_z,
    double base_speed, double mass_per_ejecta,
    int num_ejecta, double min_offset, double speed_variation,
    unsigned int rng_seed)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= num_ejecta) return;

    // Fibonacci sphere direction
    double phi = acos(1.0 - 2.0 * (i + 0.5) / num_ejecta);
    double golden = M_PI * (1.0 + sqrt(5.0));
    double theta = golden * i;

    double sin_phi = sin(phi);
    double dx = sin_phi * cos(theta);
    double dy = sin_phi * sin(theta);
    double dz = cos(phi);

    // Simple LCG-based per-thread speed variation
    unsigned int state = rng_seed ^ (i * 2654435761u);
    state = state * 1664525u + 1013904223u;
    double rand_01 = (state & 0xFFFFFF) / (double)0xFFFFFF;
    double speed = base_speed * (1.0 + speed_variation * (rand_01 - 0.5));

    out_px[i] = center_x + dx * min_offset;
    out_py[i] = center_y + dy * min_offset;
    out_pz[i] = center_z + dz * min_offset;
    out_vx[i] = dx * speed;
    out_vy[i] = dy * speed;
    out_vz[i] = dz * speed;
    out_mass[i] = mass_per_ejecta;
    out_active[i] = 1;
}

// --------------------------------------------------------------------------
// Host-side launch function
// --------------------------------------------------------------------------

void launch_ejecta_kernel(
    double* d_px, double* d_py, double* d_pz,
    double* d_vx, double* d_vy, double* d_vz,
    double* d_mass, uint8_t* d_active,
    double center_x, double center_y, double center_z,
    double base_speed, double mass_per_ejecta,
    int num_ejecta, double min_offset, double speed_variation,
    unsigned int rng_seed, cudaStream_t stream)
{
    if (num_ejecta <= 0) return;
    int block = EjectaKernelConfig::BLOCK_SIZE;
    int grid = (num_ejecta + block - 1) / block;

    spawn_ejecta_kernel<<<grid, block, 0, stream>>>(
        d_px, d_py, d_pz, d_vx, d_vy, d_vz, d_mass, d_active,
        center_x, center_y, center_z,
        base_speed, mass_per_ejecta,
        num_ejecta, min_offset, speed_variation, rng_seed);

    CUDA_CHECK(cudaGetLastError());
}

} // namespace celestial::cuda
