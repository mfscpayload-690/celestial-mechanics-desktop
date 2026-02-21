#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::profile {

/// GPU performance metrics readout.
struct GpuMetrics {
    int sm_count = 0;
    int max_threads_per_sm = 0;
    i64 total_global_mem = 0;
    i64 shared_mem_per_block = 0;
    double gravity_kernel_occupancy = 0.0;  ///< Theoretical occupancy (0-1)
    double gravity_kernel_time_ms = 0.0;
    double integration_kernel_time_ms = 0.0;
    double transfer_time_ms = 0.0;
};

} // namespace celestial::profile
