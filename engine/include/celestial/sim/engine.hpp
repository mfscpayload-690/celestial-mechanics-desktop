#pragma once

#include <celestial/physics/particle_system.hpp>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/physics/post_newtonian.hpp>
#include <celestial/physics/collision_detector.hpp>
#include <celestial/sim/simulation_config.hpp>
#include <celestial/sim/timestep.hpp>
#include <celestial/sim/async_pipeline.hpp>
#include <celestial/profile/frame_profiler.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::sim {

/// Top-level simulation engine facade.
/// Manages all subsystems and provides a simple API for the interop layer.
class CELESTIAL_API Engine {
public:
    /// Initialize the engine with given configuration.
    void init(const SimulationConfig& config);

    /// Shutdown and release all resources.
    void shutdown();

    bool is_initialized() const { return initialized_; }

    /// Set particle data from host arrays.
    void set_particles(
        const double* px, const double* py, const double* pz,
        const double* vx, const double* vy, const double* vz,
        const double* ax, const double* ay, const double* az,
        const double* mass, const double* radius,
        const u8* is_active, i32 count);

    /// Execute one fixed-timestep physics step.
    void step(double dt, double softening);

    /// Accumulator-based update: feed frame_time, execute 0..N sub-steps.
    int update(double frame_time);

    /// Copy current state to output arrays.
    void get_positions(double* out_px, double* out_py, double* out_pz, i32 count) const;
    void get_velocities(double* out_vx, double* out_vy, double* out_vz, i32 count) const;
    void get_accelerations(double* out_ax, double* out_ay, double* out_az, i32 count) const;

    /// Configuration
    void set_compute_mode(SimulationConfig::ComputeMode mode);
    void set_theta(double theta);
    void set_enable_pn(bool enable);

    /// Accessors
    i32 particle_count() const { return particles_.count; }
    const SimulationConfig& config() const { return config_; }
    const profile::FrameProfile& last_profile() const { return profiler_.last_profile(); }
    double last_gpu_time_ms() const { return profiler_.last_profile().total_gpu_ms; }
    double last_cpu_time_ms() const { return profiler_.last_profile().total_cpu_ms; }

private:
    /// Execute one physics step using CPU brute-force O(n^2).
    void step_cpu_brute_force(double dt, double softening);

    /// Execute one physics step using CPU Barnes-Hut O(n log n).
    void step_cpu_barnes_hut(double dt, double softening);

    /// Execute one physics step using GPU brute-force (tiled kernel).
    void step_gpu_brute_force(double dt, double softening);

    SimulationConfig config_;
    physics::ParticleSystem particles_;
    physics::BarnesHutSolver bh_solver_;
    physics::PostNewtonianCorrection pn_correction_;
    physics::CollisionDetector collision_detector_;
    Timestep timestep_;
    AsyncPipeline gpu_pipeline_;
    profile::FrameProfiler profiler_;
    bool initialized_ = false;
};

} // namespace celestial::sim
