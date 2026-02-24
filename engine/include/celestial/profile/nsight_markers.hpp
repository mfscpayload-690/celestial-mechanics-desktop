#pragma once

#include <celestial/core/platform.hpp>

// NVIDIA Nsight Systems integration markers.
// When NVTX is available, these wrap nvtxRangePush/Pop.
// Otherwise they compile to no-ops.

#if defined(CELESTIAL_USE_NVTX) && __has_include(<nvtx3/nvToolsExt.h>)
    #include <nvtx3/nvToolsExt.h>
    #define CELESTIAL_NVTX_PUSH(name) nvtxRangePushA(name)
    #define CELESTIAL_NVTX_POP()      nvtxRangePop()
    #define CELESTIAL_NVTX_MARK(name) nvtxMarkA(name)
#else
    #define CELESTIAL_NVTX_PUSH(name) ((void)0)
    #define CELESTIAL_NVTX_POP()      ((void)0)
    #define CELESTIAL_NVTX_MARK(name) ((void)0)
#endif

namespace celestial::profile {

/// RAII Nsight profiling scope.
/// Usage: { NsightScope scope("GravityKernel"); ... }
struct NsightScope {
    explicit NsightScope(const char* name) {
        CELESTIAL_NVTX_PUSH(name);
    }
    ~NsightScope() {
        CELESTIAL_NVTX_POP();
    }
    NsightScope(const NsightScope&) = delete;
    NsightScope& operator=(const NsightScope&) = delete;
};

} // namespace celestial::profile
