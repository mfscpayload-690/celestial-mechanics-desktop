#include <celestial/sim/adaptive_timestep.hpp>
#include <cmath>
#include <algorithm>

namespace celestial::sim {

void AdaptiveTimestep::configure(const AdaptiveTimestepConfig& cfg) {
    config_ = cfg;
    current_dt_ = cfg.initial_dt;
}

double AdaptiveTimestep::compute_dt(double a_max, double softening) const {
    if (!config_.enabled) return current_dt_;
    if (a_max <= 0.0 || softening <= 0.0) return config_.dt_max;
    double dt_new = config_.eta * std::sqrt(softening / a_max);
    return std::clamp(dt_new, config_.dt_min, config_.dt_max);
}

double AdaptiveTimestep::update(double a_max, double softening) {
    if (!config_.enabled) return current_dt_;
    current_dt_ = compute_dt(a_max, softening);
    return current_dt_;
}

void AdaptiveTimestep::reset() {
    current_dt_ = config_.initial_dt;
}

} // namespace celestial::sim
