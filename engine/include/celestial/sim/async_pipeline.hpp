#pragma once

#include <celestial/cuda/device_particles.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/core/platform.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::sim {

/// Double-buffered CPU-GPU async pipeline.
/// Frame N: GPU computes on buffer A while CPU reads results from buffer B.
/// Frame N+1: Swap — GPU uses buffer B, CPU reads buffer A.
class CELESTIAL_API AsyncPipeline {
public:
    void init(i32 capacity);
    void destroy();

    /// Submit a full physics step: upload -> kick_drift -> gravity -> pn -> kick_rotate -> download.
    void submit_step(const celestial::physics::ParticleSystem& host,
                     double softening, double dt, bool enable_pn);

    /// Wait for previous frame's results and copy to host.
    void retrieve_results(celestial::physics::ParticleSystem& host);

    /// Swap front/back buffers.
    void swap() { current_ = 1 - current_; }

    bool is_initialized() const { return initialized_; }

private:
    celestial::cuda::DeviceParticles device_buf_[2];

#if CELESTIAL_HAS_CUDA
    cudaStream_t streams_[2]{};
    cudaEvent_t compute_done_[2]{};
#endif

    int current_ = 0;
    bool initialized_ = false;
    bool first_frame_ = true;
};

} // namespace celestial::sim
