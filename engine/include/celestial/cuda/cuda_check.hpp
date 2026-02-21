#pragma once

#include <celestial/core/platform.hpp>
#include <celestial/core/error.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

/// Check a CUDA API call and throw CelestialException on failure.
#if CELESTIAL_HAS_CUDA
#define CUDA_CHECK(call) do {                                                   \
    cudaError_t err_ = (call);                                                  \
    if (err_ != cudaSuccess) {                                                  \
        throw celestial::core::CelestialException(                              \
            celestial::core::ErrorCode::CudaError,                              \
            cudaGetErrorString(err_), __FILE__, __LINE__);                       \
    }                                                                           \
} while (0)
#else
#define CUDA_CHECK(call) ((void)0)
#endif
