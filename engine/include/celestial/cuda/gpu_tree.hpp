#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/physics/collision_detector.hpp>
#include <celestial/sim/simulation_config.hpp>
#include <celestial/memory/gpu_pool.hpp>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// Maximum number of octree nodes for GPU tree.
/// Conservatively: ~8 * N for a well-distributed tree.
static constexpr i32 GPU_TREE_MAX_NODES_FACTOR = 10;

/// GPU octree node stored in flat arrays (SoA) for GPU-friendly access.
/// Separate arrays for each field to maximize coalesced access.
struct GpuTreeBuffers {
    // Node spatial bounds
    double* d_node_center_x = nullptr;
    double* d_node_center_y = nullptr;
    double* d_node_center_z = nullptr;
    double* d_node_half_size = nullptr;

    // Center-of-mass and total mass
    double* d_node_mass = nullptr;
    double* d_node_com_x = nullptr;
    double* d_node_com_y = nullptr;
    double* d_node_com_z = nullptr;

    // Child indices (-1 = no child), 8 per node
    i32* d_node_children = nullptr;  // [max_nodes * 8]

    // Node body index (-1 for internal nodes)
    i32* d_node_body = nullptr;

    // Node flags
    u8* d_node_is_leaf = nullptr;

    // Count of allocated nodes
    i32* d_node_count = nullptr;     // [1] — device-side atomic counter

    i32 max_nodes = 0;

    void allocate(i32 max_n);
    void free();
};

/// GPU-accelerated Barnes-Hut solver.
/// Builds octree on GPU using Morton-sorted bodies, then traverses on GPU.
class CELESTIAL_API GpuTreeSolver {
public:
    /// Opening angle parameter theta. Default 0.5.
    double theta = 0.5;

    /// Initialize tree buffers for up to max_bodies particles.
    void init(i32 max_bodies);

    /// Destroy tree buffers.
    void destroy();

    bool is_initialized() const { return initialized_; }

    /// Full GPU Barnes-Hut force computation:
    /// 1. Compute bounding box (reduction)
    /// 2. Generate Morton codes
    /// 3. Radix sort bodies by Morton code
    /// 4. Build octree on GPU
    /// 5. Compute center-of-mass (bottom-up)
    /// 6. Traverse tree for each body (force computation)
    void compute_forces(memory::GpuPool& pool, i32 n, double softening,
                        cudaStream_t stream);

    /// Phase 14-15: GPU Barnes-Hut force computation with collision detection.
    /// Same pipeline as compute_forces() but traversal kernel also detects
    /// collisions at leaf nodes (dist < r_i + r_j) and outputs pairs.
    void compute_forces_with_collisions(
        memory::GpuPool& pool, i32 n, double softening,
        std::vector<physics::CollisionPair>& out_pairs,
        cudaStream_t stream);

    /// Phase 18+19: GPU-resident force + collision + merge + compact pipeline.
    /// No CPU round-trips for collisions. Returns new particle count after compaction.
    /// Collision pairs are detected, sorted, merged, and compacted entirely on GPU.
    i32 compute_forces_merge_compact(
        memory::GpuPool& pool, i32 n, double softening,
        const physics::CollisionResolverConfig& collision_config,
        double min_radius, bool density_preserving,
        cudaStream_t stream);

    /// Phase 18+19: Compute GPU-resident energy snapshot.
    /// Uses the last-built tree for PE computation.
    /// Returns KE, PE, momentum, angular_momentum, COM, total_mass.
    struct GpuEnergySnapshot {
        double kinetic_energy = 0.0;
        double potential_energy = 0.0;
        double total_energy = 0.0;
        double momentum_x = 0.0, momentum_y = 0.0, momentum_z = 0.0;
        double angular_momentum_x = 0.0, angular_momentum_y = 0.0, angular_momentum_z = 0.0;
        double com_x = 0.0, com_y = 0.0, com_z = 0.0;
        double total_mass = 0.0;
    };

    GpuEnergySnapshot compute_gpu_energy(
        memory::GpuPool& pool, i32 n, double softening,
        cudaStream_t stream);

    /// Access the internal tree buffers (for PE computation by external code).
    const GpuTreeBuffers& tree_buffers() const { return tree_; }

    // Timing (updated after each compute_forces call)
    double last_sort_time_ms = 0.0;
    double last_build_time_ms = 0.0;
    double last_traverse_time_ms = 0.0;
    double last_total_time_ms = 0.0;

private:
    GpuTreeBuffers tree_;
    bool initialized_ = false;

    // Sort buffers (alternating for radix sort)
    u64* d_morton_alt_ = nullptr;
    i32* d_indices_alt_ = nullptr;
    i32 sort_capacity_ = 0;

    // Pre-allocated radix sort workspace (no device malloc during simulation)
    i32* d_sort_histograms_ = nullptr;
    i32* d_sort_offsets_ = nullptr;
    i32 sort_grid_capacity_ = 0;  // grid dim for which workspace was allocated

    // Phase 14-15: Collision output buffers (pre-allocated)
    i32* d_pair_a_ = nullptr;       // collision pair body A indices
    i32* d_pair_b_ = nullptr;       // collision pair body B indices
    double* d_pair_dist_ = nullptr; // collision pair distances
    i32* d_pair_count_ = nullptr;   // [1] atomic counter on device
    i32 max_pairs_ = 0;            // capacity of collision buffers

    void ensure_sort_workspace(i32 n);

    /// Phase 18+19: Internal collision detection without host-side download.
    void compute_forces_with_collisions_internal(
        memory::GpuPool& pool, i32 n, double softening,
        cudaStream_t stream);
};

// ── Kernel launch functions ──

/// Build octree from Morton-sorted bodies.
/// Hierarchical approach: assign bodies to leaves, then build internal nodes.
void launch_tree_build(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_mass, const u8* d_is_active,
    const i32* d_sorted_indices, i32 n,
    GpuTreeBuffers& tree,
    double bbox_cx, double bbox_cy, double bbox_cz, double bbox_half_size,
    cudaStream_t stream);

/// Compute center-of-mass for all internal nodes (bottom-up pass).
void launch_tree_com(
    GpuTreeBuffers& tree, i32 n_nodes,
    cudaStream_t stream);

/// Barnes-Hut tree traversal: compute gravitational acceleration for each body.
void launch_tree_traverse(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    const GpuTreeBuffers& tree,
    i32 n, double softening, double theta,
    cudaStream_t stream);

/// Phase 14-15: Barnes-Hut tree traversal with collision detection at leaf nodes.
/// Outputs collision pairs (body_a < body_b) via atomic counter.
void launch_tree_traverse_collide(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_radius, const u8* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    const GpuTreeBuffers& tree,
    i32 n, double softening, double theta,
    i32* d_pair_a, i32* d_pair_b, double* d_pair_dist,
    i32* d_pair_count, i32 max_pairs,
    cudaStream_t stream);

/// Compute bounding box using GPU reduction.
/// Returns host-side bounding box values.
void launch_bbox_reduction(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active, i32 n,
    double* d_work_min, double* d_work_max,
    double& out_min_x, double& out_min_y, double& out_min_z,
    double& out_max_x, double& out_max_y, double& out_max_z,
    cudaStream_t stream);

} // namespace celestial::cuda
