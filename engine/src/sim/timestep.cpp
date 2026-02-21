#include <celestial/sim/timestep.hpp>

namespace celestial::sim {

int Timestep::update(double frame_time) {
    accumulator += frame_time;
    int steps = 0;

    while (accumulator >= fixed_dt && steps < max_steps_per_frame) {
        accumulator -= fixed_dt;
        current_time += fixed_dt;
        steps++;
    }

    interpolation_alpha = (fixed_dt > 0.0) ? accumulator / fixed_dt : 0.0;
    return steps;
}

void Timestep::reset() {
    current_time = 0.0;
    accumulator = 0.0;
    interpolation_alpha = 0.0;
}

} // namespace celestial::sim
