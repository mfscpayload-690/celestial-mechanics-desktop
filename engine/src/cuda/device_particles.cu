#include <celestial/cuda/device_particles.hpp>
#include <celestial/cuda/cuda_check.hpp>
#include <celestial/core/error.hpp>

namespace celestial::cuda {

void DeviceParticles::allocate(i32 cap) {
    if (cap <= 0) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "DeviceParticles capacity must be positive");
    }
    if (capacity > 0) {
        free();
    }

    capacity = cap;
    count = 0;

    usize dsize = sizeof(double) * static_cast<usize>(cap);
    usize u8size = sizeof(u8) * static_cast<usize>(cap);
    usize i32size = sizeof(i32) * static_cast<usize>(cap);

    CUDA_CHECK(cudaMalloc(&d_pos_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_pos_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_pos_z, dsize));

    CUDA_CHECK(cudaMalloc(&d_vel_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_vel_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_vel_z, dsize));

    CUDA_CHECK(cudaMalloc(&d_acc_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_acc_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_acc_z, dsize));

    CUDA_CHECK(cudaMalloc(&d_old_acc_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_old_acc_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_old_acc_z, dsize));

    CUDA_CHECK(cudaMalloc(&d_mass, dsize));
    CUDA_CHECK(cudaMalloc(&d_radius, dsize));
    CUDA_CHECK(cudaMalloc(&d_is_active, u8size));
    CUDA_CHECK(cudaMalloc(&d_body_type_index, i32size));

    // Zero all arrays
    CUDA_CHECK(cudaMemset(d_pos_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_pos_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_pos_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_mass, 0, dsize));
    CUDA_CHECK(cudaMemset(d_radius, 0, dsize));
    CUDA_CHECK(cudaMemset(d_is_active, 0, u8size));
    CUDA_CHECK(cudaMemset(d_body_type_index, 0, i32size));
}

void DeviceParticles::free() {
    if (d_pos_x) { cudaFree(d_pos_x); d_pos_x = nullptr; }
    if (d_pos_y) { cudaFree(d_pos_y); d_pos_y = nullptr; }
    if (d_pos_z) { cudaFree(d_pos_z); d_pos_z = nullptr; }
    if (d_vel_x) { cudaFree(d_vel_x); d_vel_x = nullptr; }
    if (d_vel_y) { cudaFree(d_vel_y); d_vel_y = nullptr; }
    if (d_vel_z) { cudaFree(d_vel_z); d_vel_z = nullptr; }
    if (d_acc_x) { cudaFree(d_acc_x); d_acc_x = nullptr; }
    if (d_acc_y) { cudaFree(d_acc_y); d_acc_y = nullptr; }
    if (d_acc_z) { cudaFree(d_acc_z); d_acc_z = nullptr; }
    if (d_old_acc_x) { cudaFree(d_old_acc_x); d_old_acc_x = nullptr; }
    if (d_old_acc_y) { cudaFree(d_old_acc_y); d_old_acc_y = nullptr; }
    if (d_old_acc_z) { cudaFree(d_old_acc_z); d_old_acc_z = nullptr; }
    if (d_mass) { cudaFree(d_mass); d_mass = nullptr; }
    if (d_radius) { cudaFree(d_radius); d_radius = nullptr; }
    if (d_is_active) { cudaFree(d_is_active); d_is_active = nullptr; }
    if (d_body_type_index) { cudaFree(d_body_type_index); d_body_type_index = nullptr; }

    count = 0;
    capacity = 0;
}

void DeviceParticles::upload_all(
    const double* hpx, const double* hpy, const double* hpz,
    const double* hvx, const double* hvy, const double* hvz,
    const double* hax, const double* hay, const double* haz,
    const double* hoax, const double* hoay, const double* hoaz,
    const double* hm, const double* hr,
    const u8* h_active, i32 n, cudaStream_t stream)
{
    count = n;
    usize dsize = sizeof(double) * static_cast<usize>(n);
    usize u8size = sizeof(u8) * static_cast<usize>(n);

    CUDA_CHECK(cudaMemcpyAsync(d_pos_x, hpx, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_y, hpy, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_z, hpz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_x, hvx, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_y, hvy, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_z, hvz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_x, hax, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_y, hay, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_z, haz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_x, hoax, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_y, hoay, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_z, hoaz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_mass, hm, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_radius, hr, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_is_active, h_active, u8size, cudaMemcpyHostToDevice, stream));
}

void DeviceParticles::upload_positions_mass(
    const double* hpx, const double* hpy, const double* hpz,
    const double* hm, const u8* h_active,
    i32 n, cudaStream_t stream)
{
    count = n;
    usize dsize = sizeof(double) * static_cast<usize>(n);
    usize u8size = sizeof(u8) * static_cast<usize>(n);

    CUDA_CHECK(cudaMemcpyAsync(d_pos_x, hpx, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_y, hpy, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_z, hpz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_mass, hm, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_is_active, h_active, u8size, cudaMemcpyHostToDevice, stream));
}

void DeviceParticles::download_accelerations(
    double* hax, double* hay, double* haz,
    i32 n, cudaStream_t stream)
{
    usize dsize = sizeof(double) * static_cast<usize>(n);
    CUDA_CHECK(cudaMemcpyAsync(hax, d_acc_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hay, d_acc_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(haz, d_acc_z, dsize, cudaMemcpyDeviceToHost, stream));
}

void DeviceParticles::download_state(
    double* hpx, double* hpy, double* hpz,
    double* hvx, double* hvy, double* hvz,
    double* hax, double* hay, double* haz,
    i32 n, cudaStream_t stream)
{
    usize dsize = sizeof(double) * static_cast<usize>(n);
    CUDA_CHECK(cudaMemcpyAsync(hpx, d_pos_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hpy, d_pos_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hpz, d_pos_z, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvx, d_vel_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvy, d_vel_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvz, d_vel_z, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hax, d_acc_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hay, d_acc_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(haz, d_acc_z, dsize, cudaMemcpyDeviceToHost, stream));
}

} // namespace celestial::cuda
