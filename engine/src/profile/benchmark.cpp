#include <celestial/profile/benchmark.hpp>
#include <algorithm>

namespace celestial::profile {

void BenchmarkLogger::record(const BenchmarkMetrics& metrics) {
    last_ = metrics;
    samples_[write_index_] = metrics;
    write_index_ = (write_index_ + 1) % MAX_SAMPLES;
    if (sample_count_ < MAX_SAMPLES) sample_count_++;
}

BenchmarkMetrics BenchmarkLogger::average() const {
    BenchmarkMetrics avg{};
    if (sample_count_ == 0) return avg;

    for (int i = 0; i < sample_count_; i++) {
        int idx = (write_index_ - sample_count_ + i + MAX_SAMPLES) % MAX_SAMPLES;
        avg.kernel_time_ms    += samples_[idx].kernel_time_ms;
        avg.tree_build_ms     += samples_[idx].tree_build_ms;
        avg.tree_traverse_ms  += samples_[idx].tree_traverse_ms;
        avg.sort_ms           += samples_[idx].sort_ms;
        avg.integration_ms    += samples_[idx].integration_ms;
        avg.transfer_ms       += samples_[idx].transfer_ms;
        avg.total_frame_ms    += samples_[idx].total_frame_ms;
        avg.warp_occupancy    += samples_[idx].warp_occupancy;
        avg.memory_throughput_gbps += samples_[idx].memory_throughput_gbps;
        avg.energy_drift      += samples_[idx].energy_drift;
        avg.momentum_drift    += samples_[idx].momentum_drift;
    }

    double inv = 1.0 / sample_count_;
    avg.kernel_time_ms *= inv;
    avg.tree_build_ms *= inv;
    avg.tree_traverse_ms *= inv;
    avg.sort_ms *= inv;
    avg.integration_ms *= inv;
    avg.transfer_ms *= inv;
    avg.total_frame_ms *= inv;
    avg.warp_occupancy *= inv;
    avg.memory_throughput_gbps *= inv;
    avg.energy_drift *= inv;
    avg.momentum_drift *= inv;

    // Use last sample for non-averaged fields
    avg.body_count = last_.body_count;
    avg.active_body_count = last_.active_body_count;
    avg.step_number = last_.step_number;
    avg.accumulated_error = last_.accumulated_error;
    avg.shared_memory_used = last_.shared_memory_used;

    return avg;
}

void BenchmarkLogger::reset() {
    sample_count_ = 0;
    write_index_ = 0;
    last_ = {};
}

double BenchmarkLogger::estimated_fps() const {
    auto avg = average();
    return (avg.total_frame_ms > 0.0) ? 1000.0 / avg.total_frame_ms : 0.0;
}

double BenchmarkLogger::target_fps() const {
    i32 count = last_.body_count;
    if (count <= 10000) return 144.0;
    if (count <= 100000) return 60.0;
    return 30.0;
}

bool BenchmarkLogger::meets_performance_target() const {
    return estimated_fps() >= target_fps();
}

} // namespace celestial::profile
