#include <celestial/sim/deterministic.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::sim {

void DeterministicMode::set_enabled(bool enabled) {
    enabled_ = enabled;

#if CELESTIAL_HAS_CUDA
    if (enabled) {
        // In deterministic mode, disable CUDA kernel auto-tuning
        // which can cause non-deterministic behavior
        cudaDeviceSetCacheConfig(cudaFuncCachePreferL1);
    }
#endif
}

u64 DeterministicMode::deterministic_hash(u64 channel) const {
    // SplitMix64: high-quality deterministic hash
    u64 z = seed_ ^ step_number_ ^ (channel * 0x9E3779B97F4A7C15ULL);
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    z = z ^ (z >> 31);
    return z;
}

u32 DeterministicMode::deterministic_u32(u64 channel) const {
    return static_cast<u32>(deterministic_hash(channel));
}

double DeterministicMode::deterministic_double(u64 channel) const {
    u64 h = deterministic_hash(channel);
    // Convert to [0, 1) using top 53 bits
    return static_cast<double>(h >> 11) * (1.0 / 9007199254740992.0);
}

} // namespace celestial::sim
