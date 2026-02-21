#include <celestial/profile/frame_profiler.hpp>
#include <algorithm>

namespace celestial::profile {

void FrameProfiler::begin_section([[maybe_unused]] const char* name) {
    section_start_ = Clock::now();
}

double FrameProfiler::end_section([[maybe_unused]] const char* name) {
    auto end = Clock::now();
    double ms = std::chrono::duration<double, std::milli>(end - section_start_).count();
    return ms;
}

void FrameProfiler::end_frame(const FrameProfile& profile) {
    int idx = frame_count_ % WINDOW_SIZE;
    profiles_[idx] = profile;
    frame_count_++;
}

FrameProfile FrameProfiler::average_profile() const {
    FrameProfile avg{};
    int count = std::min(frame_count_, WINDOW_SIZE);
    if (count == 0) return avg;

    for (int i = 0; i < count; i++) {
        int idx = (frame_count_ - count + i) % WINDOW_SIZE;
        avg.tree_build_ms  += profiles_[idx].tree_build_ms;
        avg.gravity_ms     += profiles_[idx].gravity_ms;
        avg.integration_ms += profiles_[idx].integration_ms;
        avg.transfer_ms    += profiles_[idx].transfer_ms;
        avg.collision_ms   += profiles_[idx].collision_ms;
        avg.total_cpu_ms   += profiles_[idx].total_cpu_ms;
        avg.total_gpu_ms   += profiles_[idx].total_gpu_ms;
        avg.total_frame_ms += profiles_[idx].total_frame_ms;
    }

    double inv = 1.0 / count;
    avg.tree_build_ms  *= inv;
    avg.gravity_ms     *= inv;
    avg.integration_ms *= inv;
    avg.transfer_ms    *= inv;
    avg.collision_ms   *= inv;
    avg.total_cpu_ms   *= inv;
    avg.total_gpu_ms   *= inv;
    avg.total_frame_ms *= inv;

    return avg;
}

double FrameProfiler::fps() const {
    auto avg = average_profile();
    return (avg.total_frame_ms > 0.0) ? 1000.0 / avg.total_frame_ms : 0.0;
}

} // namespace celestial::profile
