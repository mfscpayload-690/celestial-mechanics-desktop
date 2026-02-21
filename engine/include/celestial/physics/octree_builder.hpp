#pragma once

#include <celestial/physics/octree_pool.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::physics {

/// Builds and maintains the Barnes-Hut octree from particle data.
/// Port of C# BarnesHutBackend tree construction.
class CELESTIAL_API OctreeBuilder {
public:
    /// Build the octree from the particle system.
    /// Returns the root node index.
    i32 build(OctreePool& pool, const ParticleSystem& particles);

    /// Minimum node half-size below which subdivision stops.
    double min_node_size = 1e-12;

private:
    /// Insert a body into the tree at the given node.
    void insert_body(OctreePool& pool, i32 node_idx, i32 body_idx,
                     double bx, double by, double bz, double bm);

    /// Insert into the correct child octant, allocating if needed.
    void insert_into_child(OctreePool& pool, i32 parent_idx, i32 body_idx,
                           double bx, double by, double bz, double bm);

    /// Update node aggregate mass/COM after insertion.
    static void update_mass(OctreeNode& node, double bm, double bx, double by, double bz);
};

} // namespace celestial::physics
