#ifndef CELESTIAL_NATIVE_API_H
#define CELESTIAL_NATIVE_API_H

#include <stdint.h>

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

/* ── Profiling ──────────────────────────────────────────────────────────── */

/// Get last frame's GPU computation time in milliseconds.
CELESTIAL_API double celestial_get_last_gpu_time_ms(void);

/// Get last frame's CPU computation time in milliseconds.
CELESTIAL_API double celestial_get_last_cpu_time_ms(void);

/// Get total device memory allocated in bytes.
CELESTIAL_API int64_t celestial_get_device_memory_bytes(void);

/// Get current particle count.
CELESTIAL_API int32_t celestial_get_particle_count(void);

#ifdef __cplusplus
}
#endif

#endif /* CELESTIAL_NATIVE_API_H */
