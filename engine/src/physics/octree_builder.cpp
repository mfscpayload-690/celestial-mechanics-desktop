#include <celestial/physics/octree_builder.hpp>
#include <cmath>
#include <algorithm>
#include <cfloat>

namespace celestial::physics {

i32 OctreeBuilder::build(OctreePool& pool, const ParticleSystem& particles) {
    i32 n = particles.count;
    if (n == 0) return -1;

    pool.reset();
    pool.ensure_capacity(4 * n + 64);

    // Step 1: Compute bounding box
    double min_x = DBL_MAX, min_y = DBL_MAX, min_z = DBL_MAX;
    double max_x = -DBL_MAX, max_y = -DBL_MAX, max_z = -DBL_MAX;

    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;
        double px = particles.pos_x[i];
        double py = particles.pos_y[i];
        double pz = particles.pos_z[i];
        if (px < min_x) min_x = px; if (px > max_x) max_x = px;
        if (py < min_y) min_y = py; if (py > max_y) max_y = py;
        if (pz < min_z) min_z = pz; if (pz > max_z) max_z = pz;
    }

    // Make it a cube with margin
    double cx = (min_x + max_x) * 0.5;
    double cy = (min_y + max_y) * 0.5;
    double cz = (min_z + max_z) * 0.5;
    double half_size = std::max({max_x - min_x, max_y - min_y, max_z - min_z}) * 0.5;
    half_size = std::max(half_size, 1e-10);
    half_size *= 1.001; // Tiny margin

    // Step 2: Create root and insert all bodies
    i32 root = pool.allocate(cx, cy, cz, half_size);

    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;
        insert_body(pool, root, i,
                    particles.pos_x[i], particles.pos_y[i], particles.pos_z[i],
                    particles.mass[i]);
    }

    return root;
}

void OctreeBuilder::insert_body(OctreePool& pool, i32 node_idx, i32 body_idx,
                                 double bx, double by, double bz, double bm) {
    OctreeNode& node = pool[node_idx];

    // Case 1: Empty node — make it a leaf
    if (node.total_mass == 0.0 && node.body_index == -1) {
        node.body_index = body_idx;
        node.is_leaf = 1;
        node.total_mass = bm;
        node.com_x = bx;
        node.com_y = by;
        node.com_z = bz;
        return;
    }

    // Case 2: Node is a leaf — must subdivide
    if (node.is_leaf) {
        if (node.half_size * 0.5 < min_node_size) {
            // Bodies are effectively coincident — aggregate
            double total = node.total_mass + bm;
            node.com_x = (node.com_x * node.total_mass + bx * bm) / total;
            node.com_y = (node.com_y * node.total_mass + by * bm) / total;
            node.com_z = (node.com_z * node.total_mass + bz * bm) / total;
            node.total_mass = total;
            return;
        }

        // Save existing body data
        i32 existing_body = node.body_index;
        double exist_mass = node.total_mass;
        double exist_x = node.com_x;
        double exist_y = node.com_y;
        double exist_z = node.com_z;

        // Convert to internal node
        node.body_index = -1;
        node.is_leaf = 0;
        node.total_mass = 0.0;
        node.com_x = 0.0;
        node.com_y = 0.0;
        node.com_z = 0.0;

        // Re-insert existing body
        insert_into_child(pool, node_idx, existing_body, exist_x, exist_y, exist_z, exist_mass);
        // Insert new body
        insert_into_child(pool, node_idx, body_idx, bx, by, bz, bm);

        // Update aggregate
        OctreeNode& updated = pool[node_idx];
        update_mass(updated, exist_mass, exist_x, exist_y, exist_z);
        update_mass(updated, bm, bx, by, bz);
        return;
    }

    // Case 3: Internal node — recurse into child
    insert_into_child(pool, node_idx, body_idx, bx, by, bz, bm);
    update_mass(pool[node_idx], bm, bx, by, bz);
}

void OctreeBuilder::insert_into_child(OctreePool& pool, i32 parent_idx, i32 body_idx,
                                       double bx, double by, double bz, double bm) {
    OctreeNode& parent = pool[parent_idx];
    int octant = parent.octant_for(bx, by, bz);
    i32 child_idx = parent.get_child(octant);

    if (child_idx == -1) {
        double ccx, ccy, ccz;
        parent.child_center(octant, ccx, ccy, ccz);
        child_idx = pool.allocate(ccx, ccy, ccz, parent.half_size * 0.5);
        // Re-read parent ref after potential pool realloc
        pool[parent_idx].set_child(octant, child_idx);
    }

    insert_body(pool, child_idx, body_idx, bx, by, bz, bm);
}

void OctreeBuilder::update_mass(OctreeNode& node, double bm, double bx, double by, double bz) {
    double new_mass = node.total_mass + bm;
    if (new_mass > 0.0) {
        double inv = 1.0 / new_mass;
        node.com_x = (node.com_x * node.total_mass + bx * bm) * inv;
        node.com_y = (node.com_y * node.total_mass + by * bm) * inv;
        node.com_z = (node.com_z * node.total_mass + bz * bm) * inv;
    }
    node.total_mass = new_mass;
}

} // namespace celestial::physics
