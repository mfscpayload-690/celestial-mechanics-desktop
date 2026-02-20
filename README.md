# Celestial Mechanics Desktop

A real-time N-body gravitational simulation engine with 3D visualization, built with .NET 8 and OpenGL.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)
[![CI](https://github.com/SharonMathew4/celestial-mechanics-desktop/actions/workflows/ci.yml/badge.svg)](https://github.com/SharonMathew4/celestial-mechanics-desktop/actions/workflows/ci.yml)
[![Release](https://github.com/SharonMathew4/celestial-mechanics-desktop/actions/workflows/release.yml/badge.svg)](https://github.com/SharonMathew4/celestial-mechanics-desktop/actions/workflows/release.yml)

## Overview

Celestial Mechanics Desktop simulates the gravitational interactions of multiple celestial bodies using Newtonian mechanics. It provides three numerical integration methods with measurable trade-offs between accuracy, stability, and performance, alongside a real-time 3D renderer and interactive diagnostics dashboard.

Default launch scenario: two equal-mass bodies in a circular orbit (1 M☉ each, 2 AU separation).

## Features

- **N-body gravity solver** — O(n²) pairwise Newtonian gravity with softening to prevent singularities
- **Three integrators** — Verlet (symplectic, default), Euler (educational), RK4 (high short-term accuracy)
- **SoA body layout** — Structure-of-Arrays storage with `CpuSingleThreadBackend` and `CpuParallelBackend`
- **Multithreaded force computation** — `Parallel.For` over bodies with determinism toggle
- **Energy & momentum monitoring** — real-time conservation tracking with drift percentage
- **3D OpenGL renderer** — instanced sphere rendering, velocity arrows, procedural grid
- **Interactive camera** — orbit, pan, zoom with smooth damping
- **ImGui dashboard** — simulation controls, body inspector, integrator selector, performance metrics
- **Body templates** — Sun, Earth, Jupiter, Black Hole, Neutron Star, Asteroid
- **Fixed-timestep accumulator** — physics decoupled from frame rate
- **Benchmark suite** — structured ms/step, steps/s, and energy drift output for 100–1000 bodies

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A GPU with OpenGL 3.3 Core Profile support
- Windows, Linux, or macOS (via Silk.NET windowing)

## Building

```bash
git clone https://github.com/SharonMathew4/celestial-mechanics-desktop.git
cd celestial-mechanics-desktop
dotnet build
```

To run:

```bash
dotnet run --project src/CelestialMechanics.App
```

To run tests:

```bash
dotnet test
```

## Project Structure

```
celestial-mechanics-desktop/
├── src/
│   ├── CelestialMechanics.App          # Entry point, main loop, ImGui overlay, input
│   ├── CelestialMechanics.Renderer     # OpenGL renderer, shaders, camera
│   ├── CelestialMechanics.Simulation   # Simulation engine, clock, state management
│   ├── CelestialMechanics.Physics      # N-body solver, integrators, force models
│   ├── CelestialMechanics.Math         # Vec3d, Mat4d, Quaterniond, physical constants
│   └── CelestialMechanics.Data         # Body templates, observation catalogs
└── tests/
    ├── CelestialMechanics.Math.Tests
    └── CelestialMechanics.Physics.Tests
```

## Integrators

| Integrator | Order | Symplectic | Energy Drift (10k steps) | Use Case |
|------------|-------|------------|--------------------------|----------|
| Verlet     | 2nd   | Yes        | < 0.01%                  | Default — long-term orbital simulations |
| Euler      | 1st   | No         | > 0.1%                   | Educational — demonstrates instability |
| RK4        | 4th   | No         | < 1% (1k steps)          | Short-term high-accuracy predictions |

The Verlet integrator is recommended for most use cases. It is symplectic, meaning it preserves the phase-space volume of the Hamiltonian system and prevents secular energy drift over long simulations.

## Controls

| Input | Action |
|-------|--------|
| `Space` | Play / Pause |
| `→` (Right arrow) | Single step |
| `R` | Reset simulation |
| Left mouse drag | Orbit camera |
| Right mouse drag | Pan camera |
| Scroll wheel | Zoom |

## Physics Details

**Gravitational force** between bodies _i_ and _j_:

```
F = G * m_i * m_j / (|r_ij|² + ε²)
```

where `ε = 1e-4` is the softening parameter that prevents singularities at close approaches.

**Units:**
- Mass: solar masses (M☉)
- Distance: astronomical units (AU)
- Time: simulation time units

Bidirectional conversion to SI units is provided via `UnitConversion`.

**Physical constants** (G, c, M☉, AU, etc.) are defined in `PhysicalConstants`.

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Silk.NET.OpenGL | 2.23.0 | OpenGL bindings |
| Silk.NET.Windowing | 2.23.0 | Cross-platform windowing |
| Silk.NET.Input | 2.23.0 | Keyboard and mouse input |
| Silk.NET.OpenGL.Extensions.ImGui | 2.23.0 | ImGui integration |
| ImGui.NET | 1.91.6.1 | Immediate mode GUI |

## Architecture Notes

- **Accumulator pattern**: physics runs at a fixed timestep (0.001s) independent of frame rate; interpolation alpha smooths rendering between steps.
- **Instanced rendering**: all spheres are drawn in a single draw call.
- **Immutable SimulationState**: diagnostics snapshots are decoupled from the mutable body array.
- **Custom math types**: `Vec3d` is a double-precision 3D vector designed for sequential memory layout and future SIMD optimization.
- **SoA physics pipeline**: `BodySoA` stores positions, velocities, accelerations and masses as separate contiguous arrays. The O(n²) inner loop reads `PosX[j]`, `PosY[j]`, `PosZ[j]`, `Mass[j]` sequentially, giving the hardware prefetcher a stride-1 pattern. A 64-byte L1 cache line holds 8 doubles; after the first miss at `j=0`, indices `j=1..7` are served from L1 at no cost. Estimated reduction in cache misses vs. AoS: ~4×.
- **Dual execution paths**: `NBodySolver` supports an AoS path (all three integrators) and a SoA path (Verlet only). Both paths share the same energy/momentum diagnostics. Tests use the AoS path so no test code was changed.
- **Pluggable compute backends** (`IPhysicsComputeBackend`): `CpuSingleThreadBackend` uses Newton's 3rd law to halve arithmetic (n(n−1)/2 pairs). `CpuParallelBackend` uses `Parallel.For` over the outer loop — each thread writes only to `AccX[i]`, no locks, no reductions. `DeterministicMode = true` forces the single-thread backend regardless of `UseParallelComputation`.
- **GPU-ready architecture**: `IPhysicsComputeBackend` is the only interface a CUDA or compute-shader implementation needs to satisfy. The SoA arrays map directly to GPU memory without struct packing.

## Roadmap

- [x] Structure of Arrays (SoA) body layout for cache efficiency
- [x] `CpuSingleThreadBackend` — deterministic O(n²/2) with Newton's 3rd law
- [x] `CpuParallelBackend` — `Parallel.For` outer loop, embarrassingly parallel
- [x] `IPhysicsComputeBackend` interface — architecture ready for CUDA / compute shaders
- [x] `PhysicsBenchmark` suite — 100 / 300 / 500 / 1000 body measurements
- [ ] Orbital trail / trajectory visualization
- [ ] Collision detection and merging
- [ ] Relativistic corrections (Schwarzschild)
- [ ] Scenario save / load
- [ ] Barnes-Hut tree for O(n log n) scaling
- [ ] SIMD vectorisation (AVX-256 / AVX-512) — SoA layout prerequisite is in place
- [ ] CUDA backend (`CudaBackend : IPhysicsComputeBackend`)
- [ ] OpenGL compute-shader backend (`ComputeShaderBackend : IPhysicsComputeBackend`)

## CI/CD

Two GitHub Actions workflows live in `.github/workflows/`.

### `ci.yml` — Continuous Integration

Runs on every push to `main` and on all pull requests.

| Job | Runner | What it does |
|-----|--------|-------------|
| `build-check` | ubuntu / windows / macos (matrix) | Restores and builds the full solution in Release mode |
| `test` | ubuntu-latest | Runs both test projects with Cobertura coverage, uploads `.trx` + coverage XML |

- NuGet packages are cached, keyed on `.csproj` file hashes.
- In-progress runs on the same branch/PR are cancelled automatically.
- `paths-ignore` skips runs on documentation-only commits.

### `release.yml` — Release

Triggered by a version tag push. Supports stable (`v1.0.0`) and pre-release (`v1.0.0-beta.1`) tags.

| Job | Depends on | What it does |
|-----|-----------|-------------|
| `test` | — | Gate: full test suite must pass |
| `publish` (matrix) | `test` | Self-contained single-file build for win-x64, linux-x64, osx-arm64 |
| `release` | `publish` | Downloads all artifacts, creates the GitHub Release |

Tags containing a hyphen are automatically marked as pre-release.
`PublishTrimmed=false`: Silk.NET and ImGui.NET use reflection; trimming breaks P/Invoke resolution.


## License

MIT — see [LICENSE](LICENSE).

## Changelog

### [Phase 2] — 2026-02-20 · Physics Core Performance Upgrade

High-performance, cache-efficient, multithread-ready physics pipeline. No new physics features; no renderer or UI changes.

**SoA body layout**
- `BodySoA` — Structure-of-Arrays buffer with separate contiguous arrays for `PosX/Y/Z`, `VelX/Y/Z`, `AccX/Y/Z`, `OldAccX/Y/Z`, `Mass`, `IsActive`. Allocated once; zero heap allocations per simulation step.
- `BodySoA.CopyFrom` / `CopyTo` — O(n) conversion helpers to bridge the SoA hot path with the AoS renderer interface.

**Compute backends (`IPhysicsComputeBackend`)**
- `CpuSingleThreadBackend` — O(n²/2) pairwise with Newton's 3rd law; symmetric force accumulation using local register variables and stride-1 array access. Deterministic and bit-reproducible.
- `CpuParallelBackend` — `Parallel.For` over the outer body index. Each thread writes exclusively to `AccX[i]`; no locks, no atomic operations, no thread-local allocations. Does not use Newton's 3rd law (avoids concurrent writes). Near-linear scaling with thread count up to memory-bandwidth saturation.

**SoA Verlet integrator (`SoAVerletIntegrator : ISoAIntegrator`)**
- Velocity Verlet operating directly on `BodySoA` arrays: half-kick → drift → `backend.ComputeForces()` → half-kick → rotate acceleration buffers. All O(n) passes use stride-1 array access with no heap allocations.

**Solver upgrade (`NBodySolver`)**
- `ConfigureSoA(enabled, softening, deterministic, useParallel)` — enables SoA path with runtime backend selection.
- `DeterministicMode = true` forces `CpuSingleThreadBackend` regardless of `UseParallelComputation`.
- AoS path (all three integrators) fully preserved; existing tests require no changes.

**Configuration (`PhysicsConfig`)**
- New properties: `UseSoAPath`, `DeterministicMode`, `UseParallelComputation`.
- `SimulationEngine` wires these to `NBodySolver.ConfigureSoA` at construction and on every `SetIntegrator` call.

**Benchmark suite (`PhysicsBenchmark`)**
- Runs AoS single-thread, SoA single-thread, and SoA parallel backends at 100 / 300 / 500 / 1000 bodies × 10,000 steps.
- Reports ms/step, steps/second, energy drift; prints SoA and parallel speedup ratios.
- Includes static memory-layout analysis: AoS vs. SoA cache-line utilisation, branch prediction, SIMD and GPU readiness.

**Tests — 73 passing (68 preserved + 5 new)**
- `SoAVerlet_SingleThread_TwoBodyOrbit_EnergyDriftBelow001Percent`
- `SoAVerlet_SingleThread_TwoBodyOrbit_BodiesRemainBound`
- `SoAVerlet_Parallel_TwoBodyOrbit_EnergyDriftBelow001Percent`
- `SoAVerlet_ThreeBody_MomentumConserved`
- `SoAVerlet_EnergyDrift_ComparableToAoSBaseline`

---

### [Phase 1] — 2026-02-19 · `2beaa1b`
**Implement Phase 1: Core 3D engine, N-body gravity, Verlet integrator, OpenGL rendering**

Complete ground-up implementation of the simulation engine:

**Math library**
- `Vec3d` — double-precision 3D vector with operator overloading and spherical coordinate support
- `Mat4d`, `Quaterniond` — matrix and quaternion types for 3D transforms
- `PhysicalConstants` — SI and simulation-unit constants (G, c, M☉, AU, etc.)
- `UnitConversion` — bidirectional SI ↔ simulation unit conversion (mass, distance, time, velocity)

**Physics module**
- Newtonian gravity with softening parameter (ε = 1e-4) to handle close approaches
- Three integrators: Verlet (symplectic, default), Euler (educational), RK4 (high short-term accuracy)
- O(n²) N-body pairwise solver
- Energy calculator (kinetic, potential, total) and momentum validator
- Schwarzschild radius and Roche limit computations

**Simulation core**
- Fixed-timestep engine with accumulator pattern and interpolation alpha
- High-resolution simulation clock with time scale multiplier
- Event bus for inter-component communication

**Renderer (OpenGL 3.3 via Silk.NET)**
- Instanced icosphere rendering (single draw call for all bodies)
- Phong-lit GLSL shaders (sphere, grid, line)
- Procedural grid background with distance-field transparency
- Velocity arrow line renderer
- Orbital camera with smooth damping (orbit, pan, zoom)

**App layer**
- Silk.NET window (1600×900, 60 FPS VSync)
- ImGui overlay with 6 panels: simulation controls, energy monitor, integrator selector, body inspector, add-body panel, performance metrics
- Keyboard (`Space`, `→`, `R`) and mouse input handling

**Data**
- Observation catalog and object templates (Sun, Earth, Jupiter, Black Hole, Neutron Star, Asteroid)
- Gravity strength and range mapping per template

**Tests — 68 passing (Phase 1 baseline)**
- `Vec3d` operations and spherical coordinates
- Gravity force correctness
- Two-body orbit energy conservation
- Euler drift demonstration
- Linear momentum conservation

---

### Initial commit — 2026-02-19 · `9613ea4`
Repository scaffolding: solution file, project structure, `.gitignore`, `global.json`.
