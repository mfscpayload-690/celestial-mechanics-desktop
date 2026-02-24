#pragma once

#include <celestial/physics/particle_system.hpp>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/physics/post_newtonian.hpp>
#include <celestial/physics/collision_detector.hpp>
#include <celestial/physics/collision_resolver.hpp>
#include <celestial/physics/density_model.hpp>
#include <celestial/sim/simulation_config.hpp>
#include <celestial/sim/timestep.hpp>
#include <celestial/sim/async_pipeline.hpp>
#include <celestial/sim/deterministic.hpp>
#include <celestial/sim/adaptive_timestep.hpp>
#include <celestial/sim/yoshida.hpp>
#include <celestial/memory/gpu_pool.hpp>
#include <celestial/profile/frame_profiler.hpp>
#include <celestial/profile/energy_tracker.hpp>
#include <celestial/profile/benchmark.hpp>
#include <celestial/profile/nsight_markers.hpp>
#include <celestial/hooks/phase13_hooks.hpp>
#include <celestial/core/platform.hpp>
#include <vector>

#if CELESTIAL_HAS_CUDA
#include <celestial/cuda/gpu_tree.hpp>
#endif

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

    /// Deterministic mode control
    void set_deterministic(bool enabled);
    bool is_deterministic() const { return deterministic_.is_enabled(); }
    void set_deterministic_seed(u64 seed) { deterministic_.set_seed(seed); }

    /// Phase 13 hook registration
    void set_phase13_hooks(hooks::Phase13Hooks* hooks) { phase13_hooks_ = hooks; }
    hooks::Phase13Hooks* phase13_hooks() const { return phase13_hooks_; }

    /// Energy tracking
    void compute_energy_snapshot();
    double energy_drift() const { return energy_tracker_.energy_drift(); }
    double momentum_drift() const { return energy_tracker_.momentum_drift(); }
    double accumulated_error() const { return energy_tracker_.accumulated_error(); }
    double angular_momentum_drift() const { return energy_tracker_.angular_momentum_drift(); }
    double com_position_drift() const { return energy_tracker_.com_position_drift(); }
    double com_velocity_drift() const { return energy_tracker_.com_velocity_drift(); }
    bool check_conservation_diagnostics(const profile::DiagnosticThresholds& t) const {
        return energy_tracker_.check_diagnostics(t);
    }

    /// Integrator selection (Phase 13)
    void set_integrator(IntegratorType type) { config_.integrator = type; }
    IntegratorType integrator() const { return config_.integrator; }

    /// Adaptive timestep (Phase 13)
    void set_adaptive_timestep(const AdaptiveTimestepConfig& cfg);
    double current_adaptive_dt() const { return adaptive_timestep_.current_dt(); }
    bool is_adaptive_dt_enabled() const { return adaptive_timestep_.is_enabled(); }

    /// Collision configuration (Phase 13)
    void set_collision_mode(physics::CollisionMode mode);
    void set_collision_restitution(double e);
    i32 last_collision_count() const { return collision_resolver_.last_collision_count(); }

    /// Phase 14-15: Density model configuration
    void set_density_config(const physics::DensityConfig& cfg);
    const physics::DensityModel& density_model() const { return density_model_; }

    /// Phase 14-15: Merge safeguards
    void set_max_merges_per_frame(i32 max_merges);
    void set_max_merges_per_body(i32 max_merges);
    void set_density_preserving_merge(bool enabled);
    i32 last_merge_count() const { return collision_resolver_.last_merge_count(); }

    /// Phase 14-15: Active particle count (after compaction)
    i32 active_particle_count() const;

    /// Phase 14-15: Manual compaction trigger (returns new count)
    i32 compact_particles();

    /// Softening configuration (Phase 13)
    void set_softening_mode(physics::SofteningMode mode);
    void set_type_softening(i32 type_index, double eps);
    void set_adaptive_softening_scale(double scale);

    /// Phase 16-17: Diagnostics toggle
    void set_enable_diagnostics(bool enabled) { config_.enable_diagnostics = enabled; }
    bool is_diagnostics_enabled() const { return config_.enable_diagnostics; }

    /// Phase 16-17: Rolling energy averages
    double rolling_avg_energy() const { return energy_tracker_.rolling_avg_energy(); }
    double rolling_avg_drift() const { return energy_tracker_.rolling_avg_drift(); }

    /// Phase 18+19: GPU validation mode
    void set_enable_gpu_validation(bool enabled) { config_.enable_gpu_validation = enabled; }
    void set_gpu_validation_tolerance(double tol) { config_.gpu_validation_tolerance = tol; }

    /// Phase 18+19: GPU-resident energy snapshot (no CPU round-trip).
    /// Only works in GPU_BarnesHut mode (requires BH tree for PE).
    void compute_gpu_energy_snapshot();

    /// Phase 18+19: CPU vs GPU parity check results
    struct GpuValidationResult {
        double cpu_ke = 0.0, gpu_ke = 0.0;
        double cpu_pe = 0.0, gpu_pe = 0.0;
        double cpu_total_mass = 0.0, gpu_total_mass = 0.0;
        double ke_relative_error = 0.0;
        double pe_relative_error = 0.0;
        double mass_error = 0.0;
        bool passed = true;
    };

    /// Run CPU vs GPU validation. Only meaningful in GPU modes.
    GpuValidationResult validate_gpu_cpu_parity();

    /// Phase 18+19: Get last GPU validation result.
    const GpuValidationResult& last_gpu_validation() const { return last_gpu_validation_; }

    /// Phase 21: Telemetry callback
    using TelemetryCallback = void(*)(const char* event_name, double value);
    void set_telemetry_callback(TelemetryCallback cb) { telemetry_callback_ = cb; }

    /// Accessors
    i32 particle_count() const { return particles_.count; }
    const SimulationConfig& config() const { return config_; }
    const profile::FrameProfile& last_profile() const { return profiler_.last_profile(); }
    double last_gpu_time_ms() const { return profiler_.last_profile().total_gpu_ms; }
    double last_cpu_time_ms() const { return profiler_.last_profile().total_cpu_ms; }
    const profile::BenchmarkMetrics& last_benchmark() const { return benchmark_.last(); }
    const DeterministicMode& deterministic() const { return deterministic_; }
    const profile::EnergyTracker& energy_tracker() const { return energy_tracker_; }
    const profile::BenchmarkLogger& benchmark_logger() const { return benchmark_; }

    /// GPU pool access (for advanced users)
    memory::GpuPool& gpu_pool() { return gpu_pool_; }
    const memory::GpuPool& gpu_pool() const { return gpu_pool_; }

private:
    // ── Leapfrog step methods ──
    void step_cpu_brute_force(double dt, double softening);
    void step_cpu_barnes_hut(double dt, double softening);
    void step_gpu_brute_force(double dt, double softening);
    void step_gpu_barnes_hut(double dt, double softening);

    // ── Yoshida 4th-order step methods (Phase 13) ──
    void step_yoshida4_cpu_brute_force(double dt, double softening);
    void step_yoshida4_cpu_barnes_hut(double dt, double softening);
    void step_yoshida4_gpu_brute_force(double dt, double softening);
    void step_yoshida4_gpu_barnes_hut(double dt, double softening);

    // ── CPU force computation helpers ──
    void compute_cpu_brute_forces(double softening);
    void compute_cpu_barnes_hut_forces(double softening);

    // ── CPU Yoshida sub-step helper (half-kick, drift, forces, half-kick) ──
    void cpu_yoshida_substep(double sub_dt, double softening, bool use_barnes_hut);

    // ── Collision detection & resolution (Phase 13) ──
    void detect_and_resolve_collisions();

    // ── Phase 14-15: Compact dead bodies after merges ──
    void compact_after_merges();

    // ── Adaptive timestep: compute max acceleration (Phase 13) ──
    double compute_max_acceleration();

    // ── Phase 13 hooks ──
    void apply_phase13_pre_force();
    void apply_phase13_post_force(double dt);
    void apply_phase13_post_position(double dt);

    // ── Benchmark recording ──
    void record_benchmark(const profile::FrameProfile& fp, double sort_ms,
                          double build_ms, double traverse_ms);

    // ── Members ──
    SimulationConfig config_;
    physics::ParticleSystem particles_;
    physics::BarnesHutSolver bh_solver_;
    physics::PostNewtonianCorrection pn_correction_;
    physics::CollisionDetector collision_detector_;
    physics::CollisionResolver collision_resolver_;
    physics::DensityModel density_model_;
    std::vector<physics::CollisionPair> collision_pairs_;
    Timestep timestep_;
    AsyncPipeline gpu_pipeline_;
    DeterministicMode deterministic_;
    AdaptiveTimestep adaptive_timestep_;
    memory::GpuPool gpu_pool_;

#if CELESTIAL_HAS_CUDA
    cuda::GpuTreeSolver gpu_tree_solver_;
#endif

    profile::FrameProfiler profiler_;
    profile::EnergyTracker energy_tracker_;
    profile::BenchmarkLogger benchmark_;

    hooks::Phase13Hooks* phase13_hooks_ = nullptr;

    /// Phase 18+19: Last GPU validation result
    GpuValidationResult last_gpu_validation_{};

    /// Phase 21: Telemetry callback (null = disabled)
    TelemetryCallback telemetry_callback_ = nullptr;

    bool initialized_ = false;
    i64 total_steps_ = 0;
};

} // namespace celestial::sim
