# Simulation Pipeline

## Overview

The engine executes physics in a **Kick-Drift-Kick (KDK) Leapfrog** pattern with an
optional **Yoshida 4th-order** wrapper. All 8 execution modes (4 ComputeModes x 2
IntegratorTypes) follow the same fundamental sequence.

---

## Top-Level Step Flow

Every call to `Engine::step(dt, softening)` (defined in `engine.cpp:199-299`) executes:

```
Engine::step(dt, softening)
│
├── 1. Phase 13 pre-force hooks  ─── apply_phase13_pre_force()
│
├── 2. Integrator dispatch ──────── switch (config_.integrator)
│   ├── Yoshida4: switch (compute_mode) → step_yoshida4_{cpu_bf, cpu_bh, gpu_bf, gpu_bh}
│   └── Leapfrog: switch (compute_mode) → step_{cpu_bf, cpu_bh, gpu_bf, gpu_bh}
│
├── 3. Phase 13 post-force hooks ── apply_phase13_post_force(dt)
│
├── 4. Adaptive timestep update ─── if enabled: dt_next = eta * sqrt(eps / a_max)
│
├── 5. Frame profiling ──────────── record total_frame_ms, gpu_ms, tree_ms
│
├── 6. Deterministic advance ────── if enabled: step_counter++
│
├── 7. Auto-diagnostics ─────────── if enable_diagnostics: compute_energy_snapshot()
│
└── 8. GPU validation ───────────── if enable_gpu_validation: validate_gpu_cpu_parity()
```

---

## Leapfrog KDK Sequence

The **Velocity Verlet** (KDK) integrator is 2nd-order symplectic with O(dt^2) global error.

### CPU Brute-Force (`engine.cpp:741-788`)

```
step_cpu_brute_force(dt, softening):
│
├── 1. Half-Kick    v += 0.5 * dt * old_acc        [using PREVIOUS step's accelerations]
│
├── 2. Drift        x += dt * v                     [positions advance to t + dt]
│
├── 3. Post-position hooks                           [Phase 13, CPU only]
│
├── 4. Forces       compute_cpu_brute_forces()       [O(N^2) pairwise gravity]
│                   3 variants per softening mode:
│                   ├── Global:      single eps^2
│                   ├── PerBodyType: lookup table, pairwise mean
│                   └── Adaptive:    eps_i = scale * cbrt(m_i)
│
├── 5. PN Correction (if enabled)                    [1PN Einstein-Infeld-Hoffmann]
│
├── 6. Collision Detection & Resolution
│      ├── detect: O(N^2) brute-force pair scan
│      ├── resolve: Elastic / Inelastic / Merge
│      └── compact: remove dead bodies after merge
│
├── 7. Half-Kick    v += 0.5 * dt * acc              [using THIS step's accelerations]
│                   (re-read particle count — compaction may have changed it)
│
└── 8. Rotate       old_acc = memcpy(acc)             [prepare for next step's first kick]
```

### CPU Barnes-Hut (`engine.cpp:794-856`)

Same as CPU Brute-Force except:
- Step 4 uses `bh_solver_.compute_forces_with_collisions()` for O(N log N) gravity
  **with simultaneous collision detection** during tree traversal (Phase 14-15)
- Collision detection is "free" — happens at leaf nodes during the same traversal
- If collisions disabled: uses `bh_solver_.compute_forces()` without collision detection

```
step_cpu_barnes_hut(dt, softening):
│
├── 1. Half-Kick
├── 2. Drift
├── 3. Post-position hooks
├── 4. Forces + Collision Detection (unified BH traversal)
│      ├── Build octree from current positions
│      ├── Traverse tree for each body:
│      │   ├── Far node (s^2 < θ^2 * d^2): monopole approximation
│      │   ├── Close node: recurse into children
│      │   └── Leaf node: direct force + collision check (dist < r_i + r_j)
│      └── Thread-local collision pairs merged, deduplicated
├── 5. PN Correction
├── 6. Collision Resolution (pairs already detected in step 4)
├── 7. Half-Kick
└── 8. Rotate
```

### GPU Brute-Force (`engine.cpp:862-882`)

```
step_gpu_brute_force(dt, softening):
│
├── 1. GPU Pipeline Submit
│      ├── Upload SoA arrays: H2D memcpy
│      ├── launch_kick_drift kernel      [Half-Kick + Drift in single kernel]
│      ├── launch_gravity_kernel          [O(N^2) tiled shared-memory gravity]
│      ├── launch_pn_correction (if PN)
│      └── launch_kick_rotate kernel     [Half-Kick + Rotate in single kernel]
│
├── 2. GPU Pipeline Retrieve
│      └── Download: D2H memcpy (pos, vel, acc, old_acc)
│
├── 3. CPU Collision Detection & Resolution
│      (Collisions detected on CPU after GPU download)
│
└── 4. Deterministic Sync (if enabled)
```

### GPU Barnes-Hut (`engine.cpp:888-1039`)

The most complex path. Three sub-variants based on collision mode:

```
step_gpu_barnes_hut(dt, softening):
│
├── 1. Upload       gpu_pool_.upload_all()            [H2D for all 15 SoA arrays]
│
├── 2. Kick+Drift   launch_kick_drift()              [GPU kernel]
│
├── 3. Forces — THREE paths:
│      │
│      ├── [GPU-Resident Merge] (Merge mode + collisions enabled)
│      │   └── gpu_tree_solver_.compute_forces_merge_compact()
│      │       ├── Morton code compute + radix sort
│      │       ├── GPU octree build
│      │       ├── GPU tree traversal (forces + collision detection)
│      │       ├── Collision pair sort (deterministic ordering)
│      │       ├── resolve_merges_kernel (GPU-side mass/momentum conservation)
│      │       ├── compact_kernel (prefix-scan, remove dead bodies)
│      │       └── Returns new_n (particle count after compaction)
│      │
│      ├── [GPU Unified Collisions] (Elastic/Inelastic + collisions enabled)
│      │   └── gpu_tree_solver_.compute_forces_with_collisions()
│      │       ├── Morton + sort + tree build + traversal
│      │       └── Collision pairs downloaded to CPU for resolution
│      │
│      └── [Forces Only] (collisions disabled)
│          └── gpu_tree_solver_.compute_forces()
│              └── Morton + sort + tree build + traversal
│
├── 4. PN Correction  launch_pn_correction()          [GPU kernel, uses new_n]
│
├── 5. Kick+Rotate    launch_kick_rotate()            [GPU kernel, uses new_n]
│
├── 6. Download
│      ├── gpu_pool_.download_state()                 [pos, vel, acc]
│      ├── gpu_pool_.download_old_acc()
│      ├── [GPU-Resident Merge only]: download mass, radius, is_active
│      ├── cudaStreamSynchronize()
│      └── [GPU-Resident Merge only]: particles_.set_count(new_n)
│
├── 7. CPU Collision Resolution (non-merge GPU paths only)
│      ├── [GPU Unified]: resolve downloaded collision pairs
│      └── [Forces Only]: CPU brute-force detect + resolve
│
└── 8. Deterministic Sync
```

---

## Yoshida 4th-Order Integrator

The Yoshida4 integrator is 4th-order symplectic with O(dt^4) global error. It composes
three Leapfrog substeps with Forest-Ruth coefficients:

```
w1 =  1.0 / (2.0 - cbrt(2.0))  ≈  1.3512071919596578
w0 = -cbrt(2.0) / (2.0 - cbrt(2.0)) ≈ -1.7024143839193153

Verify: 2*w1 + w0 = 1.0  (total timestep preserved)
```

### CPU Yoshida (`engine.cpp:1045-1069`)

Each substep is a complete KDK cycle via `cpu_yoshida_substep()`:

```
step_yoshida4_cpu_{brute_force|barnes_hut}(dt, softening):
│
├── Substep 1: cpu_yoshida_substep(w1 * dt, softening, use_bh)
│   └── Half-Kick → Drift → Forces → PN → Collisions → Half-Kick → Rotate
│
├── Substep 2: cpu_yoshida_substep(w0 * dt, softening, use_bh)    [NEGATIVE dt!]
│   └── Half-Kick → Drift → Forces → PN → Collisions → Half-Kick → Rotate
│
└── Substep 3: cpu_yoshida_substep(w1 * dt, softening, use_bh)
    └── Half-Kick → Drift → Forces → PN → Collisions → Half-Kick → Rotate
```

The negative middle substep (w0 ≈ -1.70) temporarily runs the simulation backwards.
This is mathematically correct and produces the 4th-order error cancellation.

Collisions are detected and resolved on **every substep** for CPU paths.

### GPU Yoshida Brute-Force (`engine.cpp:1075-1103`)

Each substep is a complete GPU pipeline submit+retrieve:

```
step_yoshida4_gpu_brute_force(dt, softening):
│
├── Substep 1: gpu_pipeline_.submit_step(w1 * dt) → retrieve
├── Substep 2: gpu_pipeline_.submit_step(w0 * dt) → retrieve
├── Substep 3: gpu_pipeline_.submit_step(w1 * dt) → retrieve
│
└── Collision detection on FINAL state only (not per-substep)
```

### GPU Yoshida Barnes-Hut (`engine.cpp:1109-1200+`)

Single upload, 3 substeps on device, single download:

```
step_yoshida4_gpu_barnes_hut(dt, softening):
│
├── Upload (once)
│
├── Substep 1: kick_drift(w1*dt) → BH forces → PN → kick_rotate(w1*dt)
├── Substep 2: kick_drift(w0*dt) → BH forces → PN → kick_rotate(w0*dt)
├── Substep 3: kick_drift(w1*dt) → BH forces → PN → kick_rotate(w1*dt)
│   └── Collision detection on LAST substep only (GPU tree traversal)
│   └── GPU-resident merge on LAST substep only (if Merge mode)
│
├── Download
│
└── CPU collision resolution (non-merge paths)
```

---

## Collision Pipeline

### Detection Paths

| Compute Mode | Detection Method | Complexity | Integrated with Forces |
|---|---|---|---|
| CPU_BruteForce | Brute-force pair scan | O(N^2) | No (separate pass) |
| CPU_BarnesHut | Leaf-node check during tree traversal | O(N log N) | Yes |
| GPU_BruteForce | CPU brute-force after GPU download | O(N^2) | No |
| GPU_BarnesHut | GPU tree traversal leaf check | O(N log N) | Yes |

### Resolution Flow

```
Collision pairs detected
│
├── Sort pairs by (min(a,b), max(a,b)) for deterministic ordering
│
├── Pre-resolution snapshot: total_mass, total_momentum
│
├── For each pair:
│   ├── Skip if either body deactivated by prior merge
│   │
│   ├── [Elastic]    Impulse along contact normal (e = 1.0)
│   ├── [Inelastic]  Impulse with restitution coefficient (0 ≤ e ≤ 1)
│   └── [Merge]
│       ├── Check merge safeguards (64/frame, 2/body caps)
│       ├── v_merged = (m_a*v_a + m_b*v_b) / (m_a + m_b)
│       ├── pos_merged = (m_a*pos_a + m_b*pos_b) / (m_a + m_b)
│       ├── Survivor = heavier body, victim marked inactive
│       └── Radius: density-preserving or volume-conserving
│
├── Post-resolution check: |mass_error| < 1e-10, |momentum_error| < 1e-10
│
└── Compaction: remove inactive bodies, shift SoA arrays
```

### GPU-Resident Merge (GPU_BarnesHut + Merge Mode)

The entire detect-resolve-compact pipeline runs on GPU with zero CPU round-trips:

```
GPU Stream (single cudaStream_t):
│
├── BH tree traversal detects collision pairs via atomicAdd
├── Device-side pair sort for deterministic merge order
├── resolve_merges_kernel: GPU-side conservation
│   ├── M_survivor = m_a + m_b
│   ├── v_merged = (m_a*v_a + m_b*v_b) / M
│   ├── pos_merged = COM of pair
│   └── Victim marked is_active = 0
├── compact_kernel: prefix-scan removes inactive bodies
└── Returns new particle count
```

---

## Accumulator-Based Update

`Engine::update(frame_time)` (engine.cpp:301-317) uses a fixed-timestep accumulator:

```
update(frame_time):
│
├── effective_dt = adaptive_dt (if enabled) or config.dt
├── steps = timestep_.update(frame_time)    [accumulator, capped at 10/frame]
│
└── for i in 0..steps:
    ├── step(effective_dt, softening)
    └── if adaptive_dt: refresh effective_dt for next sub-step
```

The safety cap of 10 steps per frame prevents spiral-of-death when the simulation
falls behind real time.

---

## Energy Snapshot Routing

```
compute_energy_snapshot():
│
├── if (particle_count > 256 AND compute_mode is BarnesHut):
│   └── energy_tracker_.compute_with_bh()     [O(N log N) PE via tree traversal]
│
└── else:
    └── energy_tracker_.compute()              [O(N^2) pairwise PE]
```

Both paths compute: KE, PE, linear momentum (3D), angular momentum (3D),
center of mass (position + velocity), virial ratio, total mass.

---

## Phase 13 Hooks

Three hook points per step, available on CPU paths only:

```
on_pre_force()      → Called before gravity computation
on_post_position(dt)→ Called after drift, before forces
on_post_force(dt)   → Called after the entire step completes
```

Hook interface: `hooks::Phase13Hooks` (virtual base class).
Registration: `engine.set_phase13_hooks(hooks_ptr)`.
GPU paths do not invoke hooks.

---

## Source File Reference

| File | Lines | Contents |
|------|-------|----------|
| `engine.cpp:65-134` | 70 | `Engine::init()` — subsystem initialization |
| `engine.cpp:137-161` | 25 | `Engine::shutdown()` — resource cleanup |
| `engine.cpp:199-299` | 100 | `Engine::step()` — top-level dispatch |
| `engine.cpp:301-317` | 17 | `Engine::update()` — accumulator loop |
| `engine.cpp:569-662` | 94 | `compute_cpu_brute_forces()` — 3 softening variants |
| `engine.cpp:673-735` | 63 | `cpu_yoshida_substep()` — single KDK substep |
| `engine.cpp:741-788` | 48 | `step_cpu_brute_force()` — Leapfrog CPU BF |
| `engine.cpp:794-856` | 63 | `step_cpu_barnes_hut()` — Leapfrog CPU BH |
| `engine.cpp:862-882` | 21 | `step_gpu_brute_force()` — Leapfrog GPU BF |
| `engine.cpp:888-1039` | 152 | `step_gpu_barnes_hut()` — Leapfrog GPU BH |
| `engine.cpp:1045-1054` | 10 | `step_yoshida4_cpu_brute_force()` |
| `engine.cpp:1060-1069` | 10 | `step_yoshida4_cpu_barnes_hut()` |
| `engine.cpp:1075-1103` | 29 | `step_yoshida4_gpu_brute_force()` |
| `engine.cpp:1109-1200+` | 90+ | `step_yoshida4_gpu_barnes_hut()` |
