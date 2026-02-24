#pragma once

#include <celestial/physics/octree_pool.hpp>
#include <celestial/physics/octree_builder.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/physics/collision_detector.hpp>
#include <celestial/core/platform.hpp>
#include <vector>

namespace celestial::job { class JobSystem; }

namespace celestial::physics {

/// Barnes-Hut O(n log n) force computation on CPU.
/// Port of C# BarnesHutBackend with parallel traversal via job system.
class CELESTIAL_API BarnesHutSolver {
public:
    /// Opening angle parameter theta. Default 0.5.
    double theta = 0.5;

    /// Enable parallel traversal via job system.
    bool use_parallel = false;

    /// Enable quadrupole corrections (higher accuracy, slightly slower).
    bool use_quadrupole = false;

    /// Minimum node half-size for subdivision.
    double min_node_size = 1e-12;

    /// Compute gravitational forces for all particles.
    /// Fills acc_x/y/z in the particle system.
    void compute_forces(ParticleSystem& particles, double softening);

    /// Phase 14-15: Compute forces AND detect collisions during tree traversal.
    /// Collisions detected at leaf nodes when dist < r_i + r_j.
    /// Single O(N log N) pass instead of separate gravity + O(N²) collision.
    void compute_forces_with_collisions(
        ParticleSystem& particles, double softening,
        std::vector<CollisionPair>& out_pairs);

    /// Phase 16-17: Compute total gravitational potential energy via BH traversal.
    /// O(N log N) instead of O(N²). Returns total PE (negative for bound systems).
    /// Builds the tree internally, then traverses for each body accumulating φ_i.
    /// PE = 0.5 * Σ(m_i * φ_i) to correct for double-counting.
    double compute_potential(ParticleSystem& particles, double softening);

    // Timing (updated after each compute_forces call)
    double last_build_time_ms = 0.0;
    double last_traversal_time_ms = 0.0;
    double last_total_time_ms = 0.0;

private:
    OctreePool pool_;
    OctreeBuilder builder_;

    /// Compute force on a single body from the tree (recursive traversal).
    static void compute_force_on_body(
        const OctreeNode* nodes, i32 node_idx,
        i32 body_idx, double bx, double by, double bz,
        double eps2, double theta2,
        double& axi, double& ayi, double& azi);

    /// Phase 14-15: Compute force + detect collisions at leaf nodes.
    static void compute_force_on_body_with_collisions(
        const OctreeNode* nodes, i32 node_idx,
        i32 body_idx, double bx, double by, double bz,
        double br,
        double eps2, double theta2,
        const ParticleSystem& particles,
        double& axi, double& ayi, double& azi,
        std::vector<CollisionPair>& thread_pairs);

    /// Traverse a range of bodies (for parallel execution).
    void traverse_range(const OctreeNode* nodes, i32 root,
                        ParticleSystem& particles,
                        double eps2, double theta2,
                        i32 start, i32 end);

    /// Phase 14-15: Traverse range with collision detection.
    void traverse_range_with_collisions(
        const OctreeNode* nodes, i32 root,
        ParticleSystem& particles,
        double eps2, double theta2,
        i32 start, i32 end,
        std::vector<CollisionPair>& thread_pairs);

    /// Phase 16-17: Compute gravitational potential on a single body from the tree.
    /// Returns φ_i = Σ(-M_node / dist) for all interactions (leaf or monopole).
    static double compute_potential_on_body(
        const OctreeNode* nodes, i32 node_idx,
        i32 body_idx, double bx, double by, double bz,
        double eps2, double theta2);
};

} // namespace celestial::physics
