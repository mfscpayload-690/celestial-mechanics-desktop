#include <celestial/sim/engine.hpp>
#include <celestial/cuda/device_context.hpp>
#include <celestial/job/job_system.hpp>
#include <cstring>
#include <cmath>
#include <chrono>
#include <algorithm>

// Forward declarations for kernel launch functions (defined in .cu files)
namespace celestial::cuda {
    void launch_kick_drift(
        double* d_pos_x, double* d_pos_y, double* d_pos_z,
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
        uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream);

    void launch_kick_rotate(
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
        uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream);

    void launch_pn_correction(
        double* d_pos_x, double* d_pos_y, double* d_pos_z,
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_mass, uint8_t* d_is_active,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, double softening,
        double max_velocity_fraction_c,
        double schwarz_warning_factor,
        cudaStream_t stream);

    // Phase 13: GPU gravity kernel variants
    void launch_gravity_kernel(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_mass, const uint8_t* d_is_active,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, double eps2, cudaStream_t stream);

    void launch_gravity_kernel_per_type(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_mass, const uint8_t* d_is_active,
        const int32_t* d_body_type,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, const double* host_type_softening, cudaStream_t stream);

    void launch_gravity_kernel_adaptive(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_mass, const uint8_t* d_is_active,
        const double* d_softening,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, cudaStream_t stream);

    // Phase 13: GPU max acceleration reduction (for adaptive timestep)
    void launch_max_accel_reduction(
        const double* d_acc_x, const double* d_acc_y, const double* d_acc_z,
        const uint8_t* d_is_active, int32_t n,
        double* d_scratch,
        double& out_max_accel,
        cudaStream_t stream);
}

namespace celestial::sim {

void Engine::init(const SimulationConfig& config) {
    if (initialized_) {
        throw core::CelestialException(
            core::ErrorCode::AlreadyInitialized, "Engine already initialized");
    }

    config_ = config;

    // Initialize job system
    auto& js = job::JobSystem::instance();
    if (!js.is_initialized()) {
        js.init();
    }

    // Initialize CUDA device
    auto& dc = cuda::DeviceContext::instance();
    if (!dc.is_initialized()) {
        dc.init();
    }

    // Allocate particle storage
    particles_.allocate(config_.max_particles);

    // Initialize GPU subsystems
    bool gpu_mode = dc.has_cuda() && (
        config_.compute_mode == SimulationConfig::ComputeMode::GPU_BruteForce ||
        config_.compute_mode == SimulationConfig::ComputeMode::GPU_BarnesHut);

    if (gpu_mode) {
        gpu_pipeline_.init(config_.max_particles);
        gpu_pool_.init(config_.max_particles, config_.gpu_memory_pool_size);

#if CELESTIAL_HAS_CUDA
        if (config_.compute_mode == SimulationConfig::ComputeMode::GPU_BarnesHut) {
            gpu_tree_solver_.init(config_.max_particles);
            gpu_tree_solver_.theta = config_.theta;
        }
#endif
    }

    // Configure timestep
    timestep_.fixed_dt = config_.dt;
    timestep_.max_steps_per_frame = config_.max_steps_per_frame;

    // Configure Barnes-Hut
    bh_solver_.theta = config_.theta;
    bh_solver_.use_parallel = true;

    // Configure deterministic mode
    if (config_.deterministic) {
        deterministic_.set_enabled(true);
        deterministic_.set_seed(config_.deterministic_seed);
    }

    // Phase 13: Configure adaptive timestep
    adaptive_timestep_.configure(config_.adaptive_dt);

    // Phase 13: Configure collision resolver
    collision_resolver_.configure(config_.collision_config);

    // Phase 14-15: Configure density model and wire to collision resolver
    density_model_.configure(config_.density_config);
    collision_resolver_.set_density_model(&density_model_);

    // Phase 13: Initialize softening defaults if needed
    if (config_.softening_config.mode == physics::SofteningMode::Global) {
        config_.softening_config.global_softening = config_.softening;
    }

    initialized_ = true;
}

void Engine::shutdown() {
    if (!initialized_) return;

    if (gpu_pipeline_.is_initialized()) {
        gpu_pipeline_.destroy();
    }

    if (gpu_pool_.is_initialized()) {
        gpu_pool_.destroy();
    }

#if CELESTIAL_HAS_CUDA
    if (gpu_tree_solver_.is_initialized()) {
        gpu_tree_solver_.destroy();
    }
#endif

    particles_.free();
    energy_tracker_.reset();
    adaptive_timestep_.reset();
    benchmark_.reset();
    collision_pairs_.clear();
    initialized_ = false;
    total_steps_ = 0;
}

void Engine::set_particles(
    const double* px, const double* py, const double* pz,
    const double* vx, const double* vy, const double* vz,
    const double* ax, const double* ay, const double* az,
    const double* mass, const double* radius,
    const u8* is_active, i32 count)
{
    if (count > particles_.capacity) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "Particle count exceeds capacity");
    }

    particles_.set_count(count);
    usize dsize = sizeof(double) * static_cast<usize>(count);
    usize u8size = sizeof(u8) * static_cast<usize>(count);

    std::memcpy(particles_.pos_x, px, dsize);
    std::memcpy(particles_.pos_y, py, dsize);
    std::memcpy(particles_.pos_z, pz, dsize);
    std::memcpy(particles_.vel_x, vx, dsize);
    std::memcpy(particles_.vel_y, vy, dsize);
    std::memcpy(particles_.vel_z, vz, dsize);
    std::memcpy(particles_.acc_x, ax, dsize);
    std::memcpy(particles_.acc_y, ay, dsize);
    std::memcpy(particles_.acc_z, az, dsize);
    std::memcpy(particles_.old_acc_x, ax, dsize);
    std::memcpy(particles_.old_acc_y, ay, dsize);
    std::memcpy(particles_.old_acc_z, az, dsize);
    std::memcpy(particles_.mass, mass, dsize);
    std::memcpy(particles_.radius, radius, dsize);
    std::memcpy(particles_.is_active, is_active, u8size);

    // Reset energy tracking for new particle data
    energy_tracker_.reset();
}

void Engine::step(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("Engine::step");
    auto frame_start = std::chrono::high_resolution_clock::now();
    profile::FrameProfile profile{};
    double sort_ms = 0.0, build_ms = 0.0, traverse_ms = 0.0;

    // Phase 13 pre-force hooks
    apply_phase13_pre_force();

    // Phase 13: Dispatch based on integrator type
    if (config_.integrator == IntegratorType::Yoshida4) {
        // Yoshida 4th-order integrator
        switch (config_.compute_mode) {
            case SimulationConfig::ComputeMode::CPU_BruteForce:
                step_yoshida4_cpu_brute_force(dt, softening);
                break;
            case SimulationConfig::ComputeMode::CPU_BarnesHut:
                step_yoshida4_cpu_barnes_hut(dt, softening);
                profile.tree_build_ms = bh_solver_.last_build_time_ms;
                profile.gravity_ms = bh_solver_.last_traversal_time_ms;
                build_ms = bh_solver_.last_build_time_ms;
                traverse_ms = bh_solver_.last_traversal_time_ms;
                break;
            case SimulationConfig::ComputeMode::GPU_BruteForce:
                step_yoshida4_gpu_brute_force(dt, softening);
                break;
            case SimulationConfig::ComputeMode::GPU_BarnesHut:
                step_yoshida4_gpu_barnes_hut(dt, softening);
#if CELESTIAL_HAS_CUDA
                sort_ms = gpu_tree_solver_.last_sort_time_ms;
                build_ms = gpu_tree_solver_.last_build_time_ms;
                traverse_ms = gpu_tree_solver_.last_traverse_time_ms;
                profile.tree_build_ms = build_ms;
                profile.gravity_ms = traverse_ms;
#endif
                break;
        }
    } else {
        // Standard Leapfrog KDK
        switch (config_.compute_mode) {
            case SimulationConfig::ComputeMode::CPU_BruteForce:
                step_cpu_brute_force(dt, softening);
                break;
            case SimulationConfig::ComputeMode::CPU_BarnesHut:
                step_cpu_barnes_hut(dt, softening);
                profile.tree_build_ms = bh_solver_.last_build_time_ms;
                profile.gravity_ms = bh_solver_.last_traversal_time_ms;
                build_ms = bh_solver_.last_build_time_ms;
                traverse_ms = bh_solver_.last_traversal_time_ms;
                break;
            case SimulationConfig::ComputeMode::GPU_BruteForce:
                step_gpu_brute_force(dt, softening);
                break;
            case SimulationConfig::ComputeMode::GPU_BarnesHut:
                step_gpu_barnes_hut(dt, softening);
#if CELESTIAL_HAS_CUDA
                sort_ms = gpu_tree_solver_.last_sort_time_ms;
                build_ms = gpu_tree_solver_.last_build_time_ms;
                traverse_ms = gpu_tree_solver_.last_traverse_time_ms;
                profile.tree_build_ms = build_ms;
                profile.gravity_ms = traverse_ms;
#endif
                break;
        }
    }

    // Phase 13 post-force hooks
    apply_phase13_post_force(dt);

    // Phase 13: Update adaptive timestep from current accelerations
    if (adaptive_timestep_.is_enabled()) {
        double a_max = compute_max_acceleration();
        adaptive_timestep_.update(a_max, softening);
    }

    auto frame_end = std::chrono::high_resolution_clock::now();
    profile.total_frame_ms = std::chrono::duration<double, std::milli>(frame_end - frame_start).count();
    profile.total_cpu_ms = profile.total_frame_ms;
    profiler_.end_frame(profile);

    // Record benchmark metrics
    record_benchmark(profile, sort_ms, build_ms, traverse_ms);

    // Advance deterministic step counter
    if (deterministic_.is_enabled()) {
        deterministic_.advance_step();
    }
    total_steps_++;

    // Phase 16-17: Auto-compute energy diagnostics when enabled
    if (config_.enable_diagnostics) {
        compute_energy_snapshot();
    }

    // Phase 18+19: GPU validation mode — compare CPU vs GPU energy each step
    if (config_.enable_gpu_validation) {
        validate_gpu_cpu_parity();
    }

    // Phase 21: Fire telemetry callback
    if (telemetry_callback_) {
        telemetry_callback_("step_time_ms", profile.total_frame_ms);
        telemetry_callback_("force_time_ms", profile.gravity_ms);
        telemetry_callback_("collision_count", static_cast<double>(collision_resolver_.last_collision_count()));
        telemetry_callback_("merge_count", static_cast<double>(collision_resolver_.last_merge_count()));
        telemetry_callback_("particle_count", static_cast<double>(particles_.count));
        if (config_.enable_diagnostics) {
            telemetry_callback_("energy_drift", energy_tracker_.energy_drift());
        }
    }

    CELESTIAL_NVTX_POP();
}

int Engine::update(double frame_time) {
    // Phase 13: Use adaptive dt when enabled, otherwise fixed dt
    double effective_dt = adaptive_timestep_.is_enabled()
        ? adaptive_timestep_.current_dt()
        : config_.dt;

    int steps = timestep_.update(frame_time);
    for (int i = 0; i < steps; i++) {
        step(effective_dt, config_.softening);

        // When adaptive dt is enabled, update effective_dt for next sub-step
        if (adaptive_timestep_.is_enabled()) {
            effective_dt = adaptive_timestep_.current_dt();
        }
    }
    return steps;
}

void Engine::get_positions(double* out_px, double* out_py, double* out_pz, i32 count) const {
    i32 n = std::min(count, particles_.count);
    usize bytes = sizeof(double) * static_cast<usize>(n);
    std::memcpy(out_px, particles_.pos_x, bytes);
    std::memcpy(out_py, particles_.pos_y, bytes);
    std::memcpy(out_pz, particles_.pos_z, bytes);
}

void Engine::get_velocities(double* out_vx, double* out_vy, double* out_vz, i32 count) const {
    i32 n = std::min(count, particles_.count);
    usize bytes = sizeof(double) * static_cast<usize>(n);
    std::memcpy(out_vx, particles_.vel_x, bytes);
    std::memcpy(out_vy, particles_.vel_y, bytes);
    std::memcpy(out_vz, particles_.vel_z, bytes);
}

void Engine::get_accelerations(double* out_ax, double* out_ay, double* out_az, i32 count) const {
    i32 n = std::min(count, particles_.count);
    usize bytes = sizeof(double) * static_cast<usize>(n);
    std::memcpy(out_ax, particles_.acc_x, bytes);
    std::memcpy(out_ay, particles_.acc_y, bytes);
    std::memcpy(out_az, particles_.acc_z, bytes);
}

void Engine::set_compute_mode(SimulationConfig::ComputeMode mode) {
    config_.compute_mode = mode;

    auto& dc = cuda::DeviceContext::instance();
    bool gpu_mode = (mode == SimulationConfig::ComputeMode::GPU_BruteForce ||
                     mode == SimulationConfig::ComputeMode::GPU_BarnesHut);

    if (gpu_mode && dc.has_cuda()) {
        if (!gpu_pipeline_.is_initialized()) {
            gpu_pipeline_.init(config_.max_particles);
        }
        if (!gpu_pool_.is_initialized()) {
            gpu_pool_.init(config_.max_particles, config_.gpu_memory_pool_size);
        }
#if CELESTIAL_HAS_CUDA
        if (mode == SimulationConfig::ComputeMode::GPU_BarnesHut &&
            !gpu_tree_solver_.is_initialized()) {
            gpu_tree_solver_.init(config_.max_particles);
            gpu_tree_solver_.theta = config_.theta;
        }
#endif
    }
}

void Engine::set_theta(double theta) {
    config_.theta = theta;
    bh_solver_.theta = theta;
#if CELESTIAL_HAS_CUDA
    if (gpu_tree_solver_.is_initialized()) {
        gpu_tree_solver_.theta = theta;
    }
#endif
}

void Engine::set_enable_pn(bool enable) {
    config_.enable_pn = enable;
}

void Engine::set_deterministic(bool enabled) {
    deterministic_.set_enabled(enabled);
    config_.deterministic = enabled;
}

void Engine::compute_energy_snapshot() {
    // Phase 16-17: Use O(N log N) BH PE for large particle counts in BH modes
    bool use_bh_pe = (particles_.count > 256) &&
        (config_.compute_mode == SimulationConfig::ComputeMode::CPU_BarnesHut ||
         config_.compute_mode == SimulationConfig::ComputeMode::GPU_BarnesHut);

    profile::EnergyTracker::Snapshot snap;
    if (use_bh_pe) {
        snap = energy_tracker_.compute_with_bh(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.mass, particles_.is_active,
            particles_.count, config_.softening,
            bh_solver_, particles_);
    } else {
        snap = energy_tracker_.compute(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.mass, particles_.is_active,
            particles_.count, config_.softening);
    }
    snap.step_number = total_steps_;
    energy_tracker_.record(snap);
}

// --------------------------------------------------------------------------
// Phase 13: Adaptive timestep configuration
// --------------------------------------------------------------------------

void Engine::set_adaptive_timestep(const AdaptiveTimestepConfig& cfg) {
    config_.adaptive_dt = cfg;
    adaptive_timestep_.configure(cfg);
}

// --------------------------------------------------------------------------
// Phase 13: Collision configuration
// --------------------------------------------------------------------------

void Engine::set_collision_mode(physics::CollisionMode mode) {
    config_.collision_config.mode = mode;
    collision_resolver_.configure(config_.collision_config);
}

void Engine::set_collision_restitution(double e) {
    config_.collision_config.restitution = e;
    collision_resolver_.configure(config_.collision_config);
}

// --------------------------------------------------------------------------
// Phase 13: Softening configuration
// --------------------------------------------------------------------------

void Engine::set_softening_mode(physics::SofteningMode mode) {
    config_.softening_config.mode = mode;
}

void Engine::set_type_softening(i32 type_index, double eps) {
    if (type_index >= 0 && type_index < physics::MAX_BODY_TYPES) {
        config_.softening_config.type_softening[type_index] = eps;
    }
}

void Engine::set_adaptive_softening_scale(double scale) {
    config_.softening_config.adaptive_scale = scale;
}

// --------------------------------------------------------------------------
// Phase 13: Compute max acceleration (for adaptive timestep)
// --------------------------------------------------------------------------

double Engine::compute_max_acceleration() {
    i32 n = particles_.count;
    if (n <= 0) return 0.0;

    bool use_gpu = (config_.compute_mode == SimulationConfig::ComputeMode::GPU_BruteForce ||
                    config_.compute_mode == SimulationConfig::ComputeMode::GPU_BarnesHut);

#if CELESTIAL_HAS_CUDA
    if (use_gpu && gpu_pool_.is_initialized()) {
        auto& dc = cuda::DeviceContext::instance();
        cudaStream_t stream = dc.stream(0);
        double max_accel = 0.0;
        // Use scratch pool for temporary per-block results
        double* d_scratch = static_cast<double*>(gpu_pool_.scratch_alloc(
            sizeof(double) * static_cast<usize>((n + 255) / 256)));
        if (d_scratch) {
            cuda::launch_max_accel_reduction(
                gpu_pool_.d_acc_x, gpu_pool_.d_acc_y, gpu_pool_.d_acc_z,
                gpu_pool_.d_is_active, n, d_scratch, max_accel, stream);
            return max_accel;
        }
        // Fall through to CPU if scratch alloc fails
    }
#else
    (void)use_gpu;
#endif

    // CPU path: simple loop
    double max_a2 = 0.0;
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        double ax = particles_.acc_x[i];
        double ay = particles_.acc_y[i];
        double az = particles_.acc_z[i];
        double a2 = ax * ax + ay * ay + az * az;
        if (a2 > max_a2) max_a2 = a2;
    }
    return std::sqrt(max_a2);
}

// --------------------------------------------------------------------------
// Phase 13: Collision detection & resolution
// --------------------------------------------------------------------------

void Engine::detect_and_resolve_collisions() {
    if (config_.collision_config.mode == physics::CollisionMode::Ignore) return;
    if (!config_.enable_collisions) return;

    collision_pairs_.clear();
    collision_detector_.detect(particles_, collision_pairs_);

    if (!collision_pairs_.empty()) {
        collision_resolver_.resolve(particles_, collision_pairs_);
        compact_after_merges();
    }
}

// --------------------------------------------------------------------------
// Phase 14-15: Compact dead bodies after merges
// --------------------------------------------------------------------------

void Engine::compact_after_merges() {
    if (config_.collision_config.mode != physics::CollisionMode::Merge) return;
    if (collision_resolver_.last_merge_count() <= 0) return;

    // Refresh densities after merges
    density_model_.update_densities(particles_);

    // Remove inactive bodies
    particles_.compact();
}

// --------------------------------------------------------------------------
// Phase 14-15: Density model and merge safeguard configuration
// --------------------------------------------------------------------------

void Engine::set_density_config(const physics::DensityConfig& cfg) {
    config_.density_config = cfg;
    density_model_.configure(cfg);
}

void Engine::set_max_merges_per_frame(i32 max_merges) {
    config_.collision_config.max_merges_per_frame = max_merges;
    collision_resolver_.configure(config_.collision_config);
}

void Engine::set_max_merges_per_body(i32 max_merges) {
    config_.collision_config.max_merges_per_body = max_merges;
    collision_resolver_.configure(config_.collision_config);
}

void Engine::set_density_preserving_merge(bool enabled) {
    config_.collision_config.density_preserving_merge = enabled;
    collision_resolver_.configure(config_.collision_config);
}

i32 Engine::active_particle_count() const {
    i32 active = 0;
    for (i32 i = 0; i < particles_.count; i++) {
        if (particles_.is_active[i]) active++;
    }
    return active;
}

i32 Engine::compact_particles() {
    density_model_.update_densities(particles_);
    return particles_.compact();
}

// --------------------------------------------------------------------------
// Phase 13: CPU force computation helpers
// --------------------------------------------------------------------------

void Engine::compute_cpu_brute_forces(double softening) {
    i32 n = particles_.count;
    particles_.zero_accelerations();

    auto& scfg = config_.softening_config;

    if (scfg.mode == physics::SofteningMode::PerBodyType) {
        // Per-body-type softening
        for (i32 i = 0; i < n; i++) {
            if (!particles_.is_active[i]) continue;
            double axi = 0.0, ayi = 0.0, azi = 0.0;
            double xi = particles_.pos_x[i], yi = particles_.pos_y[i], zi = particles_.pos_z[i];
            i32 type_i = particles_.body_type_index ? particles_.body_type_index[i] : 0;
            double eps_i = scfg.type_softening[type_i];

            for (i32 j = 0; j < n; j++) {
                if (i == j || !particles_.is_active[j]) continue;
                i32 type_j = particles_.body_type_index ? particles_.body_type_index[j] : 0;
                double eps_j = scfg.type_softening[type_j];
                double eps = (eps_i + eps_j) * 0.5;
                double eps2 = eps * eps;
                double dx = xi - particles_.pos_x[j];
                double dy = yi - particles_.pos_y[j];
                double dz = zi - particles_.pos_z[j];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = 1.0 / std::sqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = particles_.mass[j] * inv_dist3;
                axi -= factor * dx;
                ayi -= factor * dy;
                azi -= factor * dz;
            }
            particles_.acc_x[i] = axi;
            particles_.acc_y[i] = ayi;
            particles_.acc_z[i] = azi;
        }
    } else if (scfg.mode == physics::SofteningMode::Adaptive) {
        // Adaptive softening: eps_i = scale * m_i^(1/3)
        for (i32 i = 0; i < n; i++) {
            if (!particles_.is_active[i]) continue;
            double axi = 0.0, ayi = 0.0, azi = 0.0;
            double xi = particles_.pos_x[i], yi = particles_.pos_y[i], zi = particles_.pos_z[i];
            double eps_i = std::max(
                scfg.adaptive_scale * std::cbrt(particles_.mass[i]),
                scfg.adaptive_min);

            for (i32 j = 0; j < n; j++) {
                if (i == j || !particles_.is_active[j]) continue;
                double eps_j = std::max(
                    scfg.adaptive_scale * std::cbrt(particles_.mass[j]),
                    scfg.adaptive_min);
                double eps = (eps_i + eps_j) * 0.5;
                double eps2 = eps * eps;
                double dx = xi - particles_.pos_x[j];
                double dy = yi - particles_.pos_y[j];
                double dz = zi - particles_.pos_z[j];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = 1.0 / std::sqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = particles_.mass[j] * inv_dist3;
                axi -= factor * dx;
                ayi -= factor * dy;
                azi -= factor * dz;
            }
            particles_.acc_x[i] = axi;
            particles_.acc_y[i] = ayi;
            particles_.acc_z[i] = azi;
        }
    } else {
        // Global softening (original path)
        double eps2 = softening * softening;
        for (i32 i = 0; i < n; i++) {
            if (!particles_.is_active[i]) continue;
            double axi = 0.0, ayi = 0.0, azi = 0.0;
            double xi = particles_.pos_x[i], yi = particles_.pos_y[i], zi = particles_.pos_z[i];

            for (i32 j = 0; j < n; j++) {
                if (i == j || !particles_.is_active[j]) continue;
                double dx = xi - particles_.pos_x[j];
                double dy = yi - particles_.pos_y[j];
                double dz = zi - particles_.pos_z[j];
                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double inv_dist = 1.0 / std::sqrt(dist2);
                double inv_dist3 = inv_dist * inv_dist * inv_dist;
                double factor = particles_.mass[j] * inv_dist3;
                axi -= factor * dx;
                ayi -= factor * dy;
                azi -= factor * dz;
            }
            particles_.acc_x[i] = axi;
            particles_.acc_y[i] = ayi;
            particles_.acc_z[i] = azi;
        }
    }
}

void Engine::compute_cpu_barnes_hut_forces(double softening) {
    bh_solver_.compute_forces(particles_, softening);
}

// --------------------------------------------------------------------------
// Phase 13: CPU Yoshida sub-step helper
// --------------------------------------------------------------------------

void Engine::cpu_yoshida_substep(double sub_dt, double softening, bool use_barnes_hut) {
    i32 n = particles_.count;
    double half_dt = 0.5 * sub_dt;

    // Half-kick using old_acc
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.old_acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.old_acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.old_acc_z[i];
    }

    // Drift
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.pos_x[i] += sub_dt * particles_.vel_x[i];
        particles_.pos_y[i] += sub_dt * particles_.vel_y[i];
        particles_.pos_z[i] += sub_dt * particles_.vel_z[i];
    }

    // Compute forces
    // Phase 14-15: Use unified BH traversal with collision detection when applicable
    bool yoshida_bh_unified = (use_barnes_hut &&
        config_.collision_config.mode != physics::CollisionMode::Ignore &&
        config_.enable_collisions);

    if (yoshida_bh_unified) {
        collision_pairs_.clear();
        bh_solver_.compute_forces_with_collisions(particles_, softening, collision_pairs_);
    } else if (use_barnes_hut) {
        compute_cpu_barnes_hut_forces(softening);
    } else {
        compute_cpu_brute_forces(softening);
    }

    // PN corrections
    if (config_.enable_pn) {
        pn_correction_.apply_corrections(particles_);
    }

    // Collision detection & resolution (between force and second half-kick)
    if (yoshida_bh_unified) {
        // Pairs already detected during BH traversal
        if (!collision_pairs_.empty()) {
            collision_resolver_.resolve(particles_, collision_pairs_);
            compact_after_merges();
        }
    } else {
        detect_and_resolve_collisions();
    }

    // Second half-kick using new acc (re-read n in case compaction changed it)
    n = particles_.count;
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.acc_z[i];
    }

    // Rotate: old_acc = acc for next sub-step
    particles_.rotate_accelerations();
}

// --------------------------------------------------------------------------
// CPU Brute-Force O(n^2)
// --------------------------------------------------------------------------

void Engine::step_cpu_brute_force(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("CPU_BruteForce");
    i32 n = particles_.count;
    double half_dt = 0.5 * dt;

    // Phase 1: Half-kick
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.old_acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.old_acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.old_acc_z[i];
    }

    // Phase 2: Drift
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.pos_x[i] += dt * particles_.vel_x[i];
        particles_.pos_y[i] += dt * particles_.vel_y[i];
        particles_.pos_z[i] += dt * particles_.vel_z[i];
    }

    // Phase 13 post-position hooks
    apply_phase13_post_position(dt);

    // Phase 3: Compute forces (with softening mode support)
    compute_cpu_brute_forces(softening);

    // Optional: PN corrections
    if (config_.enable_pn) {
        pn_correction_.apply_corrections(particles_);
    }

    // Phase 13: Collision detection & resolution
    detect_and_resolve_collisions();

    // Phase 4: Second half-kick (re-read n in case compaction changed it)
    n = particles_.count;
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.acc_z[i];
    }

    // Phase 5: Rotate
    particles_.rotate_accelerations();
    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// CPU Barnes-Hut O(n log n)
// --------------------------------------------------------------------------

void Engine::step_cpu_barnes_hut(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("CPU_BarnesHut");
    i32 n = particles_.count;
    double half_dt = 0.5 * dt;

    // Phase 1: Half-kick
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.old_acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.old_acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.old_acc_z[i];
    }

    // Phase 2: Drift
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.pos_x[i] += dt * particles_.vel_x[i];
        particles_.pos_y[i] += dt * particles_.vel_y[i];
        particles_.pos_z[i] += dt * particles_.vel_z[i];
    }

    // Phase 13 post-position hooks
    apply_phase13_post_position(dt);

    // Phase 14-15: Unified BH traversal with collision detection
    if (config_.collision_config.mode != physics::CollisionMode::Ignore &&
        config_.enable_collisions) {
        collision_pairs_.clear();
        bh_solver_.compute_forces_with_collisions(particles_, softening, collision_pairs_);

        // Optional: PN corrections
        if (config_.enable_pn) {
            pn_correction_.apply_corrections(particles_);
        }

        // Resolve collisions and compact
        if (!collision_pairs_.empty()) {
            collision_resolver_.resolve(particles_, collision_pairs_);
            compact_after_merges();
        }
    } else {
        // Original path: forces only, no collision
        compute_cpu_barnes_hut_forces(softening);

        // Optional: PN corrections
        if (config_.enable_pn) {
            pn_correction_.apply_corrections(particles_);
        }
    }

    // Phase 4: Second half-kick (re-read n in case compaction changed it)
    n = particles_.count;
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.acc_z[i];
    }

    // Phase 5: Rotate
    particles_.rotate_accelerations();
    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// GPU Brute-Force (Tiled Kernel)
// --------------------------------------------------------------------------

void Engine::step_gpu_brute_force(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("GPU_BruteForce");
    if (!gpu_pipeline_.is_initialized()) {
        step_cpu_brute_force(dt, softening);
        CELESTIAL_NVTX_POP();
        return;
    }

    gpu_pipeline_.submit_step(particles_, softening, dt, config_.enable_pn);
    gpu_pipeline_.retrieve_results(particles_);

    // Phase 13: CPU-side collision detection & resolution after GPU step
    detect_and_resolve_collisions();

    if (deterministic_.force_sync()) {
#if CELESTIAL_HAS_CUDA
        cudaDeviceSynchronize();
#endif
    }
    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// GPU Barnes-Hut O(n log n) — Phase 12
// --------------------------------------------------------------------------

void Engine::step_gpu_barnes_hut(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("GPU_BarnesHut");

#if CELESTIAL_HAS_CUDA
    if (!gpu_pool_.is_initialized() || !gpu_tree_solver_.is_initialized()) {
        step_cpu_barnes_hut(dt, softening);
        CELESTIAL_NVTX_POP();
        return;
    }

    i32 n = particles_.count;
    auto& dc = cuda::DeviceContext::instance();
    cudaStream_t stream = dc.stream(0);

    // Reset scratch allocator for this frame (O(1), no device ops)
    gpu_pool_.scratch_reset();

    // Upload all host state to GPU pool
    {
        CELESTIAL_NVTX_PUSH("Upload");
        gpu_pool_.upload_all(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
            particles_.mass, particles_.radius, particles_.is_active,
            n, stream);
        CELESTIAL_NVTX_POP();
    }

    // Phase 1+2: Half-kick + Drift
    {
        CELESTIAL_NVTX_PUSH("KickDrift");
        cuda::launch_kick_drift(
            gpu_pool_.d_pos_x, gpu_pool_.d_pos_y, gpu_pool_.d_pos_z,
            gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
            gpu_pool_.d_old_acc_x, gpu_pool_.d_old_acc_y, gpu_pool_.d_old_acc_z,
            gpu_pool_.d_is_active, n, dt, stream);
        CELESTIAL_NVTX_POP();
    }

    // Phase 3: GPU Barnes-Hut force computation
    // Phase 14-15: Use unified traversal with collision detection when enabled
    bool gpu_bh_unified_collisions = (
        config_.collision_config.mode != physics::CollisionMode::Ignore &&
        config_.enable_collisions);

    // Phase 18+19: GPU-resident merge+compact when mode==Merge
    bool gpu_resident_merge = (gpu_bh_unified_collisions &&
        config_.collision_config.mode == physics::CollisionMode::Merge);

    i32 new_n = n;

    {
        CELESTIAL_NVTX_PUSH("BarnesHutForces");
        if (gpu_resident_merge) {
            // Phase 18+19: GPU-resident force + collision detect + merge + compact
            new_n = gpu_tree_solver_.compute_forces_merge_compact(
                gpu_pool_, n, softening,
                config_.collision_config,
                config_.density_config.min_radius,
                config_.collision_config.density_preserving_merge,
                stream);
        } else if (gpu_bh_unified_collisions) {
            collision_pairs_.clear();
            gpu_tree_solver_.compute_forces_with_collisions(
                gpu_pool_, n, softening, collision_pairs_, stream);
        } else {
            gpu_tree_solver_.compute_forces(gpu_pool_, n, softening, stream);
        }
        CELESTIAL_NVTX_POP();
    }

    // Phase 3b: Optional PN corrections (use new_n after potential GPU merge)
    if (config_.enable_pn) {
        CELESTIAL_NVTX_PUSH("PNCorrection");
        cuda::launch_pn_correction(
            gpu_pool_.d_pos_x, gpu_pool_.d_pos_y, gpu_pool_.d_pos_z,
            gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
            gpu_pool_.d_mass, gpu_pool_.d_is_active,
            gpu_pool_.d_acc_x, gpu_pool_.d_acc_y, gpu_pool_.d_acc_z,
            new_n, softening, 0.3, 3.0, stream);
        CELESTIAL_NVTX_POP();
    }

    // Phase 4+5: Second half-kick + Rotate (use new_n after potential GPU merge)
    {
        CELESTIAL_NVTX_PUSH("KickRotate");
        cuda::launch_kick_rotate(
            gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
            gpu_pool_.d_acc_x, gpu_pool_.d_acc_y, gpu_pool_.d_acc_z,
            gpu_pool_.d_old_acc_x, gpu_pool_.d_old_acc_y, gpu_pool_.d_old_acc_z,
            gpu_pool_.d_is_active, new_n, dt, stream);
        CELESTIAL_NVTX_POP();
    }

    // Download results
    {
        CELESTIAL_NVTX_PUSH("Download");
        gpu_pool_.download_state(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            new_n, stream);
        gpu_pool_.download_old_acc(
            particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
            new_n, stream);

        // Phase 18+19: Download mass, radius, is_active after GPU merge changed them
        if (gpu_resident_merge && new_n != n) {
            usize dbytes = sizeof(double) * static_cast<usize>(new_n);
            usize u8bytes = sizeof(u8) * static_cast<usize>(new_n);
            cudaMemcpyAsync(particles_.mass, gpu_pool_.d_mass,
                dbytes, cudaMemcpyDeviceToHost, stream);
            cudaMemcpyAsync(particles_.radius, gpu_pool_.d_radius,
                dbytes, cudaMemcpyDeviceToHost, stream);
            cudaMemcpyAsync(particles_.is_active, gpu_pool_.d_is_active,
                u8bytes, cudaMemcpyDeviceToHost, stream);
        }

        cudaStreamSynchronize(stream);

        // Phase 18+19: Update host particle count after GPU compaction
        if (gpu_resident_merge && new_n != n) {
            particles_.set_count(new_n);
        }

        CELESTIAL_NVTX_POP();
    }

    // Phase 14-15: Resolve non-merge GPU-detected collision pairs, or fallback to CPU detection
    // Phase 18+19: Merge mode is fully handled on GPU above
    if (!gpu_resident_merge) {
        if (gpu_bh_unified_collisions) {
            if (!collision_pairs_.empty()) {
                collision_resolver_.resolve(particles_, collision_pairs_);
                compact_after_merges();
            }
        } else {
            detect_and_resolve_collisions();
        }
    }

    if (deterministic_.force_sync()) {
        cudaDeviceSynchronize();
    }
#else
    step_cpu_barnes_hut(dt, softening);
#endif

    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// Phase 13: Yoshida 4th-order CPU Brute-Force
// --------------------------------------------------------------------------

void Engine::step_yoshida4_cpu_brute_force(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("Yoshida4_CPU_BruteForce");

    for (int s = 0; s < YoshidaCoefficients::NUM_STAGES; s++) {
        double sub_dt = YoshidaCoefficients::SUB_DT[s] * dt;
        cpu_yoshida_substep(sub_dt, softening, /*use_barnes_hut=*/false);
    }

    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// Phase 13: Yoshida 4th-order CPU Barnes-Hut
// --------------------------------------------------------------------------

void Engine::step_yoshida4_cpu_barnes_hut(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("Yoshida4_CPU_BarnesHut");

    for (int s = 0; s < YoshidaCoefficients::NUM_STAGES; s++) {
        double sub_dt = YoshidaCoefficients::SUB_DT[s] * dt;
        cpu_yoshida_substep(sub_dt, softening, /*use_barnes_hut=*/true);
    }

    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// Phase 13: Yoshida 4th-order GPU Brute-Force
// --------------------------------------------------------------------------

void Engine::step_yoshida4_gpu_brute_force(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("Yoshida4_GPU_BruteForce");

    // Fallback to CPU if GPU pipeline not available
    if (!gpu_pipeline_.is_initialized()) {
        step_yoshida4_cpu_brute_force(dt, softening);
        CELESTIAL_NVTX_POP();
        return;
    }

    // For GPU brute-force, each Yoshida sub-step is a full pipeline submit
    // because the tiled kernel does the entire KDK cycle internally.
    for (int s = 0; s < YoshidaCoefficients::NUM_STAGES; s++) {
        double sub_dt = YoshidaCoefficients::SUB_DT[s] * dt;
        gpu_pipeline_.submit_step(particles_, softening, sub_dt, config_.enable_pn);
        gpu_pipeline_.retrieve_results(particles_);
    }

    // Collision detection on final state
    detect_and_resolve_collisions();

    if (deterministic_.force_sync()) {
#if CELESTIAL_HAS_CUDA
        cudaDeviceSynchronize();
#endif
    }

    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// Phase 13: Yoshida 4th-order GPU Barnes-Hut
// --------------------------------------------------------------------------

void Engine::step_yoshida4_gpu_barnes_hut(double dt, double softening) {
    CELESTIAL_NVTX_PUSH("Yoshida4_GPU_BarnesHut");

#if CELESTIAL_HAS_CUDA
    if (!gpu_pool_.is_initialized() || !gpu_tree_solver_.is_initialized()) {
        step_yoshida4_cpu_barnes_hut(dt, softening);
        CELESTIAL_NVTX_POP();
        return;
    }

    i32 n = particles_.count;
    auto& dc = cuda::DeviceContext::instance();
    cudaStream_t stream = dc.stream(0);

    // Reset scratch allocator
    gpu_pool_.scratch_reset();

    // Upload once before all 3 sub-steps
    {
        CELESTIAL_NVTX_PUSH("Upload");
        gpu_pool_.upload_all(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
            particles_.mass, particles_.radius, particles_.is_active,
            n, stream);
        CELESTIAL_NVTX_POP();
    }

    // Phase 14-15: Determine if unified collision detection is needed
    bool yoshida_gpu_unified_collisions = (
        config_.collision_config.mode != physics::CollisionMode::Ignore &&
        config_.enable_collisions);

    // Phase 18+19: GPU-resident merge+compact when mode==Merge
    bool yoshida_gpu_resident_merge = (yoshida_gpu_unified_collisions &&
        config_.collision_config.mode == physics::CollisionMode::Merge);

    i32 new_n = n;

    // Execute 3 Yoshida sub-steps on device
    for (int s = 0; s < YoshidaCoefficients::NUM_STAGES; s++) {
        double sub_dt = YoshidaCoefficients::SUB_DT[s] * dt;

        // Half-kick + Drift
        cuda::launch_kick_drift(
            gpu_pool_.d_pos_x, gpu_pool_.d_pos_y, gpu_pool_.d_pos_z,
            gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
            gpu_pool_.d_old_acc_x, gpu_pool_.d_old_acc_y, gpu_pool_.d_old_acc_z,
            gpu_pool_.d_is_active, new_n, sub_dt, stream);

        // Force computation (must rebuild tree each sub-step since positions changed)
        // Phase 14-15: Use collision-aware traverse on last sub-step
        // Phase 18+19: Use GPU-resident merge+compact on last sub-step when mode==Merge
        gpu_pool_.scratch_reset();
        bool is_last_substep = (s == YoshidaCoefficients::NUM_STAGES - 1);
        if (yoshida_gpu_resident_merge && is_last_substep) {
            new_n = gpu_tree_solver_.compute_forces_merge_compact(
                gpu_pool_, new_n, softening,
                config_.collision_config,
                config_.density_config.min_radius,
                config_.collision_config.density_preserving_merge,
                stream);
        } else if (yoshida_gpu_unified_collisions && is_last_substep) {
            collision_pairs_.clear();
            gpu_tree_solver_.compute_forces_with_collisions(
                gpu_pool_, new_n, softening, collision_pairs_, stream);
        } else {
            gpu_tree_solver_.compute_forces(gpu_pool_, new_n, softening, stream);
        }

        // PN corrections
        if (config_.enable_pn) {
            cuda::launch_pn_correction(
                gpu_pool_.d_pos_x, gpu_pool_.d_pos_y, gpu_pool_.d_pos_z,
                gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
                gpu_pool_.d_mass, gpu_pool_.d_is_active,
                gpu_pool_.d_acc_x, gpu_pool_.d_acc_y, gpu_pool_.d_acc_z,
                new_n, softening, 0.3, 3.0, stream);
        }

        // Second half-kick + Rotate
        cuda::launch_kick_rotate(
            gpu_pool_.d_vel_x, gpu_pool_.d_vel_y, gpu_pool_.d_vel_z,
            gpu_pool_.d_acc_x, gpu_pool_.d_acc_y, gpu_pool_.d_acc_z,
            gpu_pool_.d_old_acc_x, gpu_pool_.d_old_acc_y, gpu_pool_.d_old_acc_z,
            gpu_pool_.d_is_active, new_n, sub_dt, stream);
    }

    // Download once after all sub-steps
    {
        CELESTIAL_NVTX_PUSH("Download");
        gpu_pool_.download_state(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            new_n, stream);
        gpu_pool_.download_old_acc(
            particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
            new_n, stream);

        // Phase 18+19: Download mass, radius, is_active after GPU merge changed them
        if (yoshida_gpu_resident_merge && new_n != n) {
            usize dbytes = sizeof(double) * static_cast<usize>(new_n);
            usize u8bytes = sizeof(u8) * static_cast<usize>(new_n);
            cudaMemcpyAsync(particles_.mass, gpu_pool_.d_mass,
                dbytes, cudaMemcpyDeviceToHost, stream);
            cudaMemcpyAsync(particles_.radius, gpu_pool_.d_radius,
                dbytes, cudaMemcpyDeviceToHost, stream);
            cudaMemcpyAsync(particles_.is_active, gpu_pool_.d_is_active,
                u8bytes, cudaMemcpyDeviceToHost, stream);
        }

        cudaStreamSynchronize(stream);

        // Phase 18+19: Update host particle count after GPU compaction
        if (yoshida_gpu_resident_merge && new_n != n) {
            particles_.set_count(new_n);
        }

        CELESTIAL_NVTX_POP();
    }

    // Phase 14-15: Resolve non-merge GPU-detected collision pairs, or fallback to CPU detection
    // Phase 18+19: Merge mode is fully handled on GPU above
    if (!yoshida_gpu_resident_merge) {
        if (yoshida_gpu_unified_collisions) {
            if (!collision_pairs_.empty()) {
                collision_resolver_.resolve(particles_, collision_pairs_);
                compact_after_merges();
            }
        } else {
            detect_and_resolve_collisions();
        }
    }

    if (deterministic_.force_sync()) {
        cudaDeviceSynchronize();
    }
#else
    step_yoshida4_cpu_barnes_hut(dt, softening);
#endif

    CELESTIAL_NVTX_POP();
}

// --------------------------------------------------------------------------
// Phase 13 hooks
// --------------------------------------------------------------------------

void Engine::apply_phase13_pre_force() {
    if (!phase13_hooks_) return;
    // Hook points for relativistic mass and precision zones
    // (no-op until Phase 13 implementations are registered)
}

void Engine::apply_phase13_post_force(double dt) {
    if (!phase13_hooks_) return;
    i32 n = particles_.count;

    if (phase13_hooks_->accretion && phase13_hooks_->accretion->is_enabled()) {
        phase13_hooks_->accretion->apply(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.mass,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            particles_.is_active, n, dt);
    }

    if (phase13_hooks_->radiation_pressure && phase13_hooks_->radiation_pressure->is_enabled()) {
        phase13_hooks_->radiation_pressure->apply(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.mass, particles_.radius,
            particles_.acc_x, particles_.acc_y, particles_.acc_z,
            particles_.is_active, n);
    }
}

void Engine::apply_phase13_post_position(double dt) {
    if (!phase13_hooks_) return;
    i32 n = particles_.count;

    if (phase13_hooks_->event_horizon && phase13_hooks_->event_horizon->is_enabled()) {
        phase13_hooks_->event_horizon->detect_crossings(
            particles_.pos_x, particles_.pos_y, particles_.pos_z,
            particles_.vel_x, particles_.vel_y, particles_.vel_z,
            particles_.mass, particles_.is_active, n, dt);
    }
}

// --------------------------------------------------------------------------
// Benchmark recording
// --------------------------------------------------------------------------

void Engine::record_benchmark(const profile::FrameProfile& fp, double sort_ms,
                               double build_ms, double traverse_ms) {
    profile::BenchmarkMetrics metrics{};
    metrics.kernel_time_ms = fp.gravity_ms;
    metrics.tree_build_ms = build_ms;
    metrics.tree_traverse_ms = traverse_ms;
    metrics.sort_ms = sort_ms;
    metrics.integration_ms = fp.integration_ms;
    metrics.transfer_ms = fp.transfer_ms;
    metrics.total_frame_ms = fp.total_frame_ms;
    metrics.energy_drift = energy_tracker_.energy_drift();
    metrics.momentum_drift = energy_tracker_.momentum_drift();
    metrics.accumulated_error = energy_tracker_.accumulated_error();
    metrics.body_count = particles_.count;
    metrics.step_number = total_steps_;
    metrics.shared_memory_used = 256 * 4 * static_cast<i64>(sizeof(double));

    i32 active = 0;
    for (i32 i = 0; i < particles_.count; i++) {
        if (particles_.is_active[i]) active++;
    }
    metrics.active_body_count = active;

    benchmark_.record(metrics);
}

// --------------------------------------------------------------------------
// Phase 18+19: GPU-resident energy snapshot
// --------------------------------------------------------------------------

void Engine::compute_gpu_energy_snapshot() {
#if CELESTIAL_HAS_CUDA
    if (config_.compute_mode != SimulationConfig::ComputeMode::GPU_BarnesHut) return;
    if (!gpu_pool_.is_initialized() || !gpu_tree_solver_.is_initialized()) return;

    i32 n = particles_.count;
    if (n <= 0) return;

    auto& dc = cuda::DeviceContext::instance();
    cudaStream_t stream = dc.stream(0);

    // Ensure latest state is on device
    gpu_pool_.scratch_reset();
    gpu_pool_.upload_all(
        particles_.pos_x, particles_.pos_y, particles_.pos_z,
        particles_.vel_x, particles_.vel_y, particles_.vel_z,
        particles_.acc_x, particles_.acc_y, particles_.acc_z,
        particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
        particles_.mass, particles_.radius, particles_.is_active,
        n, stream);

    // Build tree for PE computation (need BH tree)
    gpu_tree_solver_.compute_forces(gpu_pool_, n, config_.softening, stream);

    // Compute GPU energy snapshot
    gpu_pool_.scratch_reset();
    auto gpu_snap = gpu_tree_solver_.compute_gpu_energy(
        gpu_pool_, n, config_.softening, stream);

    // Record into the energy tracker as a regular snapshot
    profile::EnergyTracker::Snapshot snap{};
    snap.kinetic_energy = gpu_snap.kinetic_energy;
    snap.potential_energy = gpu_snap.potential_energy;
    snap.total_energy = gpu_snap.total_energy;
    snap.momentum_x = gpu_snap.momentum_x;
    snap.momentum_y = gpu_snap.momentum_y;
    snap.momentum_z = gpu_snap.momentum_z;
    snap.angular_momentum_x = gpu_snap.angular_momentum_x;
    snap.angular_momentum_y = gpu_snap.angular_momentum_y;
    snap.angular_momentum_z = gpu_snap.angular_momentum_z;
    snap.com_x = gpu_snap.com_x;
    snap.com_y = gpu_snap.com_y;
    snap.com_z = gpu_snap.com_z;
    snap.total_mass = gpu_snap.total_mass;
    snap.step_number = total_steps_;
    energy_tracker_.record(snap);
#endif
}

// --------------------------------------------------------------------------
// Phase 18+19: CPU vs GPU validation
// --------------------------------------------------------------------------

Engine::GpuValidationResult Engine::validate_gpu_cpu_parity() {
    GpuValidationResult result{};

#if CELESTIAL_HAS_CUDA
    if (config_.compute_mode != SimulationConfig::ComputeMode::GPU_BarnesHut) {
        last_gpu_validation_ = result;
        return result;
    }
    if (!gpu_pool_.is_initialized() || !gpu_tree_solver_.is_initialized()) {
        last_gpu_validation_ = result;
        return result;
    }

    i32 n = particles_.count;
    if (n <= 0) {
        last_gpu_validation_ = result;
        return result;
    }

    // CPU reference computation
    auto cpu_snap = energy_tracker_.compute(
        particles_.pos_x, particles_.pos_y, particles_.pos_z,
        particles_.vel_x, particles_.vel_y, particles_.vel_z,
        particles_.mass, particles_.is_active,
        n, config_.softening);

    result.cpu_ke = cpu_snap.kinetic_energy;
    result.cpu_pe = cpu_snap.potential_energy;
    result.cpu_total_mass = cpu_snap.total_mass;

    // GPU computation
    auto& dc = cuda::DeviceContext::instance();
    cudaStream_t stream = dc.stream(0);

    gpu_pool_.scratch_reset();
    gpu_pool_.upload_all(
        particles_.pos_x, particles_.pos_y, particles_.pos_z,
        particles_.vel_x, particles_.vel_y, particles_.vel_z,
        particles_.acc_x, particles_.acc_y, particles_.acc_z,
        particles_.old_acc_x, particles_.old_acc_y, particles_.old_acc_z,
        particles_.mass, particles_.radius, particles_.is_active,
        n, stream);

    // Build tree (needed for PE)
    gpu_tree_solver_.compute_forces(gpu_pool_, n, config_.softening, stream);

    gpu_pool_.scratch_reset();
    auto gpu_snap = gpu_tree_solver_.compute_gpu_energy(
        gpu_pool_, n, config_.softening, stream);

    result.gpu_ke = gpu_snap.kinetic_energy;
    result.gpu_pe = gpu_snap.potential_energy;
    result.gpu_total_mass = gpu_snap.total_mass;

    // Compute relative errors
    double ke_ref = std::abs(result.cpu_ke) > 1e-15 ? std::abs(result.cpu_ke) : 1e-15;
    double pe_ref = std::abs(result.cpu_pe) > 1e-15 ? std::abs(result.cpu_pe) : 1e-15;
    double mass_ref = std::abs(result.cpu_total_mass) > 1e-15 ? std::abs(result.cpu_total_mass) : 1e-15;

    result.ke_relative_error = std::abs(result.cpu_ke - result.gpu_ke) / ke_ref;
    result.pe_relative_error = std::abs(result.cpu_pe - result.gpu_pe) / pe_ref;
    result.mass_error = std::abs(result.cpu_total_mass - result.gpu_total_mass) / mass_ref;

    double tol = config_.gpu_validation_tolerance;
    result.passed = (result.ke_relative_error < tol) &&
                    (result.pe_relative_error < tol) &&
                    (result.mass_error < 1e-12);  // Mass must be near-exact

    last_gpu_validation_ = result;
#endif

    return result;
}

} // namespace celestial::sim
