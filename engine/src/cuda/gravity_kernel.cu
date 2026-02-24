#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// Per-body-type softening table in constant memory (16 body types)
// --------------------------------------------------------------------------

static constexpr int MAX_BODY_TYPES = 16;
__constant__ double d_type_softening[MAX_BODY_TYPES];

// --------------------------------------------------------------------------
// Tiled shared-memory gravity kernel — Global softening (original)
// --------------------------------------------------------------------------

static constexpr int TILE_SIZE = GravityKernelConfig::TILE_SIZE;

__global__ void gravity_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    double* __restrict__ acc_x,
    double* __restrict__ acc_y,
    double* __restrict__ acc_z,
    int32_t n,
    double eps2)
{
    __shared__ double s_px[TILE_SIZE];
    __shared__ double s_py[TILE_SIZE];
    __shared__ double s_pz[TILE_SIZE];
    __shared__ double s_m[TILE_SIZE];

    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double ax_i = 0.0, ay_i = 0.0, az_i = 0.0;
    double xi = 0.0, yi = 0.0, zi = 0.0;
    bool active_i = false;

    if (i < n) {
        active_i = is_active[i] != 0;
        xi = pos_x[i];
        yi = pos_y[i];
        zi = pos_z[i];
    }

    int num_tiles = (n + TILE_SIZE - 1) / TILE_SIZE;

    for (int tile = 0; tile < num_tiles; tile++) {
        int j = tile * TILE_SIZE + threadIdx.x;

        // Cooperatively load tile into shared memory
        if (j < n) {
            s_px[threadIdx.x] = pos_x[j];
            s_py[threadIdx.x] = pos_y[j];
            s_pz[threadIdx.x] = pos_z[j];
            s_m[threadIdx.x]  = mass[j];
        } else {
            s_px[threadIdx.x] = 0.0;
            s_py[threadIdx.x] = 0.0;
            s_pz[threadIdx.x] = 0.0;
            s_m[threadIdx.x]  = 0.0;
        }
        __syncthreads();

        // Compute interactions with tile bodies
        if (active_i) {
            #pragma unroll 8
            for (int k = 0; k < TILE_SIZE; k++) {
                double dx = xi - s_px[k];
                double dy = yi - s_py[k];
                double dz = zi - s_pz[k];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = rsqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = s_m[k] * inv_dist3;

                // Branchless: self-interaction yields ~0 force via softening
                ax_i -= factor * dx;
                ay_i -= factor * dy;
                az_i -= factor * dz;
            }
        }
        __syncthreads();
    }

    if (i < n && active_i) {
        acc_x[i] = ax_i;
        acc_y[i] = ay_i;
        acc_z[i] = az_i;
    }
}

// --------------------------------------------------------------------------
// Tiled gravity kernel — Per-body-type softening
// Uses constant-memory d_type_softening[] table. Pairwise eps = avg of
// type eps_a and eps_b. Loads body_type_index into shared memory tile.
// --------------------------------------------------------------------------

__global__ void gravity_kernel_per_type(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    const int32_t* __restrict__ body_type,
    double* __restrict__ acc_x,
    double* __restrict__ acc_y,
    double* __restrict__ acc_z,
    int32_t n)
{
    __shared__ double s_px[TILE_SIZE];
    __shared__ double s_py[TILE_SIZE];
    __shared__ double s_pz[TILE_SIZE];
    __shared__ double s_m[TILE_SIZE];
    __shared__ int32_t s_bt[TILE_SIZE];

    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double ax_i = 0.0, ay_i = 0.0, az_i = 0.0;
    double xi = 0.0, yi = 0.0, zi = 0.0;
    bool active_i = false;
    double eps_i = 0.0;

    if (i < n) {
        active_i = is_active[i] != 0;
        xi = pos_x[i];
        yi = pos_y[i];
        zi = pos_z[i];
        int bt_i = body_type[i];
        eps_i = d_type_softening[bt_i & (MAX_BODY_TYPES - 1)];
    }

    int num_tiles = (n + TILE_SIZE - 1) / TILE_SIZE;

    for (int tile = 0; tile < num_tiles; tile++) {
        int j = tile * TILE_SIZE + threadIdx.x;

        if (j < n) {
            s_px[threadIdx.x] = pos_x[j];
            s_py[threadIdx.x] = pos_y[j];
            s_pz[threadIdx.x] = pos_z[j];
            s_m[threadIdx.x]  = mass[j];
            s_bt[threadIdx.x] = body_type[j];
        } else {
            s_px[threadIdx.x] = 0.0;
            s_py[threadIdx.x] = 0.0;
            s_pz[threadIdx.x] = 0.0;
            s_m[threadIdx.x]  = 0.0;
            s_bt[threadIdx.x] = 0;
        }
        __syncthreads();

        if (active_i) {
            #pragma unroll 8
            for (int k = 0; k < TILE_SIZE; k++) {
                double eps_j = d_type_softening[s_bt[k] & (MAX_BODY_TYPES - 1)];
                double eps_avg = 0.5 * (eps_i + eps_j);
                double eps2 = eps_avg * eps_avg;

                double dx = xi - s_px[k];
                double dy = yi - s_py[k];
                double dz = zi - s_pz[k];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = rsqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = s_m[k] * inv_dist3;

                ax_i -= factor * dx;
                ay_i -= factor * dy;
                az_i -= factor * dz;
            }
        }
        __syncthreads();
    }

    if (i < n && active_i) {
        acc_x[i] = ax_i;
        acc_y[i] = ay_i;
        acc_z[i] = az_i;
    }
}

// --------------------------------------------------------------------------
// Tiled gravity kernel — Adaptive per-particle softening
// Per-particle eps stored in d_softening[] device array. Pairwise eps = avg.
// --------------------------------------------------------------------------

__global__ void gravity_kernel_adaptive(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    const double* __restrict__ per_particle_eps,
    double* __restrict__ acc_x,
    double* __restrict__ acc_y,
    double* __restrict__ acc_z,
    int32_t n)
{
    __shared__ double s_px[TILE_SIZE];
    __shared__ double s_py[TILE_SIZE];
    __shared__ double s_pz[TILE_SIZE];
    __shared__ double s_m[TILE_SIZE];
    __shared__ double s_eps[TILE_SIZE];

    int i = blockIdx.x * blockDim.x + threadIdx.x;

    double ax_i = 0.0, ay_i = 0.0, az_i = 0.0;
    double xi = 0.0, yi = 0.0, zi = 0.0;
    bool active_i = false;
    double eps_i = 0.0;

    if (i < n) {
        active_i = is_active[i] != 0;
        xi = pos_x[i];
        yi = pos_y[i];
        zi = pos_z[i];
        eps_i = per_particle_eps[i];
    }

    int num_tiles = (n + TILE_SIZE - 1) / TILE_SIZE;

    for (int tile = 0; tile < num_tiles; tile++) {
        int j = tile * TILE_SIZE + threadIdx.x;

        if (j < n) {
            s_px[threadIdx.x] = pos_x[j];
            s_py[threadIdx.x] = pos_y[j];
            s_pz[threadIdx.x] = pos_z[j];
            s_m[threadIdx.x]  = mass[j];
            s_eps[threadIdx.x] = per_particle_eps[j];
        } else {
            s_px[threadIdx.x] = 0.0;
            s_py[threadIdx.x] = 0.0;
            s_pz[threadIdx.x] = 0.0;
            s_m[threadIdx.x]  = 0.0;
            s_eps[threadIdx.x] = 0.0;
        }
        __syncthreads();

        if (active_i) {
            #pragma unroll 8
            for (int k = 0; k < TILE_SIZE; k++) {
                double eps_avg = 0.5 * (eps_i + s_eps[k]);
                double eps2 = eps_avg * eps_avg;

                double dx = xi - s_px[k];
                double dy = yi - s_py[k];
                double dz = zi - s_pz[k];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = rsqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = s_m[k] * inv_dist3;

                ax_i -= factor * dx;
                ay_i -= factor * dy;
                az_i -= factor * dz;
            }
        }
        __syncthreads();
    }

    if (i < n && active_i) {
        acc_x[i] = ax_i;
        acc_y[i] = ay_i;
        acc_z[i] = az_i;
    }
}

// --------------------------------------------------------------------------
// Host-side launch functions
// --------------------------------------------------------------------------

void launch_gravity_kernel(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_mass, uint8_t* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    int32_t n, double softening, cudaStream_t stream)
{
    if (n <= 0) return;

    double eps2 = softening * softening;
    int block = GravityKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    gravity_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active,
        d_acc_x, d_acc_y, d_acc_z, n, eps2);

    CUDA_CHECK(cudaGetLastError());
}

void launch_gravity_kernel_per_type(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_mass, uint8_t* d_is_active, int32_t* d_body_type,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    int32_t n, const double* h_type_softening, cudaStream_t stream)
{
    if (n <= 0) return;

    // Upload per-type softening table to constant memory
    CUDA_CHECK(cudaMemcpyToSymbolAsync(d_type_softening, h_type_softening,
        sizeof(double) * MAX_BODY_TYPES, 0, cudaMemcpyHostToDevice, stream));

    int block = GravityKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    gravity_kernel_per_type<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active, d_body_type,
        d_acc_x, d_acc_y, d_acc_z, n);

    CUDA_CHECK(cudaGetLastError());
}

void launch_gravity_kernel_adaptive(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_mass, uint8_t* d_is_active, double* d_per_particle_eps,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    int32_t n, cudaStream_t stream)
{
    if (n <= 0) return;

    int block = GravityKernelConfig::BLOCK_SIZE;
    int grid = (n + block - 1) / block;

    gravity_kernel_adaptive<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active, d_per_particle_eps,
        d_acc_x, d_acc_y, d_acc_z, n);

    CUDA_CHECK(cudaGetLastError());
}

} // namespace celestial::cuda
