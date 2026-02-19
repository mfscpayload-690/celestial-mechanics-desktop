# Celestial Mechanics Desktop

A real-time N-body gravitational simulation engine with 3D visualization, built with .NET 8 and OpenGL.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Overview

Celestial Mechanics Desktop simulates the gravitational interactions of multiple celestial bodies using Newtonian mechanics. It provides three numerical integration methods with measurable trade-offs between accuracy, stability, and performance, alongside a real-time 3D renderer and interactive diagnostics dashboard.

Default launch scenario: two equal-mass bodies in a circular orbit (1 M☉ each, 2 AU separation).

## Features

- **N-body gravity solver** — O(n²) pairwise Newtonian gravity with softening to prevent singularities
- **Three integrators** — Verlet (symplectic, default), Euler (educational), RK4 (high short-term accuracy)
- **Energy & momentum monitoring** — real-time conservation tracking with drift percentage
- **3D OpenGL renderer** — instanced sphere rendering, velocity arrows, procedural grid
- **Interactive camera** — orbit, pan, zoom with smooth damping
- **ImGui dashboard** — simulation controls, body inspector, integrator selector, performance metrics
- **Body templates** — Sun, Earth, Jupiter, Black Hole, Neutron Star, Asteroid
- **Fixed-timestep accumulator** — physics decoupled from frame rate

## Screenshots

> _Add screenshots here once the application is running._

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A GPU with OpenGL 3.3 Core Profile support
- Windows, Linux, or macOS (via Silk.NET windowing)

## Building

```bash
git clone <repo-url>
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

## Roadmap

- [ ] Structure of Arrays (SoA) body layout for cache efficiency
- [ ] Orbital trail / trajectory visualization
- [ ] Collision detection and merging
- [ ] Relativistic corrections (Schwarzschild)
- [ ] Scenario save / load
- [ ] Barnes-Hut tree for O(n log n) scaling

## License

MIT — see [LICENSE](LICENSE).

## Changelog

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

**Tests — 68 passing**
- `Vec3d` operations and spherical coordinates
- Gravity force correctness
- Two-body orbit energy conservation
- Euler drift demonstration
- Linear momentum conservation

---

### Initial commit — 2026-02-19 · `9613ea4`
Repository scaffolding: solution file, project structure, `.gitignore`, `global.json`.
