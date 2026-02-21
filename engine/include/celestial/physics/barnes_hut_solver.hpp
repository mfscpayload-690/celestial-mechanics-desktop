#pragma once

#include <celestial/physics/octree_pool.hpp>
#include <celestial/physics/octree_builder.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/core/platform.hpp>

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

    /// Traverse a range of bodies (for parallel execution).
    void traverse_range(const OctreeNode* nodes, i32 root,
                        ParticleSystem& particles,
                        double eps2, double theta2,
                        i32 start, i32 end);
};

} // namespace celestial::physics
