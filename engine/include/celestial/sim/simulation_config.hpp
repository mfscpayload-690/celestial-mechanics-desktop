#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::sim {

/// Simulation configuration. Mirrors the key settings from C# PhysicsConfig.
struct SimulationConfig {
    double dt = 0.001;              ///< Fixed timestep
    double softening = 1e-4;        ///< Gravitational softening epsilon
    double theta = 0.5;             ///< Barnes-Hut opening angle
    bool enable_pn = false;         ///< Enable post-Newtonian corrections
    bool enable_collisions = true;  ///< Enable collision detection
    i32 max_particles = 1048576;    ///< Max particle count (1M default)
    int max_steps_per_frame = 10;   ///< Safety cap on substeps per frame

    /// Compute mode selection.
    enum class ComputeMode : i32 {
        CPU_BruteForce = 0,
        CPU_BarnesHut  = 1,
        GPU_BruteForce = 2,
        GPU_BarnesHut  = 3  // Future: GPU-accelerated tree traversal
    };
    ComputeMode compute_mode = ComputeMode::GPU_BruteForce;
};

} // namespace celestial::sim
