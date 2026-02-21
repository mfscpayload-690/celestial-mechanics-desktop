#include <celestial/cuda/device_context.hpp>
#include <celestial/cuda/cuda_check.hpp>

namespace celestial::cuda {

DeviceContext& DeviceContext::instance() {
    static DeviceContext ctx;
    return ctx;
}

DeviceContext::~DeviceContext() {
    if (initialized_) {
        shutdown();
    }
}

void DeviceContext::init() {
    if (initialized_) {
        throw core::CelestialException(
            core::ErrorCode::AlreadyInitialized,
            "DeviceContext already initialized");
    }

    int device_count = 0;
    cudaError_t err = cudaGetDeviceCount(&device_count);

    if (err != cudaSuccess || device_count == 0) {
        has_cuda_ = false;
        initialized_ = true;
        return;
    }

    // Select device with best double-precision throughput
    int best_device = 0;
    double best_dp_perf = 0.0;

    for (int i = 0; i < device_count; i++) {
        cudaDeviceProp prop{};
        CUDA_CHECK(cudaGetDeviceProperties(&prop, i));

        // fp64 throughput estimate: SMs * clock * fp64_ratio
        // Consumer GPUs: 1/64 fp64, HPC GPUs: 1/2 fp64
        double dp_perf = static_cast<double>(prop.multiProcessorCount) *
                         static_cast<double>(prop.clockRate);

        if (dp_perf > best_dp_perf) {
            best_dp_perf = dp_perf;
            best_device = i;
        }
    }

    device_id_ = best_device;
    CUDA_CHECK(cudaSetDevice(device_id_));

    // Query properties
    cudaDeviceProp prop{};
    CUDA_CHECK(cudaGetDeviceProperties(&prop, device_id_));
    sm_count_ = prop.multiProcessorCount;
    max_threads_per_block_ = prop.maxThreadsPerBlock;
    total_global_mem_ = static_cast<i64>(prop.totalGlobalMem);
    shared_mem_per_block_ = static_cast<i64>(prop.sharedMemPerBlock);

    // Create 2 streams for double-buffered pipeline
    for (int i = 0; i < 2; i++) {
        CUDA_CHECK(cudaStreamCreateWithFlags(&streams_[i], cudaStreamNonBlocking));
    }

    // Create synchronization events
    for (int i = 0; i < 2; i++) {
        CUDA_CHECK(cudaEventCreateWithFlags(&compute_done_[i], cudaEventDisableTiming));
        CUDA_CHECK(cudaEventCreateWithFlags(&transfer_done_[i], cudaEventDisableTiming));
    }

    has_cuda_ = true;
    initialized_ = true;
}

void DeviceContext::shutdown() {
    if (!initialized_) return;

    if (has_cuda_) {
        for (int i = 0; i < 2; i++) {
            if (streams_[i]) { cudaStreamDestroy(streams_[i]); streams_[i] = nullptr; }
            if (compute_done_[i]) { cudaEventDestroy(compute_done_[i]); compute_done_[i] = nullptr; }
            if (transfer_done_[i]) { cudaEventDestroy(transfer_done_[i]); transfer_done_[i] = nullptr; }
        }
        cudaDeviceReset();
    }

    initialized_ = false;
    has_cuda_ = false;
}

} // namespace celestial::cuda
