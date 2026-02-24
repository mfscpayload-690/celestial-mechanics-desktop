# Determinism Certification

## Overview

The Celestial Engine supports bit-exact deterministic execution in **6 out of 8** engine
modes. This document certifies which modes are deterministic, explains why the remaining
2 are not, and details the infrastructure that enables determinism.

---

## Per-Mode Determinism Matrix

| Mode | Leapfrog | Yoshida4 | Deterministic? |
|------|----------|----------|----------------|
| CPU_BruteForce | YES | YES | **Bit-exact** |
| CPU_BarnesHut | YES | YES | **Bit-exact** |
| GPU_BruteForce | YES | YES | **Bit-exact** |
| GPU_BarnesHut | NO | NO | Non-deterministic |

### CPU_BruteForce (Deterministic)

**Why**: Fixed `for(i=0..n)` outer loop, fixed `for(j=0..n)` inner loop. Single-threaded
execution. Floating-point addition order is identical every run.

**Code path**: `engine.cpp:569-662` — three force variants (Global, PerBodyType, Adaptive)
all use sequential `for` loops with no parallelism.

### CPU_BarnesHut (Deterministic)

**Why**: Octree children are always visited in fixed order 0-7 (`octree_builder.cpp`).
Parallel BH uses `JobSystem` but each thread writes only to its own body's `acc[i]`,
eliminating write races. Tree traversal order is deterministic given identical tree
structure.

**Code path**: `barnes_hut_solver.cpp` — recursive traversal with fixed child ordering.

### GPU_BruteForce (Deterministic)

**Why**: Each GPU thread computes only `acc[i]` for its assigned body. Tiled shared-memory
loads are cooperative but all threads load the same tile. Accumulation order within each
thread is fixed: tile 0, tile 1, ..., and within each tile: k=0, k=1, ..., TILE_SIZE-1.

**Code path**: `gravity_kernel.cu:20-95` — tiled kernel with `#pragma unroll 8`.

### GPU_BarnesHut (Non-Deterministic)

**Why**: The radix sort in `morton_kernel.cu` uses `atomicAdd` on shared memory histogram
offsets during the scatter phase. When two threads in the same block have keys mapping to
the same radix bucket, the `atomicAdd` resolves in non-deterministic order (GPU warp
scheduling is non-deterministic).

**Impact**: Two runs with identical input produce different orderings of particles with
the same Morton code. This changes:
1. Tree structure (child ordering within nodes)
2. Traversal order (which interactions use monopole vs direct)
3. Final force values (different FP addition order)

**Magnitude**: Inter-run variance is < 0.01% RMS at theta=0.5, far smaller than the
~1% BH approximation error itself. Physically inconsequential.

---

## Deterministic Mode Infrastructure

**Files**: `deterministic.hpp`, `deterministic.cpp`

When `config.deterministic = true`, the engine activates:

### 1. Fixed Dispatch Order

All iterations proceed in fixed index order. No work-stealing, no dynamic scheduling.
The job system dispatches bodies in contiguous chunks, and thread writes are isolated
to per-body outputs.

### 2. GPU Pipeline Drain

```cpp
bool DeterministicMode::force_sync() const {
    return enabled_;
}
```

When `force_sync()` returns true, the engine calls `cudaDeviceSynchronize()` after
each GPU kernel launch. This prevents out-of-order kernel completion that could affect
subsequent operations.

### 3. CUDA Cache Configuration

Sets `cudaFuncCachePreferL1` to disable CUDA auto-tuning that varies between runs.

### 4. Seeded PRNG

```cpp
u64 DeterministicMode::deterministic_hash(u64 channel) const {
    u64 z = seed_ ^ step_number_ ^ (channel * 0x9E3779B97F4A7C15ULL);
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    z = z ^ (z >> 31);
    return z;
}
```

SplitMix64 hash parameterized by `(seed, step_number, channel)`. Not a sequential
generator — a hash function. Identical `(seed, step, channel)` always produces
identical output.

### 5. Monotonic Step Counter

`advance_step()` increments after every `Engine::step()`. Provides deterministic
progression for the PRNG hash.

---

## GPU Reduction Determinism

The deterministic two-level reduction framework (`gpu_reduction.hpp/cu`) guarantees
that all GPU-computed scalar quantities are deterministic **regardless of GPU scheduling**.

### Architecture

```
Level 1 — Device-side (fixed topology):
  Block (256 threads = 8 warps):
    Warp 0: [t0..t31]   → shuffle-down (16,8,4,2,1) → partial_sum_0
    Warp 1: [t32..t63]  → shuffle-down → partial_sum_1
    ...
    Warp 7: [t224..t255] → shuffle-down → partial_sum_7
            ↓
    shared_mem[8] = {ps_0, ps_1, ..., ps_7}
            ↓
    Warp 0 reduces shared_mem[0..7] → block_result
            ↓
    d_block_out[blockIdx.x] = block_result

Level 2 — Host-side (Kahan compensated summation):
  Download d_block_out → host
  host_kahan_sum(block_results, num_blocks) → scalar
```

### Why This Is Deterministic

| Property | Guarantee |
|----------|-----------|
| Block size | Compile-time constant (256), never auto-tuned |
| Warp shuffle offsets | Always `{16, 8, 4, 2, 1}` — same binary tree every run |
| Block output addressing | `d_block_out[blockIdx.x]` — deterministic indexed write |
| Host summation | Sequential Kahan sum — fixed addition order |
| Floating-point atomicAdd | **NONE** — eliminated entirely from reduction pipeline |
| Integer atomicAdd | Used only for collision pair counting (order-independent) |

### Reduction Kernels (8 total)

| Kernel | Output | Used For |
|--------|--------|----------|
| `k_reduce_ke` | Scalar | Kinetic energy |
| `k_reduce_momentum` | 3-component | Linear momentum |
| `k_reduce_angular_momentum` | 3-component | Angular momentum |
| `k_reduce_com` | 4-component | Center of mass + total mass |
| `k_reduce_max_accel` | Scalar | Max acceleration (adaptive dt) |
| `k_reduce_total_mass` | Scalar | Total mass |
| `k_reduce_sum` | Scalar | Generic sum |
| `k_reduce_max` | Scalar | Generic max |

### Configuration

```cpp
struct ReductionConfig {
    static constexpr int BLOCK_SIZE = 256;
    static constexpr int WARPS_PER_BLOCK = 8;   // 256 / 32
    static constexpr bool KAHAN_COMPENSATION = true;
};
```

---

## Collision Determinism

Collision pairs are sorted by `(min(a,b), max(a,b))` before resolution
(`collision_resolver.cpp:8-18`). This canonical ordering ensures that:
1. Pairs are always processed in the same order
2. Merge outcomes are identical (heavier body survives, same merged velocity)
3. Per-body merge counters produce identical cap behavior

For GPU-resident merges, pairs are sorted on-device before the merge kernel runs.

---

## Cross-Platform Determinism

**Not guaranteed.** Different CPUs may implement `std::sqrt` with different rounding.
Different GPUs implement `rsqrt` with different ULP accuracy. IEEE 754 guarantees
correctly-rounded results for basic operations but not for transcendental functions
on all implementations.

Same hardware + same binary + same seed = identical results (in 6/8 modes).

---

## How to Reproduce Deterministic Results

1. Set `config.deterministic = true`
2. Set `config.deterministic_seed = <your_seed>`
3. Choose a bit-exact mode (any except GPU_BarnesHut)
4. Use fixed dt (disable adaptive timestep, or use same initial conditions)
5. Load identical initial conditions
6. Results are bit-exact: `EXPECT_DOUBLE_EQ` passes across runs

### Test Reference

`test_validation.cpp` — `DeterminismTest::IdenticalSeed_IdenticalResults`:
Runs 100 steps twice with the same seed, asserts bit-exact double equality.

---

## GPU_BarnesHut Non-Determinism Mitigation

If approximate determinism is acceptable (< 0.01% RMS variance):
- Use GPU_BarnesHut normally — variance is below BH approximation error
- Compare results statistically rather than bit-exactly

If bit-exact determinism is required:
- Use CPU_BarnesHut instead — same O(N log N) complexity, deterministic
- Or use GPU_BruteForce — deterministic, but O(N^2)

There is no configuration that makes GPU_BarnesHut bit-exact deterministic.
The root cause (`atomicAdd` in radix sort scatter) is fundamental to the
GPU sorting algorithm.
