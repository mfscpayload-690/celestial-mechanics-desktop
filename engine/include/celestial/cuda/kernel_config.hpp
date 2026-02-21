#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// Gravity kernel launch configuration.
struct GravityKernelConfig {
    static constexpr int BLOCK_SIZE = 256;
    static constexpr int TILE_SIZE = 256;
};

/// Integration kernel launch configuration.
struct IntegrationKernelConfig {
    static constexpr int BLOCK_SIZE = 256;
};

/// Ejecta kernel launch configuration.
struct EjectaKernelConfig {
    static constexpr int BLOCK_SIZE = 256;
};

/// PN correction kernel launch configuration.
struct PNKernelConfig {
    static constexpr int BLOCK_SIZE = 256;
};

/// Reduction kernel launch configuration.
struct ReductionKernelConfig {
    static constexpr int BLOCK_SIZE = 256;
};

/// Compute grid dimension for given N and block size.
inline int grid_dim(int n, int block_size) {
    return (n + block_size - 1) / block_size;
}

} // namespace celestial::cuda
