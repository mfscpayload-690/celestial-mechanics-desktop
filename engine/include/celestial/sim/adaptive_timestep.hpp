#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/sim/simulation_config.hpp>

namespace celestial::sim {

/// Deterministic adaptive timestep controller.
/// Computes new dt from max acceleration after each step:
///   dt = eta * sqrt(softening / a_max)
/// Clamped to [dt_min, dt_max]. Same a_max -> same dt (deterministic).
class CELESTIAL_API AdaptiveTimestep {
public:
    /// Configure adaptive timestep parameters.
    void configure(const AdaptiveTimestepConfig& cfg);

    /// Compute new dt from maximum acceleration magnitude.
    /// Pure function: same a_max -> same dt_new.
    double compute_dt(double a_max, double softening) const;

    /// Update current dt from a_max. Returns new dt.
    double update(double a_max, double softening);

    /// Get current dt (last computed or initial).
    double current_dt() const { return current_dt_; }

    /// Reset to initial dt.
    void reset();

    bool is_enabled() const { return config_.enabled; }
    const AdaptiveTimestepConfig& config() const { return config_; }

private:
    AdaptiveTimestepConfig config_{};
    double current_dt_ = 0.001;
};

} // namespace celestial::sim
