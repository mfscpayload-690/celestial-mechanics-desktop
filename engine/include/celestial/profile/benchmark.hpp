#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/profile/frame_profiler.hpp>
#include <string>
#include <vector>

namespace celestial::profile {

/// Detailed benchmark metrics for a single frame.
struct BenchmarkMetrics {
    // Timing
    double kernel_time_ms = 0.0;
    double tree_build_ms = 0.0;
    double tree_traverse_ms = 0.0;
    double sort_ms = 0.0;
    double integration_ms = 0.0;
    double transfer_ms = 0.0;
    double total_frame_ms = 0.0;

    // GPU metrics
    double warp_occupancy = 0.0;       // Estimated occupancy [0,1]
    i64 shared_memory_used = 0;         // Bytes of shared mem per block
    double memory_throughput_gbps = 0.0; // Estimated memory throughput

    // Conservation metrics
    double energy_drift = 0.0;
    double momentum_drift = 0.0;
    double accumulated_error = 0.0;

    // Body count
    i32 body_count = 0;
    i32 active_body_count = 0;

    // Step info
    i64 step_number = 0;
};

/// Benchmark logger that records per-frame metrics for analysis.
class CELESTIAL_API BenchmarkLogger {
public:
    /// Record a benchmark sample.
    void record(const BenchmarkMetrics& metrics);

    /// Get the most recent metrics.
    const BenchmarkMetrics& last() const { return last_; }

    /// Get average metrics over the recorded window.
    BenchmarkMetrics average() const;

    /// Reset all recorded data.
    void reset();

    /// Get estimated FPS from average frame time.
    double estimated_fps() const;

    /// Check if performance targets are met for given body count.
    /// Returns true if FPS meets target:
    ///   10k -> 144 FPS, 100k -> 60 FPS, 1M -> 30 FPS
    bool meets_performance_target() const;

    /// Get the target FPS for current body count.
    double target_fps() const;

    i32 sample_count() const { return sample_count_; }

private:
    static constexpr int MAX_SAMPLES = 300;
    BenchmarkMetrics samples_[MAX_SAMPLES]{};
    BenchmarkMetrics last_{};
    i32 sample_count_ = 0;
    i32 write_index_ = 0;
};

} // namespace celestial::profile
