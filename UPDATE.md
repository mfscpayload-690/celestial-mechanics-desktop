# Celestial Mechanics Desktop - Current Status Update

Date: 2026-04-12
Prepared for: next update planning and implementation handoff

## 1. Executive Summary

This repository currently contains two parallel technology tracks:

1. Active managed desktop application stack in .NET 8 (solution-based, running now)
2. Native C++/CUDA engine stack in `engine/` (architecturally mature, separate build system)

Current operational status:

- Desktop app launch: SUCCESS (process observed running: `CelestialMechanics.App.exe`)
- Managed solution build: SUCCESS (`dotnet build CelestialMechanics.sln -c Debug`, 0 warnings, 0 errors)
- Managed test suite: SUCCESS (271/271 tests passed across 4 test projects)
- Key runtime/solver files diagnostics: no active editor/LSP errors
- Native engine: present and documented, but not wired into the active C# runtime path

Bottom line: the app is in a stable and shippable development state on the managed stack. The most important architectural decision for the next update is whether to continue enhancing the current managed solver path or begin integrating the native C++/CUDA engine path.

## 2. Solution Topology (Authoritative)

Active .NET solution projects (`CelestialMechanics.sln`):

- `src/CelestialMechanics.App`
- `src/CelestialMechanics.AppCore`
- `src/CelestialMechanics.Data`
- `src/CelestialMechanics.Math`
- `src/CelestialMechanics.Physics`
- `src/CelestialMechanics.Renderer`
- `src/CelestialMechanics.Simulation`
- `tests/CelestialMechanics.AppCore.Tests`
- `tests/CelestialMechanics.Math.Tests`
- `tests/CelestialMechanics.Physics.Tests`
- `tests/CelestialMechanics.Simulation.Tests`

Framework baseline:

- `global.json` pins SDK `8.0.418` with `rollForward: latestPatch`
- Current runs observed on SDK `8.0.419` (expected with roll-forward)

## 3. Frontend Status (Desktop Client)

Frontend here means the user-facing desktop runtime: windowing, input, rendering, and UI controls.

### 3.1 Entry and Runtime Loop

Current app entrypoint (`src/CelestialMechanics.App/Program.cs`):

- Window size: 1600x900
- Graphics API: OpenGL 3.3 Core via Silk.NET
- VSync: enabled
- Lifecycle wiring: `Load`, `Update`, `Render`, `Closing`, `Resize`

`Application` startup (`src/CelestialMechanics.App/Application.cs`) currently:

- Creates OpenGL + input contexts
- Initializes ImGui controller and overlay
- Initializes simulation engine with high-interactivity config
- Initializes renderer and camera/input handling
- Boots with default two-body circular orbit scenario
- Starts simulation clock and engine immediately

### 3.2 Active Frontend Feature Surface

UI system (`ImGuiOverlay`) includes:

- Floating panels:
  - Simulation Controls
  - Energy Monitor
  - Performance
  - Integrator
  - Add Body
  - Body Inspector
- Bottom control panel:
  - Mode switch (`ADD`, `SIMULATE`)
  - Time flow slider/input (`1` to `10000`)
  - Panel visibility chips
- Interactive placement workflow:
  - Ghost-follow preview
  - Right-click anchor
  - Left-click commit
  - Gravity-aware trajectory preview samples
- Environment perturbation controls:
  - gas drag approximation
  - turbulence field
  - local gravity amplification region

Input and interaction:

- Keyboard/mouse control through `InputHandler`
- Camera motion can be blocked while placement mode is active
- Step/pause/reset controls are connected to simulation state machine

### 3.3 Rendering Status

Renderer core (`src/CelestialMechanics.Renderer/GLRenderer.cs`) is feature-rich and currently active:

- Instanced sphere rendering
- Grid rendering
- Line rendering (velocity vectors, preview paths)
- Procedural background rendering
- Orbital trails (per-body queues)
- Collision flash effects
- Accretion disk particle renderer
- Star-driven lighting and ray-shadow options
- Black hole visual quality tiers/presets/debug views
- Shader discovery fallback (output folder and source-relative fallback)

Shader assets are copied from `src/CelestialMechanics.Renderer/Shaders/**/*.vert|frag` into output.

### 3.4 Frontend Runtime Observability

Runtime diagnostics logger (`RuntimeDiagnosticsLogger`) writes NDJSON snapshots to:

- `test-results/module1/runtime-diagnostics.ndjson`

Captured fields include:

- frame/physics/render timing
- active solver backend and integrator
- body/collision counts
- major physics flags (SoA, Barnes-Hut, deterministic, parallel, SIMD, adaptive timestep)
- visible UI panel state
- major renderer feature toggles

### 3.5 Frontend Current Runtime Result

Launch command executed:

- `dotnet run --project src/CelestialMechanics.App/CelestialMechanics.App.csproj`

Observed result:

- GUI process running (`CelestialMechanics.App.exe`) at time of check
- No immediate startup crash signature observed from command session

## 4. Backend Status (Managed .NET Simulation Stack)

Backend here means simulation logic, physics, state systems, and serialization used by the desktop app.

### 4.1 Physics Configuration and Runtime Policy

`PhysicsConfig` supports broad runtime modes:

- Integrators: Euler, Verlet, RK4
- Force regularization: softening modes
- Execution path: AoS and SoA
- Deterministic vs parallel compute modes
- Barnes-Hut tree acceleration
- Collision pipeline + broad phase
- Adaptive timestep + collision substepping
- SIMD toggle
- Relativistic and high-energy extensions:
  - Post-Newtonian corrections
  - accretion disk effects
  - gravitational wave estimation
  - jet emission controls

### 4.2 Solver Architecture

`NBodySolver` currently supports:

- AoS legacy path for compatibility and non-SoA integrators
- SoA path with selectable backends
- Backend routing policy:
  - Barnes-Hut backends when enabled
  - deterministic single-thread backend when deterministic mode is on
  - optional parallel CPU backend for throughput
  - optional SIMD backend
- Collision handling integrated in SoA step pipeline
- Diagnostics outputs (energy, momentum, drift, collision burst metadata)

Important implementation note:

- `CudaPhysicsBackend` in managed physics exists as a stub and throws `NotImplementedException`
- Therefore, managed runtime does not currently execute a real CUDA path via this class

### 4.3 Simulation Orchestration

Two orchestration layers are present:

1. `SimulationEngine` (used directly by the desktop app)
2. `SimulationManager` ECS-like orchestration (used by AppCore snapshot/serialization workflows)

`SimulationEngine` currently provides:

- fixed-step accumulator loop with dynamic substep budgeting
- adaptive timestep computation from acceleration/velocity/radius heuristics
- collision-safe substepping to reduce tunneling
- integrator switching and solver reconfiguration
- current and previous state snapshots for interpolation and UI

`SimulationManager` currently provides:

- entity/component orchestration
- expansion/time management systems
- event trigger/action framework
- catastrophic event pipeline (supernova, shockwaves, mergers)
- entity<->PhysicsBody sync around solver execution

### 4.4 AppCore Status

AppCore role: non-UI application core services.

Implemented capabilities:

- scene graph and selection management
- mode management (simulation/observation)
- snapshot ring buffer with memory cap and restore support
- project serialization/deserialization to `.cesim` zip package
- determinism validation workflow (save/load/re-run drift checks)

Serialization package contents include:

- `ProjectMetadata.json`
- `Scene.json`
- `PhysicsConfig.json`
- `SimulationState.json`
- `Entities.json`
- `EventHistory.json`

## 5. Backend Status (Native C++/CUDA Engine Track)

The `engine/` folder is a separate native backend track with its own build and test pipeline.

### 5.1 Native Engine Build/Structure Snapshot

From current tree and `engine/CMakeLists.txt`:

- CMake minimum: 3.25
- Languages: C++20 + CUDA
- CUDA architectures targeted: 75, 80, 86, 89, 90
- Build options:
  - tests ON by default
  - benchmark OFF by default
  - shared library ON by default for C# interop intent

Native inventory counts observed:

- headers (`.hpp`/`.h`): 45
- C++ implementation files (`.cpp`): 21
- CUDA files (`.cu`): 16
- native test files (`engine/tests/*.cpp`): 11

### 5.2 Native Engine Functional Position

Docs and file layout indicate a full architecture with:

- SoA particle system
- Barnes-Hut/octree
- collisions and density modeling
- deterministic mode/timestep controls
- profiling modules
- C interop surface (`native_api.h/.cpp`)

### 5.3 Native/Managed Integration Status Today

Current .NET runtime path does not show active interop binding:

- no `DllImport`/`LibraryImport` matches in `src/**/*.cs` at scan time
- active app uses managed `SimulationEngine`/`NBodySolver` directly

Interpretation: native backend is present and advanced, but not currently connected to the active managed app execution path.

## 6. Verification and Quality Status

### 6.1 Build Status (Managed)

Command:

- `dotnet build CelestialMechanics.sln -c Debug`

Result:

- SUCCESS
- 0 warnings
- 0 errors

### 6.2 Test Status (Managed)

Command:

- `dotnet test CelestialMechanics.sln -c Debug --no-build`

Results:

- `CelestialMechanics.Math.Tests`: 54 passed
- `CelestialMechanics.AppCore.Tests`: 25 passed
- `CelestialMechanics.Simulation.Tests`: 95 passed
- `CelestialMechanics.Physics.Tests`: 97 passed
- Total: 271 passed, 0 failed, 0 skipped

### 6.3 Key File Static Diagnostics

Checked:

- `src/CelestialMechanics.App/Application.cs`
- `src/CelestialMechanics.Simulation/SimulationEngine.cs`
- `src/CelestialMechanics.Physics/Solvers/NBodySolver.cs`

Result:

- no reported errors

### 6.4 Source Inventory (Managed)

Non-generated C# file counts (excluding `bin/`, `obj/`, `obj_alt/`):

- `src/CelestialMechanics.App`: 5
- `src/CelestialMechanics.Renderer`: 11
- `src/CelestialMechanics.Simulation`: 34
- `src/CelestialMechanics.Physics`: 54
- `src/CelestialMechanics.Math`: 5
- `src/CelestialMechanics.AppCore`: 19
- `src/CelestialMechanics.Data`: 5
- `tests`: 22

## 7. Inconsistencies and Technical Signals to Track

### 7.1 Stale Legacy Build Artifacts in Repo Root

Existing text artifacts (`compile_output.txt`, `fresh_desktop_build.txt`, `fresh_restore_diag.txt`) reference an older project path/name (`CelestialMechanics.Desktop`) and include a failed restore/build attempt from that era.

Current truth should be taken from fresh solution commands against `CelestialMechanics.sln` and `CelestialMechanics.App`.

### 7.2 Dual Backend Strategy Not Yet Unified

There is currently a strategic split:

- managed solver path is production-active in the desktop app
- native C++/CUDA engine path exists but is not integrated into current managed runtime

This is not an immediate defect, but it is the key architectural fork for the next phase.

### 7.3 Determinism Defaults vs Interactive Runtime Defaults

- `PhysicsConfig` default favors deterministic mode
- desktop `Application` startup config currently sets `DeterministicMode = false` and `UseParallelComputation = true`

This is intentional for interactive throughput, but update plans should explicitly state when reproducibility is required (e.g., regression scenarios, deterministic replay, scientific baselines).

## 8. Current Readiness Assessment

### 8.1 Ready Now

- Continue feature development on UI/renderer/simulation managed path
- Add new physics options and UX controls
- Add/extend tests with confidence (current suite green)
- Produce diagnostics snapshots for runtime analysis

### 8.2 Needs Decision Before Deep Refactors

- Whether to integrate native engine into active .NET app now, later, or in a separate branch
- Whether managed `CudaPhysicsBackend` should be implemented or replaced by native-engine interop strategy
- Whether stale historical log artifacts should be archived/removed to reduce confusion

## 9. Recommended Next-Update Workplan (Actionable)

Use this as a direct prompt basis for the next planning session.

1. Decide backend direction
   - Option A: continue managed backend evolution (fastest delivery)
   - Option B: start managed-native interop integration milestone

2. Stabilize runtime diagnostics into a reproducible benchmark protocol
   - define scenario seeds and body counts
   - standardize capture windows
   - compare deterministic vs parallel runs

3. Add release-mode validation lane
   - `dotnet build -c Release`
   - `dotnet test -c Release --no-build`
   - smoke-launch check

4. Clean stale root diagnostics artifacts
   - mark as historical or move to archival folder
   - prevent accidental use as current status source

5. If native integration is selected
   - define interop contract boundary (who owns arrays and lifecycle)
   - add first minimal bridge path (positions/velocities step call)
   - add parity tests managed vs native on fixed scenarios

## 10. Implementation Outcome Log (This Session)

This section records what was successfully implemented/executed versus what failed or was blocked during the status update work.

### 10.1 Successfully Implemented/Completed

1. Application launch execution
  - `dotnet run --project src/CelestialMechanics.App/CelestialMechanics.App.csproj` executed.
  - Running process confirmed: `CelestialMechanics.App.exe`.

2. Managed solution verification
  - `dotnet build CelestialMechanics.sln -c Debug` completed successfully.
  - Result: 0 warnings, 0 errors.

3. Managed test verification
  - `dotnet test CelestialMechanics.sln -c Debug --no-build` completed successfully.
  - Result: 271/271 tests passed.

4. Architecture and status audit
  - Confirmed active managed runtime path (App + SimulationEngine + managed NBodySolver).
  - Confirmed native `engine/` track exists and is currently not wired via managed interop bindings.

5. Documentation implementation
  - Created and populated this file (`UPDATE.md`) with the full current-state report.
  - Added explicit readiness, risk, and next-update workplan sections.

### 10.2 Failed/Blocked/Not Completed

1. First process check attempt failed
  - `wmic` based process query failed because `wmic` is unavailable in the active shell environment.
  - Mitigation used: switched to alternative process inspection commands and still confirmed app runtime.

2. First PowerShell CIM process filter attempt failed
  - An early `Get-CimInstance` command had quoting/command-format issues and did not return valid filtered output.
  - Mitigation used: fallback process listing and grep-based confirmation.

3. First module-count PowerShell command failed
  - A malformed inline PowerShell command failed during count aggregation.
  - Mitigation used: replaced with shell-safe counting command and obtained final counts.

4. Memory note files unavailable
  - Attempted reads for `/memories/repo/build-notes.md` and `/memories/repo/cpp-compat-notes.md` failed because files were not present at resolved path.
  - Impact: none on final report quality; status data was sourced directly from workspace files and live command results.

5. Feature implementation scope
  - No new application feature code was implemented in this pass.
  - This pass focused on runtime validation, status extraction, and documentation.

## 11. Quick Prompt Block for Next Chat Session

You can paste this block directly into the next planning conversation:

"Project baseline as of 2026-04-12:
- Managed solution builds clean on .NET 8 (Debug), 0 warnings/errors.
- All managed tests pass: 271/271.
- Desktop app (`CelestialMechanics.App`) runs with OpenGL + ImGui frontend.
- Active runtime backend is managed `SimulationEngine` + `NBodySolver` (SoA/parallel enabled in app startup).
- Native C++/CUDA engine exists in `engine/` with C API surface but is not currently wired into managed runtime (no `DllImport` usage).
- Managed `CudaPhysicsBackend` is currently a stub (`NotImplementedException`).
- Need next-step decision: stay managed-only for upcoming update or begin managed-native integration milestone." 

## 12. Requested Architecture and Delivery Addendum

This addendum explicitly includes the requested project structure, backend detail, UI detail, data flow, completion state, known issues, and tech stack.

### 12.1 Project Structure (Folders/Files)

Top-level structure:

- `src/` (managed application and libraries)
- `tests/` (managed test projects)
- `engine/` (native C++/CUDA engine track)
- `CelestialMechanics.sln` (managed solution)
- `global.json` (.NET SDK pin/roll-forward policy)
- root diagnostics artifacts: `compile_output.txt`, `fresh_desktop_build.txt`, `fresh_restore_diag.txt`

Managed solution structure:

- `src/CelestialMechanics.App`
  - app entrypoint and runtime glue (`Program.cs`, `Application.cs`, `ImGuiOverlay.cs`, `InputHandler.cs`, `RuntimeDiagnostics.cs`)
- `src/CelestialMechanics.Renderer`
  - OpenGL rendering pipeline and shaders (`GLRenderer.cs`, `ShaderProgram.cs`, `Camera.cs`, shader files)
- `src/CelestialMechanics.Simulation`
  - simulation orchestration (`SimulationEngine.cs`, `SimulationClock.cs`, ECS/event/systems/factories)
- `src/CelestialMechanics.Physics`
  - solver/integrators/forces/collisions/validation (`NBodySolver.cs`, integrators, Barnes-Hut, SoA, backends)
- `src/CelestialMechanics.Math`
  - math primitives and constants (`Vec3d.cs`, `Mat4d.cs`, `Quaterniond.cs`, `PhysicalConstants.cs`)
- `src/CelestialMechanics.Data`
  - templates/catalogs (`CelestialCatalog.cs`, object templates, gravity maps)
- `src/CelestialMechanics.AppCore`
  - app core services (scene graph, modes, snapshot, serialization, determinism validation)

Managed tests:

- `tests/CelestialMechanics.Math.Tests`
- `tests/CelestialMechanics.Physics.Tests`
- `tests/CelestialMechanics.Simulation.Tests`
- `tests/CelestialMechanics.AppCore.Tests`

Native engine structure:

- `engine/include/celestial/**` (public headers)
- `engine/src/**` (C++ and CUDA implementation)
- `engine/tests/**` (GoogleTest-style C++ test suite)
- `engine/docs/**` (architecture/build/pipeline docs)

### 12.2 Backend Details (Physics, Algorithms Used)

Managed backend core (active runtime path):

- Gravity modeling:
  - Newtonian gravity with softening
  - optional shell-theorem handling for overlapping bodies
- Integrators:
  - Euler (1st order)
  - Verlet / velocity-Verlet (default, symplectic)
  - RK4 (4th order)
- Data layout and compute path:
  - AoS path for compatibility
  - SoA path for high-performance contiguous-array access
- Solver complexity options:
  - brute-force pairwise gravity O(n^2)
  - Barnes-Hut tree approximation O(n log n)
- Collision pipeline:
  - broad phase: spatial hash culling
  - narrow phase contact detection
  - outcome modes including merge/bounce/realistic fragmentation behavior
- Time control:
  - fixed-step accumulator
  - adaptive timestep heuristics
  - collision safety substepping
- Optional advanced physics toggles:
  - SIMD backend (CPU)
  - post-Newtonian corrections
  - gravitational wave estimation
  - accretion disk/jet related effects

Native backend track (not currently wired into active managed runtime):

- C++20/CUDA architecture with SoA, Barnes-Hut/octree, collision systems, deterministic controls, profiling modules, and C ABI interop surface.

### 12.3 Current UI Structure

Windowing/render shell:

- Silk.NET window lifecycle (`Load`, `Update`, `Render`, `Resize`, `Closing`)
- OpenGL 3.3 Core rendering

ImGui UI hierarchy:

- Floating windows/panels:
  - Simulation Controls
  - Energy Monitor
  - Performance
  - Integrator Selector
  - Add Body
  - Body Inspector
- Bottom control bar:
  - mode toggles (`ADD`, `SIMULATE`)
  - time-flow slider/manual input
  - panel visibility chips
- Placement UX flow:
  - ghost preview follow
  - direction vector editing
  - trajectory preview
  - commit/cancel interactions

Renderer-facing UI toggles include:

- grid/background/trails/velocity arrows
- accretion disk visuals
- lighting/shadow/luminosity/saturation controls
- black-hole visual presets and debug views

### 12.4 Data Flow (UI <-> Backend Communication)

Runtime control/data flow:

1. `Program.cs` creates window and binds lifecycle callbacks to `Application`.
2. `Application.OnLoad` initializes renderer, input, overlay, and `SimulationEngine`.
3. `ImGuiOverlay` and `InputHandler` collect user actions (pause, step, reset, add body, time scaling, toggles).
4. `Application.OnUpdate` applies UI-driven parameters to simulation config and environment effects.
5. `SimulationEngine.Update` advances physics via `NBodySolver` and updates simulation state snapshots.
6. `Application.OnRender` pushes simulation output to `GLRenderer` (`UpdateFromSimulation`), then draws scene and ImGui.
7. `RuntimeDiagnosticsLogger` records periodic runtime snapshots (timing, solver/backend flags, counts, visible UI state).

Bidirectional interaction pattern:

- UI -> backend:
  - integrator selection, timestep scaling, physics toggles, body placement, environment modifiers
- backend -> UI:
  - engine state, energy/performance metrics, collision/body counts, backend/integrator identity, diagnostics values

### 12.5 Features Completed

Completed and currently available in managed runtime:

- real-time desktop simulation app startup and rendering loop
- N-body gravity simulation with multiple integrators
- SoA solver path and optional parallel/SIMD/Barnes-Hut modes
- collision detection and resolution modes
- adaptive timestep and collision-safe substepping
- rich ImGui control surface and interactive body placement
- OpenGL visualization stack (instancing, trails, overlays, effect rendering)
- app-core scene/snapshot/serialization infrastructure (`.cesim`)
- automated managed build/test baseline passing (271 tests)

Completed but not integrated into active managed runtime path:

- native C++/CUDA engine subsystem with its own architecture/build/tests

### 12.6 Known Issues / Bugs

Known technical issues or gaps at this snapshot:

- Managed GPU backend gap:
  - `CudaPhysicsBackend` in managed physics is a stub and throws `NotImplementedException`.
- Native integration gap:
  - native engine exists but is not currently invoked by managed app runtime.
- Legacy artifact confusion risk:
  - root text logs reference older `CelestialMechanics.Desktop` naming and historical failed restore/build attempts.
- Throughput vs reproducibility split:
  - desktop runtime defaults favor non-deterministic parallel execution for interactivity, while defaults elsewhere favor deterministic execution; this can cause expectation mismatch if not explicitly configured per scenario.

### 12.7 Tech Stack (.NET Version, Frameworks, Libraries)

Managed stack:

- Runtime/framework target:
  - .NET 8 (`net8.0`)
- SDK policy:
  - `global.json` pins `8.0.418` with `latestPatch` roll-forward
- Language/platform:
  - C# (modern SDK-style projects)

Primary managed libraries:

- `Silk.NET.Windowing` 2.23.0
- `Silk.NET.OpenGL` 2.23.0
- `Silk.NET.Input` 2.23.0
- `Silk.NET.Maths` 2.23.0
- `Silk.NET.OpenGL.Extensions.ImGui` 2.23.0
- `ImGui.NET` 1.91.6.1

Test stack:

- `xunit` 2.5.3
- `xunit.runner.visualstudio` 2.5.3
- `Microsoft.NET.Test.Sdk` 17.8.0
- `coverlet.collector` 6.0.0

Native stack (parallel track):

- CMake 3.25+
- C++20
- CUDA toolkit/toolchain (architectures 75/80/86/89/90 in build config)

