#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// Manages CUDA device initialization, streams, and events.
/// Singleton pattern — one device context per process.
struct DeviceContext {
    /// Get the singleton instance.
    static DeviceContext& instance();

    /// Initialize CUDA: select best device, create streams and events.
    /// Call once at startup. Throws on failure.
    void init();

    /// Release all CUDA resources.
    void shutdown();

    bool is_initialized() const { return initialized_; }
    bool has_cuda() const { return has_cuda_; }

#if CELESTIAL_HAS_CUDA
    /// Two streams for double-buffered pipeline.
    cudaStream_t stream(int index) const { return streams_[index & 1]; }

    /// Synchronization events: 2 compute-done + 2 transfer-done.
    cudaEvent_t compute_done_event(int index) const { return compute_done_[index & 1]; }
    cudaEvent_t transfer_done_event(int index) const { return transfer_done_[index & 1]; }
#endif

    // Device properties
    int device_id() const { return device_id_; }
    int sm_count() const { return sm_count_; }
    int max_threads_per_block() const { return max_threads_per_block_; }
    i64 total_global_mem() const { return total_global_mem_; }
    i64 shared_mem_per_block() const { return shared_mem_per_block_; }

private:
    DeviceContext() = default;
    ~DeviceContext();

    DeviceContext(const DeviceContext&) = delete;
    DeviceContext& operator=(const DeviceContext&) = delete;

    bool initialized_ = false;
    bool has_cuda_ = false;
    int device_id_ = -1;
    int sm_count_ = 0;
    int max_threads_per_block_ = 0;
    i64 total_global_mem_ = 0;
    i64 shared_mem_per_block_ = 0;

#if CELESTIAL_HAS_CUDA
    cudaStream_t streams_[2]{};
    cudaEvent_t compute_done_[2]{};
    cudaEvent_t transfer_done_[2]{};
#endif
};

} // namespace celestial::cuda
