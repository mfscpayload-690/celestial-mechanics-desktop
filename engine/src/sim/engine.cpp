#include <celestial/sim/engine.hpp>
#include <celestial/cuda/device_context.hpp>
#include <celestial/job/job_system.hpp>
#include <cstring>
#include <cmath>
#include <chrono>

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

    // Initialize GPU pipeline if CUDA available and GPU mode selected
    if (dc.has_cuda() && (config_.compute_mode == SimulationConfig::ComputeMode::GPU_BruteForce ||
                          config_.compute_mode == SimulationConfig::ComputeMode::GPU_BarnesHut)) {
        gpu_pipeline_.init(config_.max_particles);
    }

    // Configure timestep
    timestep_.fixed_dt = config_.dt;
    timestep_.max_steps_per_frame = config_.max_steps_per_frame;

    // Configure Barnes-Hut
    bh_solver_.theta = config_.theta;
    bh_solver_.use_parallel = true;

    initialized_ = true;
}

void Engine::shutdown() {
    if (!initialized_) return;

    if (gpu_pipeline_.is_initialized()) {
        gpu_pipeline_.destroy();
    }

    particles_.free();
    initialized_ = false;
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
    // Initialize old_acc = acc for first Verlet step
    std::memcpy(particles_.old_acc_x, ax, dsize);
    std::memcpy(particles_.old_acc_y, ay, dsize);
    std::memcpy(particles_.old_acc_z, az, dsize);
    std::memcpy(particles_.mass, mass, dsize);
    std::memcpy(particles_.radius, radius, dsize);
    std::memcpy(particles_.is_active, is_active, u8size);
}

void Engine::step(double dt, double softening) {
    auto frame_start = std::chrono::high_resolution_clock::now();
    profile::FrameProfile profile{};

    switch (config_.compute_mode) {
        case SimulationConfig::ComputeMode::CPU_BruteForce:
            step_cpu_brute_force(dt, softening);
            break;
        case SimulationConfig::ComputeMode::CPU_BarnesHut:
            step_cpu_barnes_hut(dt, softening);
            profile.tree_build_ms = bh_solver_.last_build_time_ms;
            profile.gravity_ms = bh_solver_.last_traversal_time_ms;
            break;
        case SimulationConfig::ComputeMode::GPU_BruteForce:
        case SimulationConfig::ComputeMode::GPU_BarnesHut:
            step_gpu_brute_force(dt, softening);
            break;
    }

    auto frame_end = std::chrono::high_resolution_clock::now();
    profile.total_frame_ms = std::chrono::duration<double, std::milli>(frame_end - frame_start).count();
    profile.total_cpu_ms = profile.total_frame_ms;
    profiler_.end_frame(profile);
}

int Engine::update(double frame_time) {
    int steps = timestep_.update(frame_time);
    for (int i = 0; i < steps; i++) {
        step(config_.dt, config_.softening);
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

    // Initialize GPU pipeline if switching to GPU mode
    if ((mode == SimulationConfig::ComputeMode::GPU_BruteForce ||
         mode == SimulationConfig::ComputeMode::GPU_BarnesHut) &&
        !gpu_pipeline_.is_initialized()) {
        auto& dc = cuda::DeviceContext::instance();
        if (dc.has_cuda()) {
            gpu_pipeline_.init(config_.max_particles);
        }
    }
}

void Engine::set_theta(double theta) {
    config_.theta = theta;
    bh_solver_.theta = theta;
}

void Engine::set_enable_pn(bool enable) {
    config_.enable_pn = enable;
}

// --------------------------------------------------------------------------
// CPU Brute-Force O(n^2)
// --------------------------------------------------------------------------

void Engine::step_cpu_brute_force(double dt, double softening) {
    i32 n = particles_.count;
    double eps2 = softening * softening;
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

    // Phase 3: Compute forces (brute-force O(n^2))
    particles_.zero_accelerations();
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        double axi = 0.0, ayi = 0.0, azi = 0.0;
        double xi = particles_.pos_x[i];
        double yi = particles_.pos_y[i];
        double zi = particles_.pos_z[i];

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

    // Optional: PN corrections
    if (config_.enable_pn) {
        pn_correction_.apply_corrections(particles_);
    }

    // Phase 4: Second half-kick
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.acc_z[i];
    }

    // Phase 5: Rotate
    particles_.rotate_accelerations();
}

// --------------------------------------------------------------------------
// CPU Barnes-Hut O(n log n)
// --------------------------------------------------------------------------

void Engine::step_cpu_barnes_hut(double dt, double softening) {
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

    // Phase 3: Barnes-Hut force computation
    bh_solver_.compute_forces(particles_, softening);

    // Optional: PN corrections
    if (config_.enable_pn) {
        pn_correction_.apply_corrections(particles_);
    }

    // Phase 4: Second half-kick
    for (i32 i = 0; i < n; i++) {
        if (!particles_.is_active[i]) continue;
        particles_.vel_x[i] += half_dt * particles_.acc_x[i];
        particles_.vel_y[i] += half_dt * particles_.acc_y[i];
        particles_.vel_z[i] += half_dt * particles_.acc_z[i];
    }

    // Phase 5: Rotate
    particles_.rotate_accelerations();
}

// --------------------------------------------------------------------------
// GPU Brute-Force (Tiled Kernel)
// --------------------------------------------------------------------------

void Engine::step_gpu_brute_force(double dt, double softening) {
    if (!gpu_pipeline_.is_initialized()) {
        // Fallback to CPU
        step_cpu_brute_force(dt, softening);
        return;
    }

    // Submit full Verlet step to GPU
    gpu_pipeline_.submit_step(particles_, softening, dt, config_.enable_pn);

    // Synchronize to get results
    gpu_pipeline_.retrieve_results(particles_);

    // For synchronous mode, wait for current buffer too
    // (Double-buffering benefit applies when calling update() each frame)
}

} // namespace celestial::sim
