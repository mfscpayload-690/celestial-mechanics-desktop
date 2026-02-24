# C API Reference

## Overview

The Celestial Engine exposes a pure C API through `native_api.h` for consumption via
P/Invoke from .NET (C#). All functions use `extern "C"` linkage with `CELESTIAL_API`
(`__declspec(dllexport)` on Windows, `__attribute__((visibility("default")))` on Linux).

**Header**: `engine/include/celestial/interop/native_api.h`
**Implementation**: `engine/src/interop/native_api.cpp`

**Conventions**:
- Boolean parameters use `int32_t` (0 = false, nonzero = true)
- Boolean returns use `int32_t` (0 = false, 1 = true)
- Error codes are negative `int32_t` values
- All pointer parameters are caller-owned (engine does not take ownership)
- All functions are safe to call with `g_engine == nullptr` (no-op or return 0/0.0)

---

## Lifecycle

### `celestial_init`
```c
int32_t celestial_init(int32_t max_particles);
```
Initialize the engine. Allocates particle arrays for `max_particles` capacity.
Creates singleton Engine instance. Initializes CUDA device, job system, GPU pool.

**Returns**: 0 on success, negative error code on failure.
- `-1` (AlreadyInitialized): Engine already initialized
- `-100`: Unknown exception

### `celestial_shutdown`
```c
void celestial_shutdown(void);
```
Shutdown engine and release all resources. Safe to call when not initialized.
After shutdown, `celestial_init` can be called again.

---

## Data Transfer

### `celestial_set_particles`
```c
void celestial_set_particles(
    const double* pos_x, const double* pos_y, const double* pos_z,
    const double* vel_x, const double* vel_y, const double* vel_z,
    const double* acc_x, const double* acc_y, const double* acc_z,
    const double* mass, const double* radius,
    const uint8_t* is_active, int32_t count);
```
Upload particle data from host arrays. `count` must be <= `max_particles`.
Copies into engine's internal SoA arrays. Resets energy tracking.

### `celestial_get_positions`
```c
void celestial_get_positions(double* out_pos_x, double* out_pos_y, double* out_pos_z, int32_t count);
```
Download current positions to caller-owned arrays. Copies `min(count, particle_count)` elements.

### `celestial_get_velocities`
```c
void celestial_get_velocities(double* out_vel_x, double* out_vel_y, double* out_vel_z, int32_t count);
```
Download current velocities.

### `celestial_get_accelerations`
```c
void celestial_get_accelerations(double* out_acc_x, double* out_acc_y, double* out_acc_z, int32_t count);
```
Download current accelerations.

---

## Physics Step

### `celestial_step`
```c
void celestial_step(double dt, double softening);
```
Execute one full physics step. Dispatches to the appropriate mode based on current
integrator (Leapfrog/Yoshida4) and compute mode (CPU_BF/CPU_BH/GPU_BF/GPU_BH).
See [PIPELINE.md](PIPELINE.md) for execution details.

### `celestial_update`
```c
int32_t celestial_update(double frame_time);
```
Accumulator-based update. Feeds `frame_time` (seconds) into the fixed-timestep
accumulator, executing 0-10 substeps. Returns number of substeps taken.

### `celestial_compute_forces`
```c
void celestial_compute_forces(double softening);
```
Compute forces only (calls `step(0.0, softening)` — zero dt means no integration).

---

## Configuration

### `celestial_set_compute_mode`
```c
void celestial_set_compute_mode(int32_t mode);
```
Set compute mode. Values: 0=CPU_BruteForce, 1=CPU_BarnesHut, 2=GPU_BruteForce, 3=GPU_BarnesHut.
Lazily initializes GPU resources when switching to a GPU mode.

### `celestial_set_theta`
```c
void celestial_set_theta(double theta);
```
Set Barnes-Hut opening angle. Range: 0.0 (exact/direct sum) to ~1.0 (aggressive approximation).
Typical: 0.5. Floor on CPU: 0.2.

### `celestial_set_dt`
```c
void celestial_set_dt(double dt);
```
Set fixed timestep for accumulator-based update.

### `celestial_set_softening`
```c
void celestial_set_softening(double softening);
```
Set global softening parameter epsilon.

### `celestial_set_enable_pn`
```c
void celestial_set_enable_pn(int32_t enable);
```
Enable/disable post-Newtonian 1PN corrections.

### `celestial_set_gpu_pool_size`
```c
void celestial_set_gpu_pool_size(uint64_t bytes);
```
Set GPU memory pool size in bytes. Must be called before `celestial_init`. Default: 256 MB.

---

## Deterministic Mode

### `celestial_set_deterministic`
```c
void celestial_set_deterministic(int32_t enable);
```
Enable/disable deterministic mode. Forces fixed dispatch order, GPU sync, and seeded PRNG.

### `celestial_is_deterministic`
```c
int32_t celestial_is_deterministic(void);
```
Query deterministic mode state. Returns 1 if enabled.

### `celestial_set_deterministic_seed`
```c
void celestial_set_deterministic_seed(uint64_t seed);
```
Set RNG seed for deterministic mode. Same seed + same initial conditions = identical results
(in bit-exact modes: all except GPU_BarnesHut).

---

## Energy Tracking

### `celestial_compute_energy_snapshot`
```c
void celestial_compute_energy_snapshot(void);
```
Compute and record an energy snapshot. Computes KE, PE (O(N^2) or O(N log N) via BH),
momentum, angular momentum, COM, virial ratio. Records into rolling history.

### `celestial_get_energy_drift`
```c
double celestial_get_energy_drift(void);
```
Get relative energy drift: `|E_now - E_initial| / |E_initial|`. Returns 0 if no snapshots.

### `celestial_get_momentum_drift`
```c
double celestial_get_momentum_drift(void);
```
Get absolute momentum drift magnitude since first snapshot.

### `celestial_get_accumulated_error`
```c
double celestial_get_accumulated_error(void);
```
Get accumulated integration error (sum of per-step drifts).

### `celestial_get_angular_momentum_drift`
```c
double celestial_get_angular_momentum_drift(void);
```
Get angular momentum drift magnitude since first snapshot.

### `celestial_get_com_position_drift`
```c
double celestial_get_com_position_drift(void);
```
Get center-of-mass position drift since first snapshot.

### `celestial_get_com_velocity_drift`
```c
double celestial_get_com_velocity_drift(void);
```
Get center-of-mass velocity drift since first snapshot.

### `celestial_get_virial_ratio`
```c
double celestial_get_virial_ratio(void);
```
Get virial ratio (2*KE/|PE|) from last snapshot. ~1.0 for virialized system.

### `celestial_check_conservation_diagnostics`
```c
int32_t celestial_check_conservation_diagnostics(void);
```
Check all conservation diagnostics against default thresholds. Returns 1 if all pass.
Thresholds: energy < 0.1%, momentum < 1e-8, angular momentum < 1e-8, COM velocity < 1e-10.

---

## Profiling

### `celestial_get_last_gpu_time_ms`
```c
double celestial_get_last_gpu_time_ms(void);
```
Last frame's GPU computation time in milliseconds.

### `celestial_get_last_cpu_time_ms`
```c
double celestial_get_last_cpu_time_ms(void);
```
Last frame's CPU computation time in milliseconds.

### `celestial_get_device_memory_bytes`
```c
int64_t celestial_get_device_memory_bytes(void);
```
Total GPU device memory currently allocated (bytes).

### `celestial_get_particle_count`
```c
int32_t celestial_get_particle_count(void);
```
Current particle count (may decrease after merges + compaction).

---

## Benchmark

### `celestial_get_avg_frame_time_ms`
```c
double celestial_get_avg_frame_time_ms(void);
```
Average frame time over rolling window (ms).

### `celestial_get_estimated_fps`
```c
double celestial_get_estimated_fps(void);
```
Estimated FPS based on rolling average.

### `celestial_meets_performance_target`
```c
int32_t celestial_meets_performance_target(void);
```
Returns 1 if current performance meets target for the body count.

### `celestial_get_last_tree_build_ms`
```c
double celestial_get_last_tree_build_ms(void);
```
Last frame's tree build time (Barnes-Hut modes only, ms).

### `celestial_get_last_tree_traverse_ms`
```c
double celestial_get_last_tree_traverse_ms(void);
```
Last frame's tree traversal time (Barnes-Hut modes only, ms).

---

## Integrator Selection (Phase 13)

### `celestial_set_integrator`
```c
void celestial_set_integrator(int32_t type);
```
Values: 0=Leapfrog_KDK (default), 1=Yoshida4.

### `celestial_get_integrator`
```c
int32_t celestial_get_integrator(void);
```

---

## Adaptive Timestep (Phase 13)

### `celestial_set_adaptive_timestep`
```c
void celestial_set_adaptive_timestep(
    int32_t enabled, double eta, double dt_min, double dt_max, double initial_dt);
```
Configure adaptive timestep. `eta`: safety factor (default 0.01),
`dt_min`/`dt_max`: bounds, `initial_dt`: starting dt.

### `celestial_get_adaptive_dt`
```c
double celestial_get_adaptive_dt(void);
```
Current adaptive dt value.

### `celestial_is_adaptive_dt_enabled`
```c
int32_t celestial_is_adaptive_dt_enabled(void);
```

---

## Collision Handling (Phase 13)

### `celestial_set_collision_mode`
```c
void celestial_set_collision_mode(int32_t mode);
```
Values: 0=Ignore, 1=Elastic, 2=Inelastic, 3=Merge.

### `celestial_set_collision_restitution`
```c
void celestial_set_collision_restitution(double e);
```
Coefficient of restitution for inelastic collisions. Range: 0.0-1.0.

### `celestial_get_collision_count`
```c
int32_t celestial_get_collision_count(void);
```
Number of collisions detected in last step.

---

## Softening Configuration (Phase 13)

### `celestial_set_softening_mode`
```c
void celestial_set_softening_mode(int32_t mode);
```
Values: 0=Global, 1=PerBodyType, 2=Adaptive.

### `celestial_set_type_softening`
```c
void celestial_set_type_softening(int32_t type_index, double eps);
```
Set per-body-type softening. `type_index` in [0, 15].

### `celestial_set_adaptive_softening_scale`
```c
void celestial_set_adaptive_softening_scale(double scale);
```
Scale factor for adaptive softening: `eps_i = scale * cbrt(m_i)`.

---

## Density & Compaction (Phase 14-15)

### `celestial_set_density_config`
```c
void celestial_set_density_config(double default_density, double min_radius);
```
Configure density model. `default_density`: kg/m^3 (default 5515.0), `min_radius`: floor (default 1e-6).

### `celestial_set_max_merges_per_frame`
```c
void celestial_set_max_merges_per_frame(int32_t max_merges);
```
Merge safeguard: cap total merges per step. Default: 64.

### `celestial_set_max_merges_per_body`
```c
void celestial_set_max_merges_per_body(int32_t max_merges);
```
Merge safeguard: cap merges per body per step. Default: 2.

### `celestial_set_density_preserving_merge`
```c
void celestial_set_density_preserving_merge(int32_t enabled);
```
Use density model for survivor radius after merge (vs volume-conserving).

### `celestial_get_active_particle_count`
```c
int32_t celestial_get_active_particle_count(void);
```
Count of active (non-merged) particles.

### `celestial_get_last_merge_count`
```c
int32_t celestial_get_last_merge_count(void);
```
Number of merges performed in last step.

### `celestial_compact_particles`
```c
int32_t celestial_compact_particles(void);
```
Manually trigger particle compaction. Returns new particle count.

---

## Rolling Averages & Diagnostics (Phase 16-17)

### `celestial_get_rolling_avg_energy`
```c
double celestial_get_rolling_avg_energy(void);
```
Rolling average of total energy over last 300 snapshots.

### `celestial_get_rolling_avg_drift`
```c
double celestial_get_rolling_avg_drift(void);
```
Rolling average of energy drift over last 300 snapshots.

### `celestial_set_enable_diagnostics`
```c
void celestial_set_enable_diagnostics(int32_t enabled);
```
Enable/disable per-step auto-compute of energy diagnostics.

---

## GPU Validation (Phase 18-19)

### `celestial_set_enable_gpu_validation`
```c
void celestial_set_enable_gpu_validation(int32_t enabled);
```
Enable/disable CPU vs GPU parity checking each step. Only meaningful in GPU_BarnesHut mode.

### `celestial_set_gpu_validation_tolerance`
```c
void celestial_set_gpu_validation_tolerance(double tolerance);
```
Set relative error tolerance for GPU vs CPU comparison. Default: 1e-6.

### `celestial_validate_gpu_cpu_parity`
```c
int32_t celestial_validate_gpu_cpu_parity(void);
```
Run CPU vs GPU validation. Returns 1 if passed, 0 if failed.

### `celestial_get_gpu_validation_ke_error`
```c
double celestial_get_gpu_validation_ke_error(void);
```
Last GPU validation KE relative error.

### `celestial_get_gpu_validation_pe_error`
```c
double celestial_get_gpu_validation_pe_error(void);
```
Last GPU validation PE relative error.

### `celestial_get_gpu_validation_mass_error`
```c
double celestial_get_gpu_validation_mass_error(void);
```
Last GPU validation mass error.

### `celestial_compute_gpu_energy_snapshot`
```c
void celestial_compute_gpu_energy_snapshot(void);
```
Compute energy snapshot entirely on GPU (no CPU round-trip for energy).
Only works in GPU_BarnesHut mode. Falls back to CPU snapshot otherwise.

---

## Enum Summary

| Enum | Values |
|------|--------|
| ComputeMode | 0=CPU_BruteForce, 1=CPU_BarnesHut, 2=GPU_BruteForce, 3=GPU_BarnesHut |
| IntegratorType | 0=Leapfrog_KDK, 1=Yoshida4 |
| CollisionMode | 0=Ignore, 1=Elastic, 2=Inelastic, 3=Merge |
| SofteningMode | 0=Global, 1=PerBodyType, 2=Adaptive |

## Function Count

**Total**: 56 functions across 12 categories.
