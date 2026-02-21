#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <chrono>
#include <array>

namespace celestial::profile {

/// Per-frame timing data.
struct FrameProfile {
    double tree_build_ms   = 0.0;
    double gravity_ms      = 0.0;
    double integration_ms  = 0.0;
    double transfer_ms     = 0.0;
    double collision_ms    = 0.0;
    double total_cpu_ms    = 0.0;
    double total_gpu_ms    = 0.0;
    double total_frame_ms  = 0.0;
};

/// Frame profiler with rolling window for averaging.
class CELESTIAL_API FrameProfiler {
public:
    static constexpr int WINDOW_SIZE = 120;

    /// Start a named timer section.
    void begin_section(const char* name);

    /// End a named timer section and record elapsed time.
    double end_section(const char* name);

    /// Record the current frame profile and advance to next frame.
    void end_frame(const FrameProfile& profile);

    /// Get the most recent frame profile.
    const FrameProfile& last_profile() const { return profiles_[last_index()]; }

    /// Get average profile over the rolling window.
    FrameProfile average_profile() const;

    /// Get current FPS estimate.
    double fps() const;

    int frame_count() const { return frame_count_; }

private:
    int last_index() const {
        return (frame_count_ > 0) ? ((frame_count_ - 1) % WINDOW_SIZE) : 0;
    }

    std::array<FrameProfile, WINDOW_SIZE> profiles_{};
    int frame_count_ = 0;

    using Clock = std::chrono::high_resolution_clock;
    Clock::time_point section_start_;
};

} // namespace celestial::profile
