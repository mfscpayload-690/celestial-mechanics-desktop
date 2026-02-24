# Celestial Engine Architecture

## Overview

The Celestial Engine is a native C++20/CUDA gravitational N-body simulation engine designed
for real-time desktop applications. It provides a pure C ABI through P/Invoke for consumption
by a .NET (WPF/Avalonia) frontend.

```
┌──────────────────────────────────────────────────────────────┐
│                     C# / .NET Desktop UI                     │
│                   (WPF/Avalonia front-end)                   │
├──────────────────────────────────────────────────────────────┤
│                    C Interop Layer (P/Invoke)                │
│               engine/src/interop/native_api.cpp              │
│               engine/include/.../native_api.h                │
├──────────────────────────────────────────────────────────────┤
│                    Engine Facade (C++)                        │
│                 engine/src/sim/engine.cpp                     │
│ ┌──────────┐ ┌───────────┐ ┌───────────┐ ┌───────────────┐ │
│ │ Physics  │ │  Profile   │ │    Sim    │ │     CUDA      │ │
│ │ Systems  │ │  Systems   │ │ Subsystems│ │   Pipeline    │ │
│ ├──────────┤ ├───────────┤ ├───────────┤ ├───────────────┤ │
│ │Particles │ │Energy     │ │Timestep   │ │Gravity Kernel │ │
│ │Octree/BH │ │Benchmark  │ │Determinism│ │Morton Sort    │ │
│ │Collision │ │Frame Prof │ │Yoshida    │ │GPU Tree       │ │
│ │PN Corr   │ │Memory     │ │Adaptive DT│ │Integration    │ │
│ │Density   │ │NSight     │ │Pipeline   │ │Collision      │ │
│ └──────────┘ └───────────┘ └───────────┘ └───────────────┘ │
├──────────────────────────────────────────────────────────────┤
│                     Core (types, math, jobs)                 │
└──────────────────────────────────────────────────────────────┘
```

---

## Dependency Graph

The include dependency graph is **acyclic** with a strict layered architecture.
No header in a lower layer includes a header from a higher layer.

```
Layer 0 (Foundation):
    core/types.hpp
    core/platform.hpp
    core/constants.hpp
    core/error.hpp
        │
        ▼
Layer 1 (Math & Jobs):
    math/vec3d.hpp
    math/mat4d.hpp
    math/quaterniond.hpp
    job/job.hpp
    job/job_queue.hpp
    job/job_system.hpp
    job/job_types.hpp
        │
        ▼
Layer 2 (Physics & CUDA):
    physics/particle_system.hpp  ← core/types
    physics/octree_node.hpp      ← core/types, math/vec3d
    physics/octree_pool.hpp      ← physics/octree_node
    physics/octree_builder.hpp   ← physics/octree_pool
    physics/barnes_hut_solver.hpp← physics/octree_builder, physics/particle_system
    physics/collision_detector.hpp← physics/particle_system
    physics/collision_resolver.hpp← physics/particle_system, physics/density_model
    physics/density_model.hpp    ← core/types
    physics/post_newtonian.hpp   ← physics/particle_system
    cuda/device_context.hpp      ← core/platform
    cuda/cuda_check.hpp          ← core/platform
    cuda/device_particles.hpp    ← core/types
    cuda/kernel_config.hpp       ← core/types
    cuda/pinned_buffer.hpp       ← core/types
    cuda/morton.hpp              ← core/types
    cuda/gpu_tree.hpp            ← cuda/*, physics/particle_system
    cuda/gpu_reduction.hpp       ← core/types
        │
        ▼
Layer 3 (Simulation & Profile):
    sim/simulation_config.hpp    ← core/types, physics/density_model
    sim/engine.hpp               ← physics/*, sim/*, profile/*, cuda/*, hooks/*
    sim/timestep.hpp             ← core/types
    sim/deterministic.hpp        ← core/types
    sim/adaptive_timestep.hpp    ← sim/simulation_config
    sim/yoshida.hpp              ← (header-only constants)
    sim/async_pipeline.hpp       ← cuda/device_particles, cuda/pinned_buffer
    profile/energy_tracker.hpp   ← core/types
    profile/benchmark.hpp        ← core/types
    profile/frame_profiler.hpp   ← core/types
    profile/memory_tracker.hpp   ← core/types
    profile/gpu_metrics.hpp      ← core/types
    profile/nsight_markers.hpp   ← core/platform
    memory/gpu_pool.hpp          ← core/types
    hooks/phase13_hooks.hpp      ← physics/particle_system
        │
        ▼
Layer 4 (Interop):
    interop/native_api.h         ← <stdint.h> only (pure C)
```

### Key Observations

1. **No circular dependencies** exist anywhere in the codebase
2. `native_api.h` depends only on `<stdint.h>` -- it is pure C with zero C++ includes
3. `native_api.cpp` bridges the C world to C++ by including `engine.hpp`
4. `engine.hpp` is the only "fan-in" header that includes from all subsystems
5. All math types (`vec3d`, `mat4d`, `quaterniond`) are header-only value types

---

## Layer Descriptions

### Layer 0: Core

**Files**: `core/types.hpp`, `core/platform.hpp`, `core/constants.hpp`, `core/error.hpp`

Platform abstraction, type aliases (`f64`, `i32`, `u8`, `usize`), physical constants
(`G = 1` in N-body units), and an exception hierarchy with typed error codes.

### Layer 1: Math & Jobs

**Math**: Header-only 3D vector, 4x4 matrix, and quaternion types in double precision.
All operations are `constexpr` where possible.

**Job System**: Singleton thread pool (up to 64 workers) with lock-free MPMC work queue.
Used for parallel Barnes-Hut traversal and parallel force computation.

### Layer 2: Physics & CUDA

**Particle System** (`particle_system.hpp/cpp`): Structure-of-Arrays with 18 parallel
arrays: `pos_x/y/z`, `vel_x/y/z`, `acc_x/y/z`, `old_acc_x/y/z`, `mass`, `radius`,
`density`, `is_active`, `is_collidable`, `body_type_index`. Aligned heap allocation.

**Barnes-Hut** (`barnes_hut_solver.hpp/cpp`): O(N log N) gravity via octree traversal.
Monopole-only approximation with opening criterion `(2s)^2 < theta^2 * d^2`. Unified
collision detection during traversal (Phase 14-15). Potential energy computation for
O(N log N) PE (Phase 16-17).

**Collision System** (`collision_detector.hpp`, `collision_resolver.hpp/cpp`):
Three-stage pipeline: Detection -> Resolution -> Compaction. Four modes: Ignore,
Elastic, Inelastic, Merge. Merge safeguards: 64/frame, 2/body caps.

**Density Model** (`density_model.hpp/cpp`): Per-body density `rho = m / (4pi/3 * r^3)`.
Density-preserving merge computes survivor radius from combined mass and pre-merge density.

**CUDA Kernels**: 15 kernel files covering gravity (3 softening variants), integration
(kick-drift, kick-rotate), collision detection, Morton sort, GPU octree, prefix-scan
compaction, merge resolution, and deterministic reductions.

### Layer 3: Simulation & Profile

**Engine** (`engine.hpp/cpp`, ~1200 lines): Top-level Facade pattern. Owns all 16
subsystem objects and dispatches to the correct step method based on integrator type
and compute mode. See [PIPELINE.md](PIPELINE.md) for execution flow.

**Timestep** (`timestep.hpp/cpp`): Fixed-timestep accumulator with safety cap (10 steps/frame).

**Deterministic Mode** (`deterministic.hpp/cpp`): Fixed dispatch order, SplitMix64
seeded PRNG, forced GPU sync. Guarantees bit-exact results in 6/8 modes.

**Adaptive Timestep** (`adaptive_timestep.hpp/cpp`): `dt = eta * sqrt(eps / a_max)`,
clamped to `[dt_min, dt_max]`. Applied between steps to preserve per-step symplecticity.

**Yoshida** (`yoshida.hpp`): Header-only Forest-Ruth 4th-order symplectic coefficients.

**Energy Tracker** (`energy_tracker.hpp/cpp`): Snapshot computation (KE, PE, momentum,
angular momentum, COM, virial ratio). Rolling averages over 300-sample window.

**Benchmark** (`benchmark.hpp/cpp`): Rolling performance metrics, FPS estimation.

### Layer 4: Interop

**native_api.h**: Pure C header with 56+ `extern "C"` functions. Uses only `<stdint.h>`.
Provides the complete P/Invoke surface for .NET consumption.

**native_api.cpp**: Bridges C calls to the Engine C++ facade. Manages a static
`Engine*` singleton with `celestial_init()` / `celestial_shutdown()`. All C++ exceptions
are caught and swallowed at the boundary (error codes returned where applicable).

---

## Engine Facade Design

The `Engine` class follows the **Facade pattern** -- it aggregates 16 subsystem objects
and delegates all work to them. The public API is deliberately simple (init, step,
set_particles, get_positions) while the internal routing is complex.

### Member Objects (16)

| Member | Type | Responsibility |
|--------|------|----------------|
| `config_` | `SimulationConfig` | All configuration state |
| `particles_` | `ParticleSystem` | SoA body data (18 arrays) |
| `bh_solver_` | `BarnesHutSolver` | CPU octree gravity + collision |
| `pn_correction_` | `PostNewtonianCorrection` | 1PN relativistic corrections |
| `collision_detector_` | `CollisionDetector` | O(N^2) brute collision detect |
| `collision_resolver_` | `CollisionResolver` | Elastic/inelastic/merge |
| `density_model_` | `DensityModel` | Per-body density computation |
| `collision_pairs_` | `vector<CollisionPair>` | Collision pair buffer |
| `timestep_` | `Timestep` | Fixed-DT accumulator |
| `gpu_pipeline_` | `AsyncPipeline` | Double-buffered GPU transfers |
| `deterministic_` | `DeterministicMode` | Seeded PRNG, forced sync |
| `adaptive_timestep_` | `AdaptiveTimestep` | Dynamic DT from a_max |
| `gpu_pool_` | `GpuPool` | 256 MB GPU scratch memory |
| `profiler_` | `FrameProfiler` | Per-frame timing |
| `energy_tracker_` | `EnergyTracker` | Conservation diagnostics |
| `benchmark_` | `BenchmarkLogger` | Rolling perf metrics |

Plus conditionally: `gpu_tree_solver_` (GpuTreeSolver, CUDA-only).

### Why Not IComputeBackend?

The original Phase 21 spec proposed an `IComputeBackend` interface to abstract CPU vs
GPU differences. After analysis, this was **evaluated and rejected** for these reasons:

1. **Different method signatures**: `step_cpu_brute_force` takes no GPU parameters,
   while `step_gpu_barnes_hut` interacts with `gpu_pool_`, `gpu_tree_solver_`, CUDA
   streams, and async pipelines. A common interface would require lowest-common-denominator
   parameters or `void*` bags.

2. **Vtable overhead**: Virtual dispatch per step adds measurable overhead for the
   GPU fast path where we aim for <1ms step times at 10k bodies.

3. **Leaky abstraction**: The 4 backends share the KDK sequence but differ in every detail
   (memory management, synchronization, collision detection path). The "abstraction" would
   leak more than it hides.

4. **Current switch dispatch works**: The double-switch (`integrator x compute_mode`) in
   `Engine::step()` is 30 lines of straight routing code. It is easy to audit, easy to
   extend, and zero overhead.

The step dispatch (8 modes) is documented in detail in [PIPELINE.md](PIPELINE.md).

---

## Folder Structure

### Current Layout

```
engine/
├── CMakeLists.txt                        Build configuration
├── include/celestial/                    Public headers (44 files)
│   ├── core/         (4 files)           Foundation types, platform, constants, errors
│   ├── math/         (3 files)           Vec3d, Mat4d, Quaterniond
│   ├── job/          (4 files)           Thread pool, work queue, job types
│   ├── physics/      (9 files)           Particles, octree, BH solver, collisions, PN, density
│   ├── cuda/         (8 files)           Device context, kernels, GPU tree, reductions, Morton
│   ├── memory/       (1 file)            GPU memory pool
│   ├── sim/          (7 files)           Engine, config, timestep, determinism, adaptive DT, Yoshida
│   ├── profile/      (6 files)           Energy tracker, benchmark, frame profiler, memory, NSight
│   ├── hooks/        (1 file)            Phase 13 per-step hooks
│   └── interop/      (1 file)            native_api.h (pure C)
├── src/                                  Implementation files (36 files)
│   ├── core/         (1 file)            error.cpp
│   ├── job/          (2 files)           job_system.cpp, job_queue.cpp
│   ├── physics/      (7 files)           barnes_hut, collision_*, density, octree_*, particles, PN
│   ├── cuda/         (15 files)          All CUDA kernels (.cu)
│   ├── memory/       (1 file)            gpu_pool.cu
│   ├── sim/          (4 files)           engine.cpp, timestep, deterministic, adaptive_timestep
│   ├── profile/      (4 files)           energy_tracker, benchmark, frame_profiler, memory_tracker
│   └── interop/      (1 file)            native_api.cpp
├── tests/                                Test files (11 files)
│   ├── test_engine.cpp                   Engine lifecycle
│   ├── test_gravity.cpp                  Direct gravity
│   ├── test_job_system.cpp               Thread pool
│   ├── test_octree.cpp                   Octree construction
│   ├── test_vec3d.cpp                    Vec3d math
│   ├── test_timestep.cpp                 Accumulator
│   ├── test_phase13.cpp                  Integrator, adaptive DT, softening
│   ├── test_phase14_15.cpp  (25 tests)   Density, compaction, merge safeguards, unified BH
│   ├── test_phase16_17.cpp  (25 tests)   Orbital mechanics, energy tracking, stress tests
│   ├── test_phase18_19.cpp  (8 tests)    BH accuracy, drift stress
│   └── test_validation.cpp  (11+ tests)  Core validation suite
└── docs/                                 Engineering documentation (this directory)
```

### Folder Reorganization Assessment

The original Phase 21 spec proposed reorganizing to a `subsystem/` flat structure.
After analysis, this was **evaluated and deferred** for these reasons:

1. **200+ include path edits**: Every `#include <celestial/physics/...>` across all
   source and header files would need updating. High risk of broken builds with no
   functional benefit.

2. **Current structure maps cleanly to layers**: The existing `core/ -> math/ -> physics/
   -> sim/ -> interop/` directory layout directly reflects the dependency graph.

3. **Header/source mirroring is consistent**: Every `include/celestial/X/foo.hpp` has a
   corresponding `src/X/foo.cpp` (or `.cu`). This convention is well-established in C++.

4. **No naming conflicts**: No two files in different directories share the same name.

**Future opportunity**: If the engine grows beyond ~100 source files, a reorganization
into coarser subsystem directories could be justified. The current 80-file layout does
not warrant it.

---

## 8-Mode Engine Matrix

| Feature | CPU_BF | CPU_BH | GPU_BF | GPU_BH |
|---------|--------|--------|--------|--------|
| Gravity algorithm | O(N^2) direct | O(N log N) BH | O(N^2) tiled GPU | O(N log N) GPU BH |
| Softening: Global | YES | YES | YES | YES |
| Softening: PerBodyType | YES | YES | YES | NO |
| Softening: Adaptive | YES | YES | YES | NO |
| Collision: All modes | YES | YES | YES | YES |
| GPU-resident merge | -- | -- | -- | YES (Merge only) |
| Phase 13 hooks | YES | YES | NO | NO |
| Post-Newtonian | YES | YES | YES | YES |
| Adaptive timestep | YES | YES | YES | YES |
| Bit-exact deterministic | YES | YES | YES | NO (D1) |
| GPU validation | -- | -- | -- | YES |

D1: GPU_BarnesHut non-deterministic due to radix sort scatter `atomicAdd`.

---

## Data Flow Summary

```
C# App
  │
  ├── celestial_init(max_particles)
  ├── celestial_set_particles(SoA arrays)
  │
  │   ┌── Per frame ───────────────────────────────────────────────┐
  │   │ celestial_step(dt, softening)                              │
  │   │   └── Engine::step()                                       │
  │   │       ├── Pre-force hooks                                  │
  │   │       ├── Integrator dispatch (Leapfrog or Yoshida4)       │
  │   │       │   └── ComputeMode dispatch (CPU_BF/BH, GPU_BF/BH) │
  │   │       │       └── Half-kick → Drift → Forces → Collisions  │
  │   │       │           → Half-kick → Rotate                     │
  │   │       ├── Post-force hooks                                 │
  │   │       ├── Adaptive timestep update                         │
  │   │       ├── Benchmark recording                              │
  │   │       ├── Deterministic step advance                       │
  │   │       └── Auto-diagnostics (if enabled)                    │
  │   │                                                            │
  │   │ celestial_get_positions(out_x, out_y, out_z)               │
  │   │ celestial_get_energy_drift()                               │
  │   └────────────────────────────────────────────────────────────┘
  │
  └── celestial_shutdown()
```
