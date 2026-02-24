#include <celestial/interop/native_api.h>
#include <celestial/sim/engine.hpp>
#include <celestial/sim/config_serializer.hpp>
#include <celestial/profile/memory_tracker.hpp>
#include <cstring>
#include <cmath>

// Static engine instance
static celestial::sim::Engine* g_engine = nullptr;
static celestial::sim::SimulationConfig g_config{};

// Phase 21: Last error message buffer
static char g_last_error[512] = "";

// Phase 21: Telemetry callback
static CelestialTelemetryCallback g_telemetry_callback = nullptr;

// Phase 21: Helper to set last error message
static void set_last_error(const char* msg) {
    if (msg) {
        std::strncpy(g_last_error, msg, sizeof(g_last_error) - 1);
        g_last_error[sizeof(g_last_error) - 1] = '\0';
    } else {
        g_last_error[0] = '\0';
    }
}

extern "C" {

int32_t celestial_init(int32_t max_particles) {
    try {
        if (g_engine) {
            set_last_error("Engine already initialized");
            return static_cast<int32_t>(celestial::core::ErrorCode::AlreadyInitialized);
        }
        g_engine = new celestial::sim::Engine();
        g_config.max_particles = max_particles;
        g_engine->init(g_config);
        // Phase 21: Restore telemetry callback if set before init
        if (g_telemetry_callback) {
            g_engine->set_telemetry_callback(g_telemetry_callback);
        }
        set_last_error(nullptr);
        return 0;
    } catch (const celestial::core::CelestialException& e) {
        set_last_error(e.what());
        return static_cast<int32_t>(e.code());
    } catch (const std::exception& e) {
        set_last_error(e.what());
        return -100;
    } catch (...) {
        set_last_error("Unknown error during initialization");
        return -100;
    }
}

void celestial_shutdown(void) {
    if (g_engine) {
        g_engine->shutdown();
        delete g_engine;
        g_engine = nullptr;
    }
}

void celestial_set_particles(
    const double* pos_x, const double* pos_y, const double* pos_z,
    const double* vel_x, const double* vel_y, const double* vel_z,
    const double* acc_x, const double* acc_y, const double* acc_z,
    const double* mass, const double* radius,
    const uint8_t* is_active, int32_t count)
{
    if (!g_engine) return;
    try {
        g_engine->set_particles(pos_x, pos_y, pos_z,
                                vel_x, vel_y, vel_z,
                                acc_x, acc_y, acc_z,
                                mass, radius, is_active, count);
    } catch (...) {}
}

void celestial_get_accelerations(
    double* out_acc_x, double* out_acc_y, double* out_acc_z, int32_t count)
{
    if (!g_engine) return;
    g_engine->get_accelerations(out_acc_x, out_acc_y, out_acc_z, count);
}

void celestial_get_positions(
    double* out_pos_x, double* out_pos_y, double* out_pos_z, int32_t count)
{
    if (!g_engine) return;
    g_engine->get_positions(out_pos_x, out_pos_y, out_pos_z, count);
}

void celestial_get_velocities(
    double* out_vel_x, double* out_vel_y, double* out_vel_z, int32_t count)
{
    if (!g_engine) return;
    g_engine->get_velocities(out_vel_x, out_vel_y, out_vel_z, count);
}

void celestial_compute_forces(double softening) {
    if (!g_engine) return;
    try {
        g_engine->step(0.0, softening);
    } catch (...) {}
}

void celestial_step(double dt, double softening) {
    if (!g_engine) return;
    try {
        g_engine->step(dt, softening);
    } catch (...) {}
}

int32_t celestial_update(double frame_time) {
    if (!g_engine) return 0;
    try {
        return g_engine->update(frame_time);
    } catch (...) {
        return 0;
    }
}

void celestial_set_compute_mode(int32_t mode) {
    if (!g_engine) return;
    auto m = static_cast<celestial::sim::SimulationConfig::ComputeMode>(mode);
    g_engine->set_compute_mode(m);
    g_config.compute_mode = m;
}

void celestial_set_theta(double theta) {
    if (!g_engine) return;
    g_engine->set_theta(theta);
    g_config.theta = theta;
}

void celestial_set_enable_pn(int32_t enable) {
    if (!g_engine) return;
    g_engine->set_enable_pn(enable != 0);
    g_config.enable_pn = (enable != 0);
}

void celestial_set_dt(double dt) {
    if (!g_engine) return;
    g_config.dt = dt;
}

void celestial_set_softening(double softening) {
    if (!g_engine) return;
    g_config.softening = softening;
}

void celestial_set_gpu_pool_size(uint64_t bytes) {
    g_config.gpu_memory_pool_size = static_cast<size_t>(bytes);
}

// ── Deterministic Mode ──────────────────────────────────────────────────

void celestial_set_deterministic(int32_t enable) {
    if (!g_engine) return;
    g_engine->set_deterministic(enable != 0);
    g_config.deterministic = (enable != 0);
}

int32_t celestial_is_deterministic(void) {
    if (!g_engine) return 0;
    return g_engine->is_deterministic() ? 1 : 0;
}

void celestial_set_deterministic_seed(uint64_t seed) {
    if (!g_engine) return;
    g_engine->set_deterministic_seed(seed);
    g_config.deterministic_seed = seed;
}

// ── Energy Tracking ─────────────────────────────────────────────────────

void celestial_compute_energy_snapshot(void) {
    if (!g_engine) return;
    try {
        g_engine->compute_energy_snapshot();
    } catch (...) {}
}

double celestial_get_energy_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->energy_drift();
}

double celestial_get_momentum_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->momentum_drift();
}

double celestial_get_accumulated_error(void) {
    if (!g_engine) return 0.0;
    return g_engine->accumulated_error();
}

// ── Profiling ───────────────────────────────────────────────────────────

double celestial_get_last_gpu_time_ms(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_gpu_time_ms();
}

double celestial_get_last_cpu_time_ms(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_cpu_time_ms();
}

int64_t celestial_get_device_memory_bytes(void) {
    return celestial::profile::MemoryTracker::instance().device_allocated();
}

int32_t celestial_get_particle_count(void) {
    if (!g_engine) return 0;
    return g_engine->particle_count();
}

// ── Benchmark ───────────────────────────────────────────────────────────

double celestial_get_avg_frame_time_ms(void) {
    if (!g_engine) return 0.0;
    return g_engine->benchmark_logger().average().total_frame_ms;
}

double celestial_get_estimated_fps(void) {
    if (!g_engine) return 0.0;
    return g_engine->benchmark_logger().estimated_fps();
}

int32_t celestial_meets_performance_target(void) {
    if (!g_engine) return 0;
    return g_engine->benchmark_logger().meets_performance_target() ? 1 : 0;
}

double celestial_get_last_tree_build_ms(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_benchmark().tree_build_ms;
}

double celestial_get_last_tree_traverse_ms(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_benchmark().tree_traverse_ms;
}

// ── Phase 13: Integrator Selection ──────────────────────────────────────

void celestial_set_integrator(int32_t type) {
    if (!g_engine) return;
    auto t = static_cast<celestial::sim::IntegratorType>(type);
    g_engine->set_integrator(t);
    g_config.integrator = t;
}

int32_t celestial_get_integrator(void) {
    if (!g_engine) return 0;
    return static_cast<int32_t>(g_engine->integrator());
}

// ── Phase 13: Adaptive Timestep ─────────────────────────────────────────

void celestial_set_adaptive_timestep(
    int32_t enabled, double eta, double dt_min, double dt_max, double initial_dt)
{
    if (!g_engine) return;
    celestial::sim::AdaptiveTimestepConfig cfg;
    cfg.enabled = (enabled != 0);
    cfg.eta = eta;
    cfg.dt_min = dt_min;
    cfg.dt_max = dt_max;
    cfg.initial_dt = initial_dt;
    g_engine->set_adaptive_timestep(cfg);
    g_config.adaptive_dt = cfg;
}

double celestial_get_adaptive_dt(void) {
    if (!g_engine) return 0.0;
    return g_engine->current_adaptive_dt();
}

int32_t celestial_is_adaptive_dt_enabled(void) {
    if (!g_engine) return 0;
    return g_engine->is_adaptive_dt_enabled() ? 1 : 0;
}

// ── Phase 13: Angular Momentum & COM ────────────────────────────────────

double celestial_get_angular_momentum_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->angular_momentum_drift();
}

double celestial_get_com_position_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->com_position_drift();
}

double celestial_get_com_velocity_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->com_velocity_drift();
}

double celestial_get_virial_ratio(void) {
    if (!g_engine) return 0.0;
    return g_engine->energy_tracker().current().virial_ratio;
}

int32_t celestial_check_conservation_diagnostics(void) {
    if (!g_engine) return 1;
    celestial::profile::DiagnosticThresholds defaults{};
    return g_engine->check_conservation_diagnostics(defaults) ? 1 : 0;
}

// ── Phase 13: Collision Handling ────────────────────────────────────────

void celestial_set_collision_mode(int32_t mode) {
    if (!g_engine) return;
    auto m = static_cast<celestial::physics::CollisionMode>(mode);
    g_engine->set_collision_mode(m);
    g_config.collision_config.mode = m;
}

void celestial_set_collision_restitution(double e) {
    if (!g_engine) return;
    g_engine->set_collision_restitution(e);
    g_config.collision_config.restitution = e;
}

int32_t celestial_get_collision_count(void) {
    if (!g_engine) return 0;
    return g_engine->last_collision_count();
}

// ── Phase 13: Softening Configuration ───────────────────────────────────

void celestial_set_softening_mode(int32_t mode) {
    if (!g_engine) return;
    auto m = static_cast<celestial::physics::SofteningMode>(mode);
    g_engine->set_softening_mode(m);
    g_config.softening_config.mode = m;
}

void celestial_set_type_softening(int32_t type_index, double eps) {
    if (!g_engine) return;
    g_engine->set_type_softening(type_index, eps);
    if (type_index >= 0 && type_index < celestial::physics::MAX_BODY_TYPES) {
        g_config.softening_config.type_softening[type_index] = eps;
    }
}

void celestial_set_adaptive_softening_scale(double scale) {
    if (!g_engine) return;
    g_engine->set_adaptive_softening_scale(scale);
    g_config.softening_config.adaptive_scale = scale;
}

// ── Phase 14-15: Density & Compaction ───────────────────────────────────

void celestial_set_density_config(double default_density, double min_radius) {
    celestial::physics::DensityConfig cfg;
    cfg.default_density = default_density;
    cfg.min_radius = min_radius;
    g_config.density_config = cfg;
    if (g_engine) {
        g_engine->set_density_config(cfg);
    }
}

void celestial_set_max_merges_per_frame(int32_t max_merges) {
    g_config.collision_config.max_merges_per_frame = max_merges;
    if (g_engine) {
        g_engine->set_max_merges_per_frame(max_merges);
    }
}

void celestial_set_max_merges_per_body(int32_t max_merges) {
    g_config.collision_config.max_merges_per_body = max_merges;
    if (g_engine) {
        g_engine->set_max_merges_per_body(max_merges);
    }
}

void celestial_set_density_preserving_merge(int32_t enabled) {
    g_config.collision_config.density_preserving_merge = (enabled != 0);
    if (g_engine) {
        g_engine->set_density_preserving_merge(enabled != 0);
    }
}

int32_t celestial_get_active_particle_count(void) {
    if (!g_engine) return 0;
    return g_engine->active_particle_count();
}

int32_t celestial_get_last_merge_count(void) {
    if (!g_engine) return 0;
    return g_engine->last_merge_count();
}

int32_t celestial_compact_particles(void) {
    if (!g_engine) return 0;
    return g_engine->compact_particles();
}

// ── Phase 16-17: Rolling Averages & Diagnostics ─────────────────────────

double celestial_get_rolling_avg_energy(void) {
    if (!g_engine) return 0.0;
    return g_engine->rolling_avg_energy();
}

double celestial_get_rolling_avg_drift(void) {
    if (!g_engine) return 0.0;
    return g_engine->rolling_avg_drift();
}

void celestial_set_enable_diagnostics(int32_t enabled) {
    if (!g_engine) return;
    g_engine->set_enable_diagnostics(enabled != 0);
}

// ── Phase 18+19: GPU Validation & Energy ────────────────────────────────

void celestial_set_enable_gpu_validation(int32_t enabled) {
    if (!g_engine) return;
    g_engine->set_enable_gpu_validation(enabled != 0);
}

void celestial_set_gpu_validation_tolerance(double tolerance) {
    if (!g_engine) return;
    g_engine->set_gpu_validation_tolerance(tolerance);
}

int32_t celestial_validate_gpu_cpu_parity(void) {
    if (!g_engine) return 1;
    try {
        auto result = g_engine->validate_gpu_cpu_parity();
        return result.passed ? 1 : 0;
    } catch (...) {
        return 0;
    }
}

double celestial_get_gpu_validation_ke_error(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_gpu_validation().ke_relative_error;
}

double celestial_get_gpu_validation_pe_error(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_gpu_validation().pe_relative_error;
}

double celestial_get_gpu_validation_mass_error(void) {
    if (!g_engine) return 0.0;
    return g_engine->last_gpu_validation().mass_error;
}

void celestial_compute_gpu_energy_snapshot(void) {
    if (!g_engine) return;
    try {
        g_engine->compute_gpu_energy_snapshot();
    } catch (...) {}
}

// ── Phase 21: API Version & Capabilities ────────────────────────────────

CelestialVersion celestial_api_version(void) {
    CelestialVersion v;
    v.major = CELESTIAL_API_VERSION_MAJOR;
    v.minor = CELESTIAL_API_VERSION_MINOR;
    v.patch = CELESTIAL_API_VERSION_PATCH;
    return v;
}

uint32_t celestial_query_capabilities(void) {
    uint32_t caps = 0;

    // Always-available capabilities (compiled into every build)
    caps |= CELESTIAL_CAP_BARNES_HUT;
    caps |= CELESTIAL_CAP_YOSHIDA4;
    caps |= CELESTIAL_CAP_POST_NEWTONIAN;
    caps |= CELESTIAL_CAP_ADAPTIVE_DT;
    caps |= CELESTIAL_CAP_DETERMINISTIC;

#if CELESTIAL_HAS_CUDA
    caps |= CELESTIAL_CAP_CUDA;
    caps |= CELESTIAL_CAP_GPU_TREE;
    caps |= CELESTIAL_CAP_GPU_MERGE;
#endif

    return caps;
}

const char* celestial_get_last_error_message(void) {
    return g_last_error;
}

// ── Phase 21: Simulation Snapshot ───────────────────────────────────────

void celestial_get_snapshot(CelestialSnapshot* out) {
    if (!out) return;
    std::memset(out, 0, sizeof(CelestialSnapshot));

    if (!g_engine) return;

    const auto& tracker = g_engine->energy_tracker();
    const auto& snap = tracker.current();

    out->step_number             = snap.step_number;
    out->particle_count          = g_engine->particle_count();
    out->active_particle_count   = g_engine->active_particle_count();
    out->total_energy            = snap.total_energy;
    out->kinetic_energy          = snap.kinetic_energy;
    out->potential_energy        = snap.potential_energy;
    out->momentum_x              = snap.momentum_x;
    out->momentum_y              = snap.momentum_y;
    out->momentum_z              = snap.momentum_z;
    out->momentum_magnitude      = snap.momentum_magnitude;
    out->angular_momentum_x      = snap.angular_momentum_x;
    out->angular_momentum_y      = snap.angular_momentum_y;
    out->angular_momentum_z      = snap.angular_momentum_z;
    out->angular_momentum_magnitude = snap.angular_momentum_magnitude;
    out->com_x                   = snap.com_x;
    out->com_y                   = snap.com_y;
    out->com_z                   = snap.com_z;
    out->total_mass              = snap.total_mass;
    out->energy_drift            = tracker.energy_drift();
    out->virial_ratio            = snap.virial_ratio;
    out->last_collision_count    = g_engine->last_collision_count();
    out->last_merge_count        = g_engine->last_merge_count();
    out->frame_time_ms           = g_engine->last_cpu_time_ms() + g_engine->last_gpu_time_ms();
}

// ── Phase 21: Memory Statistics ─────────────────────────────────────────

void celestial_get_memory_stats(CelestialMemoryStats* out) {
    if (!out) return;
    std::memset(out, 0, sizeof(CelestialMemoryStats));

    out->gpu_allocated_bytes = celestial::profile::MemoryTracker::instance().device_allocated();

    if (g_engine) {
        out->gpu_pool_total_bytes = static_cast<int64_t>(g_engine->gpu_pool().scratch_capacity());
        out->particle_count       = g_engine->particle_count();
        out->particle_capacity    = g_engine->gpu_pool().capacity();
    }
}

// ── Phase 21: Telemetry Callback ────────────────────────────────────────

void celestial_set_telemetry_callback(CelestialTelemetryCallback callback) {
    g_telemetry_callback = callback;
    if (g_engine) {
        g_engine->set_telemetry_callback(callback);
    }
}

// ── Phase 21: Configuration Serialization ───────────────────────────────

int32_t celestial_get_config_json(char* buf, int32_t buf_size) {
    std::string json = celestial::sim::ConfigSerializer::to_json(g_config);
    int32_t required = static_cast<int32_t>(json.size());

    if (buf && buf_size > 0) {
        int32_t copy_len = (required < buf_size) ? required : (buf_size - 1);
        std::memcpy(buf, json.c_str(), static_cast<size_t>(copy_len));
        buf[copy_len] = '\0';
    }

    return required;
}

int32_t celestial_set_config_json(const char* json) {
    if (!json) {
        set_last_error("NULL JSON string");
        return -1;
    }

    celestial::sim::SimulationConfig new_config = g_config;
    if (!celestial::sim::ConfigSerializer::from_json(json, new_config)) {
        set_last_error("JSON parse error");
        return -2;
    }

    g_config = new_config;

    // Apply hot-reloadable settings to running engine
    if (g_engine) {
        g_engine->set_compute_mode(g_config.compute_mode);
        g_engine->set_theta(g_config.theta);
        g_engine->set_enable_pn(g_config.enable_pn);
        g_engine->set_deterministic(g_config.deterministic);
        g_engine->set_integrator(g_config.integrator);
        g_engine->set_adaptive_timestep(g_config.adaptive_dt);
        g_engine->set_collision_mode(g_config.collision_config.mode);
        g_engine->set_collision_restitution(g_config.collision_config.restitution);
        g_engine->set_max_merges_per_frame(g_config.collision_config.max_merges_per_frame);
        g_engine->set_max_merges_per_body(g_config.collision_config.max_merges_per_body);
        g_engine->set_density_preserving_merge(g_config.collision_config.density_preserving_merge);
        g_engine->set_density_config(g_config.density_config);
        g_engine->set_softening_mode(g_config.softening_config.mode);
        g_engine->set_enable_diagnostics(g_config.enable_diagnostics);
        g_engine->set_enable_gpu_validation(g_config.enable_gpu_validation);
        g_engine->set_gpu_validation_tolerance(g_config.gpu_validation_tolerance);
    }

    set_last_error(nullptr);
    return 0;
}

} // extern "C"
