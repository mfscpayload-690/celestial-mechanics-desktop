#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::sim {

/// Fixed-timestep accumulator. Port of C# SimulationEngine accumulator pattern.
class CELESTIAL_API Timestep {
public:
    double fixed_dt = 0.001;
    double current_time = 0.0;
    double accumulator = 0.0;
    double interpolation_alpha = 0.0;
    int max_steps_per_frame = 10;

    /// Feed frame time, returns number of physics steps to execute.
    int update(double frame_time);

    /// Reset time state.
    void reset();
};

} // namespace celestial::sim
