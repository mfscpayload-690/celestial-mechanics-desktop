#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// SoA device (GPU) memory layout. Separate device pointers for each array.
/// Mirrors ParticleSystem but lives in GPU global memory.
struct DeviceParticles {
    double* d_pos_x = nullptr;
    double* d_pos_y = nullptr;
    double* d_pos_z = nullptr;

    double* d_vel_x = nullptr;
    double* d_vel_y = nullptr;
    double* d_vel_z = nullptr;

    double* d_acc_x = nullptr;
    double* d_acc_y = nullptr;
    double* d_acc_z = nullptr;

    double* d_old_acc_x = nullptr;
    double* d_old_acc_y = nullptr;
    double* d_old_acc_z = nullptr;

    double* d_mass       = nullptr;
    double* d_radius     = nullptr;
    u8*     d_is_active  = nullptr;
    i32*    d_body_type_index = nullptr;

    i32 count    = 0;
    i32 capacity = 0;

    /// Allocate device memory for given capacity.
    void allocate(i32 cap);

    /// Free all device memory.
    void free();

#if CELESTIAL_HAS_CUDA
    /// Upload positions, mass, and active flags from host (async).
    void upload_all(const double* hpx, const double* hpy, const double* hpz,
                    const double* hvx, const double* hvy, const double* hvz,
                    const double* hax, const double* hay, const double* haz,
                    const double* hoax, const double* hoay, const double* hoaz,
                    const double* hm, const double* hr,
                    const u8* h_active, i32 n, cudaStream_t stream);

    /// Upload positions and mass only (for force-only computation).
    void upload_positions_mass(const double* hpx, const double* hpy, const double* hpz,
                               const double* hm, const u8* h_active,
                               i32 n, cudaStream_t stream);

    /// Download accelerations from device to host (async).
    void download_accelerations(double* hax, double* hay, double* haz,
                                i32 n, cudaStream_t stream);

    /// Download all state (positions, velocities, accelerations) from device (async).
    void download_state(double* hpx, double* hpy, double* hpz,
                        double* hvx, double* hvy, double* hvz,
                        double* hax, double* hay, double* haz,
                        i32 n, cudaStream_t stream);
#endif
};

} // namespace celestial::cuda
