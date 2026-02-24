#ifndef CELESTIAL_NATIVE_API_H
#define CELESTIAL_NATIVE_API_H

#include <stdint.h>

/* ── Phase 21: API Version ─────────────────────────────────────────────── */

#define CELESTIAL_API_VERSION_MAJOR 1
#define CELESTIAL_API_VERSION_MINOR 0
#define CELESTIAL_API_VERSION_PATCH 0

#ifdef _WIN32
  #ifdef CELESTIAL_EXPORTS
    #define CELESTIAL_API __declspec(dllexport)
  #else
    #define CELESTIAL_API __declspec(dllimport)
  #endif
#else
  #define CELESTIAL_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ── Lifecycle ──────────────────────────────────────────────────────────── */

/// Initialize the engine. max_particles = capacity for particle arrays.
/// Returns 0 on success, negative error code on failure.
CELESTIAL_API int32_t celestial_init(int32_t max_particles);

/// Shutdown and release all resources.
CELESTIAL_API void celestial_shutdown(void);

/* ── Data Transfer ──────────────────────────────────────────────────────── */

/// Upload particle data from host arrays. count must be <= max_particles.
CELESTIAL_API void celestial_set_particles(
    const double* pos_x, const double* pos_y, const double* pos_z,
    const double* vel_x, const double* vel_y, const double* vel_z,
    const double* acc_x, const double* acc_y, const double* acc_z,
    const double* mass, const double* radius,
    const uint8_t* is_active, int32_t count);

/// Download computed accelerations to host arrays.
CELESTIAL_API void celestial_get_accelerations(
    double* out_acc_x, double* out_acc_y, double* out_acc_z, int32_t count);

/// Download current positions to host arrays.
CELESTIAL_API void celestial_get_positions(
    double* out_pos_x, double* out_pos_y, double* out_pos_z, int32_t count);

/// Download current velocities to host arrays.
CELESTIAL_API void celestial_get_velocities(
    double* out_vel_x, double* out_vel_y, double* out_vel_z, int32_t count);

/* ── Physics Step ───────────────────────────────────────────────────────── */

/// Compute forces only (no integration). Fills acc arrays.
CELESTIAL_API void celestial_compute_forces(double softening);

/// Execute one full Verlet step (half-kick + drift + forces + half-kick + rotate).
CELESTIAL_API void celestial_step(double dt, double softening);

/// Accumulator-based update: feed frame_time, returns number of sub-steps taken.
CELESTIAL_API int32_t celestial_update(double frame_time);

/* ── Configuration ──────────────────────────────────────────────────────── */

/// Set compute mode: 0=CPU_BruteForce, 1=CPU_BarnesHut, 2=GPU_BruteForce, 3=GPU_BarnesHut
CELESTIAL_API void celestial_set_compute_mode(int32_t mode);

/// Set Barnes-Hut opening angle theta.
CELESTIAL_API void celestial_set_theta(double theta);

/// Enable/disable post-Newtonian corrections (0=off, 1=on).
CELESTIAL_API void celestial_set_enable_pn(int32_t enable);

/// Set fixed timestep.
CELESTIAL_API void celestial_set_dt(double dt);

/// Set softening parameter.
CELESTIAL_API void celestial_set_softening(double softening);

/// Set GPU memory pool size in bytes (must be called before celestial_init).
CELESTIAL_API void celestial_set_gpu_pool_size(uint64_t bytes);

/* ── Deterministic Mode ─────────────────────────────────────────────────── */

/// Enable/disable deterministic mode (0=off, 1=on).
CELESTIAL_API void celestial_set_deterministic(int32_t enable);

/// Query deterministic mode state. Returns 1 if enabled, 0 if disabled.
CELESTIAL_API int32_t celestial_is_deterministic(void);

/// Set deterministic mode RNG seed.
CELESTIAL_API void celestial_set_deterministic_seed(uint64_t seed);

/* ── Energy Tracking ───────────────────────────────────────────────────── */

/// Compute and record an energy snapshot for the current state.
CELESTIAL_API void celestial_compute_energy_snapshot(void);

/// Get relative energy drift since first snapshot. Returns 0 if no snapshots.
CELESTIAL_API double celestial_get_energy_drift(void);

/// Get relative momentum drift since first snapshot. Returns 0 if no snapshots.
CELESTIAL_API double celestial_get_momentum_drift(void);

/// Get accumulated integration error. Returns 0 if no snapshots.
CELESTIAL_API double celestial_get_accumulated_error(void);

/* ── Profiling ──────────────────────────────────────────────────────────── */

/// Get last frame's GPU computation time in milliseconds.
CELESTIAL_API double celestial_get_last_gpu_time_ms(void);

/// Get last frame's CPU computation time in milliseconds.
CELESTIAL_API double celestial_get_last_cpu_time_ms(void);

/// Get total device memory allocated in bytes.
CELESTIAL_API int64_t celestial_get_device_memory_bytes(void);

/// Get current particle count.
CELESTIAL_API int32_t celestial_get_particle_count(void);

/* ── Benchmark ─────────────────────────────────────────────────────────── */

/// Get average frame time over the rolling window (ms).
CELESTIAL_API double celestial_get_avg_frame_time_ms(void);

/// Get estimated FPS based on rolling average.
CELESTIAL_API double celestial_get_estimated_fps(void);

/// Check if current performance meets target for the body count.
/// Returns 1 if target is met, 0 otherwise.
CELESTIAL_API int32_t celestial_meets_performance_target(void);

/// Get last frame's tree build time (Barnes-Hut modes only, ms).
CELESTIAL_API double celestial_get_last_tree_build_ms(void);

/// Get last frame's tree traverse time (Barnes-Hut modes only, ms).
CELESTIAL_API double celestial_get_last_tree_traverse_ms(void);

/* ── Phase 13: Integrator Selection ────────────────────────────────────── */

/// Set integrator type: 0=Leapfrog_KDK (default), 1=Yoshida4
CELESTIAL_API void celestial_set_integrator(int32_t type);

/// Get current integrator type.
CELESTIAL_API int32_t celestial_get_integrator(void);

/* ── Phase 13: Adaptive Timestep ───────────────────────────────────────── */

/// Enable adaptive timestep with given parameters.
/// eta: safety factor, dt_min/dt_max: bounds, initial_dt: starting dt.
CELESTIAL_API void celestial_set_adaptive_timestep(
    int32_t enabled, double eta, double dt_min, double dt_max, double initial_dt);

/// Query current adaptive dt value.
CELESTIAL_API double celestial_get_adaptive_dt(void);

/// Query whether adaptive dt is enabled. Returns 1 if enabled, 0 if disabled.
CELESTIAL_API int32_t celestial_is_adaptive_dt_enabled(void);

/* ── Phase 13: Angular Momentum & COM ──────────────────────────────────── */

/// Get angular momentum drift magnitude since initial snapshot.
CELESTIAL_API double celestial_get_angular_momentum_drift(void);

/// Get center-of-mass position drift since initial snapshot.
CELESTIAL_API double celestial_get_com_position_drift(void);

/// Get center-of-mass velocity drift since initial snapshot.
CELESTIAL_API double celestial_get_com_velocity_drift(void);

/// Get virial ratio (2*KE/|PE|) from last energy snapshot.
CELESTIAL_API double celestial_get_virial_ratio(void);

/// Check all conservation diagnostics against default thresholds.
/// Returns 1 if all pass, 0 if any violation detected.
CELESTIAL_API int32_t celestial_check_conservation_diagnostics(void);

/* ── Phase 13: Collision Handling ──────────────────────────────────────── */

/// Set collision mode: 0=Ignore, 1=Elastic, 2=Inelastic, 3=Merge
CELESTIAL_API void celestial_set_collision_mode(int32_t mode);

/// Set coefficient of restitution for inelastic collisions.
CELESTIAL_API void celestial_set_collision_restitution(double e);

/// Get number of collisions detected in last step.
CELESTIAL_API int32_t celestial_get_collision_count(void);

/* ── Phase 13: Softening Configuration ─────────────────────────────────── */

/// Set softening mode: 0=Global, 1=PerBodyType, 2=Adaptive
CELESTIAL_API void celestial_set_softening_mode(int32_t mode);

/// Set per-body-type softening value. type_index in [0, 15].
CELESTIAL_API void celestial_set_type_softening(int32_t type_index, double eps);

/// Set adaptive softening scale factor (eps_i = scale * m_i^(1/3)).
CELESTIAL_API void celestial_set_adaptive_softening_scale(double scale);

/* ── Phase 14-15: Density & Compaction ─────────────────────────────────── */

/// Set density model configuration.
CELESTIAL_API void celestial_set_density_config(double default_density, double min_radius);

/// Set maximum merges per frame (merge safeguard).
CELESTIAL_API void celestial_set_max_merges_per_frame(int32_t max_merges);

/// Set maximum merges per body per frame (merge safeguard).
CELESTIAL_API void celestial_set_max_merges_per_body(int32_t max_merges);

/// Enable/disable density-preserving merge (0=off, 1=on).
CELESTIAL_API void celestial_set_density_preserving_merge(int32_t enabled);

/// Get number of active (non-merged) particles.
CELESTIAL_API int32_t celestial_get_active_particle_count(void);

/// Get number of merges performed in last step.
CELESTIAL_API int32_t celestial_get_last_merge_count(void);

/// Manually trigger particle compaction. Returns new particle count.
CELESTIAL_API int32_t celestial_compact_particles(void);

/* ── Phase 16-17: Rolling Averages & Diagnostics ──────────────────────── */

/// Get rolling average of total energy over the last 300 snapshots.
CELESTIAL_API double celestial_get_rolling_avg_energy(void);

/// Get rolling average of energy drift over the last 300 snapshots.
CELESTIAL_API double celestial_get_rolling_avg_drift(void);

/// Enable/disable per-step auto-compute of energy diagnostics (0=off, 1=on).
CELESTIAL_API void celestial_set_enable_diagnostics(int32_t enabled);

/* ── Phase 18+19: GPU Validation & Energy ─────────────────────────────── */

/// Enable/disable CPU vs GPU validation mode (0=off, 1=on).
/// When enabled, each step computes both CPU and GPU energy and checks parity.
CELESTIAL_API void celestial_set_enable_gpu_validation(int32_t enabled);

/// Set tolerance for GPU vs CPU validation (default 1e-6).
CELESTIAL_API void celestial_set_gpu_validation_tolerance(double tolerance);

/// Run CPU vs GPU validation. Returns 1 if passed, 0 if failed.
CELESTIAL_API int32_t celestial_validate_gpu_cpu_parity(void);

/// Get last GPU validation KE relative error.
CELESTIAL_API double celestial_get_gpu_validation_ke_error(void);

/// Get last GPU validation PE relative error.
CELESTIAL_API double celestial_get_gpu_validation_pe_error(void);

/// Get last GPU validation mass error.
CELESTIAL_API double celestial_get_gpu_validation_mass_error(void);

/// Compute energy snapshot on GPU (no CPU round-trip for energy).
/// Only works in GPU_BarnesHut mode. Falls back to CPU snapshot otherwise.
CELESTIAL_API void celestial_compute_gpu_energy_snapshot(void);

/* ── Phase 21: API Version & Capabilities ──────────────────────────────── */

/// API version struct for runtime version query.
typedef struct CelestialVersion {
    int32_t major;
    int32_t minor;
    int32_t patch;
} CelestialVersion;

/// Get API version at runtime.
CELESTIAL_API CelestialVersion celestial_api_version(void);

/// Capability flags (bitfield).
typedef enum CelestialCapability {
    CELESTIAL_CAP_CUDA             = (1 << 0),  ///< CUDA support compiled in
    CELESTIAL_CAP_BARNES_HUT       = (1 << 1),  ///< Barnes-Hut solver available
    CELESTIAL_CAP_YOSHIDA4         = (1 << 2),  ///< Yoshida 4th-order integrator
    CELESTIAL_CAP_POST_NEWTONIAN   = (1 << 3),  ///< Post-Newtonian corrections
    CELESTIAL_CAP_GPU_TREE         = (1 << 4),  ///< GPU tree solver (GPU_BarnesHut)
    CELESTIAL_CAP_GPU_MERGE        = (1 << 5),  ///< GPU-resident merge pipeline
    CELESTIAL_CAP_ADAPTIVE_DT      = (1 << 6),  ///< Adaptive timestep
    CELESTIAL_CAP_DETERMINISTIC    = (1 << 7)   ///< Deterministic mode
} CelestialCapability;

/// Query engine capabilities as a bitfield of CelestialCapability flags.
CELESTIAL_API uint32_t celestial_query_capabilities(void);

/// Get last error message (human-readable). Returns pointer to static buffer.
/// The returned string is valid until the next error occurs.
CELESTIAL_API const char* celestial_get_last_error_message(void);

/* ── Phase 21: Simulation Snapshot (UI Readiness) ──────────────────────── */

/// Complete simulation state snapshot for UI consumption.
/// A single struct copy per frame — minimal overhead.
typedef struct CelestialSnapshot {
    int64_t  step_number;
    int32_t  particle_count;
    int32_t  active_particle_count;
    double   total_energy;
    double   kinetic_energy;
    double   potential_energy;
    double   momentum_x;
    double   momentum_y;
    double   momentum_z;
    double   momentum_magnitude;
    double   angular_momentum_x;
    double   angular_momentum_y;
    double   angular_momentum_z;
    double   angular_momentum_magnitude;
    double   com_x;
    double   com_y;
    double   com_z;
    double   total_mass;
    double   energy_drift;
    double   virial_ratio;
    int32_t  last_collision_count;
    int32_t  last_merge_count;
    double   frame_time_ms;
} CelestialSnapshot;

/// Fill a snapshot struct with the current simulation state.
/// Requires a prior call to celestial_compute_energy_snapshot() for energy fields.
CELESTIAL_API void celestial_get_snapshot(CelestialSnapshot* out);

/* ── Phase 21: Memory Statistics ───────────────────────────────────────── */

/// Memory statistics for monitoring resource usage.
typedef struct CelestialMemoryStats {
    int64_t  gpu_allocated_bytes;      ///< Total GPU memory allocated
    int64_t  gpu_pool_total_bytes;     ///< GPU scratch pool capacity
    int32_t  particle_count;           ///< Current particle count
    int32_t  particle_capacity;        ///< Max particle capacity
} CelestialMemoryStats;

/// Fill a memory stats struct with current resource usage.
CELESTIAL_API void celestial_get_memory_stats(CelestialMemoryStats* out);

/* ── Phase 21: Telemetry Callback ──────────────────────────────────────── */

/// Telemetry callback type. Called at end of each step with event name and value.
/// event_name is a static string (do not free). Value meaning depends on event.
/// Events: "step_time_ms", "force_time_ms", "collision_count", "merge_count",
///         "particle_count", "energy_drift"
typedef void (*CelestialTelemetryCallback)(const char* event_name, double value);

/// Set telemetry callback. Pass NULL to disable. Callback fires at end of each step.
CELESTIAL_API void celestial_set_telemetry_callback(CelestialTelemetryCallback callback);

/* ── Phase 21: Configuration Serialization ─────────────────────────────── */

/// Serialize current engine configuration to JSON.
/// Writes at most buf_size bytes (including null terminator) to buf.
/// Returns the number of bytes required (excluding null terminator).
/// If buf is NULL or buf_size is 0, only returns the required size.
CELESTIAL_API int32_t celestial_get_config_json(char* buf, int32_t buf_size);

/// Apply configuration from a JSON string.
/// Unknown keys are silently ignored. Missing keys retain current values.
/// Returns 0 on success, negative error code on parse failure.
CELESTIAL_API int32_t celestial_set_config_json(const char* json);

#ifdef __cplusplus
}
#endif

#endif /* CELESTIAL_NATIVE_API_H */
