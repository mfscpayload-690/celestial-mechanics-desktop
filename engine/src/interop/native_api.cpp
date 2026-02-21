#include <celestial/interop/native_api.h>
#include <celestial/sim/engine.hpp>
#include <celestial/profile/memory_tracker.hpp>

// Static engine instance
static celestial::sim::Engine* g_engine = nullptr;
static celestial::sim::SimulationConfig g_config{};

extern "C" {

int32_t celestial_init(int32_t max_particles) {
    try {
        if (g_engine) {
            return static_cast<int32_t>(celestial::core::ErrorCode::AlreadyInitialized);
        }
        g_engine = new celestial::sim::Engine();
        g_config.max_particles = max_particles;
        g_engine->init(g_config);
        return 0;
    } catch (const celestial::core::CelestialException& e) {
        return static_cast<int32_t>(e.code());
    } catch (...) {
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
        // For force-only computation, we just step without integration
        // This matches the C# CudaPhysicsBackend.ComputeForces() pattern
        g_engine->step(0.0, softening); // dt=0 means no integration
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

} // extern "C"
