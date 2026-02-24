#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/physics/density_model.hpp>

namespace celestial::physics {

/// Maximum number of distinct body types for per-type softening.
static constexpr i32 MAX_BODY_TYPES = 16;

/// Softening modes.
enum class SofteningMode : i32 {
    Global      = 0,   ///< Single global eps (existing behavior, default)
    PerBodyType = 1,   ///< Per-type softening table
    Adaptive    = 2    ///< eps_i = scale * m_i^(1/3)
};

/// Per-body-type softening configuration.
struct SofteningConfig {
    SofteningMode mode = SofteningMode::Global;

    /// Global softening (used when mode == Global, also as fallback).
    double global_softening = 1e-4;

    /// Per-body-type softening values. Index matches body_type_index.
    double type_softening[MAX_BODY_TYPES] = {};

    /// Adaptive softening scale factor: eps_i = adaptive_scale * m_i^(1/3)
    double adaptive_scale = 0.01;

    /// Adaptive softening floor (minimum eps).
    double adaptive_min = 1e-8;

    /// Initialize all type softening to global value.
    void init_defaults() {
        for (int i = 0; i < MAX_BODY_TYPES; i++) {
            type_softening[i] = global_softening;
        }
    }
};

/// Collision resolution modes.
enum class CollisionMode : i32 {
    Ignore     = 0,   ///< Detection but no resolution (default)
    Elastic    = 1,   ///< Perfectly elastic bounce
    Inelastic  = 2,   ///< Inelastic (coefficient of restitution)
    Merge      = 3    ///< Bodies merge: conserve mass + momentum
};

/// Configuration for collision resolution.
struct CollisionResolverConfig {
    CollisionMode mode = CollisionMode::Ignore;
    double restitution = 0.5;  ///< Coefficient of restitution (0=perfectly inelastic, 1=elastic)

    /// Phase 14-15: Merge safeguards
    i32 max_merges_per_frame = 64;        ///< Cap total merges per step
    i32 max_merges_per_body = 2;          ///< Cap merges per body per step
    bool density_preserving_merge = true; ///< Use density model for survivor radius
};

} // namespace celestial::physics

namespace celestial::sim {

/// Integrator selection.
enum class IntegratorType : i32 {
    Leapfrog_KDK = 0,   ///< Standard 2nd-order leapfrog (default)
    Yoshida4     = 1     ///< 4th-order Yoshida (Forest-Ruth)
};

/// Adaptive timestep configuration.
struct AdaptiveTimestepConfig {
    bool enabled = false;       ///< Enable adaptive dt
    double eta = 0.01;          ///< Safety factor
    double dt_min = 1e-8;       ///< Floor
    double dt_max = 0.01;       ///< Ceiling
    double initial_dt = 0.001;  ///< Starting dt before first acceleration known
};

/// Simulation configuration. Mirrors the key settings from C# PhysicsConfig.
struct SimulationConfig {
    double dt = 0.001;              ///< Fixed timestep
    double softening = 1e-4;        ///< Gravitational softening epsilon (global mode)
    double theta = 0.5;             ///< Barnes-Hut opening angle
    bool enable_pn = false;         ///< Enable post-Newtonian corrections
    bool enable_collisions = true;  ///< Enable collision detection
    bool deterministic = false;     ///< Enable deterministic mode (fixed dispatch, seeded RNG)
    u64 deterministic_seed = 42;    ///< Seed for deterministic RNG
    i32 max_particles = 1048576;    ///< Max particle count (1M default)
    int max_steps_per_frame = 10;   ///< Safety cap on substeps per frame
    usize gpu_memory_pool_size = 256 * 1024 * 1024; ///< GPU scratch pool size in bytes (256 MB default)

    /// PN correction parameters
    double max_velocity_fraction_c = 0.3;  ///< Max v/c before clamping
    double schwarz_warning_factor = 3.0;   ///< Schwarzschild radius warning multiplier

    /// Integrator selection (Phase 13)
    IntegratorType integrator = IntegratorType::Leapfrog_KDK;

    /// Adaptive timestep (Phase 13)
    AdaptiveTimestepConfig adaptive_dt;

    /// Softening configuration (Phase 13)
    physics::SofteningConfig softening_config;

    /// Collision resolution configuration (Phase 13)
    physics::CollisionResolverConfig collision_config;

    /// Density model configuration (Phase 14-15)
    physics::DensityConfig density_config;

    /// Phase 16-17: Auto-compute energy diagnostics each step
    bool enable_diagnostics = false;

    /// Phase 18+19: Enable CPU vs GPU validation mode.
    /// When enabled, computes both CPU and GPU energy/momentum each step
    /// and asserts relative error is below tolerance.
    bool enable_gpu_validation = false;

    /// Phase 18+19: Tolerance for GPU vs CPU validation.
    double gpu_validation_tolerance = 1e-6;

    /// Compute mode selection.
    enum class ComputeMode : i32 {
        CPU_BruteForce = 0,
        CPU_BarnesHut  = 1,
        GPU_BruteForce = 2,
        GPU_BarnesHut  = 3
    };
    ComputeMode compute_mode = ComputeMode::GPU_BruteForce;
};

} // namespace celestial::sim
