#pragma once

#include <cstdint>
#include <celestial/core/platform.hpp>

namespace celestial::physics {

/// Octree node for Barnes-Hut gravity computation.
/// Stored in flat pool array. Matches C# OctreeNode semantics.
struct OctreeNode {
    // Spatial bounds (axis-aligned bounding cube)
    double center_x = 0.0;
    double center_y = 0.0;
    double center_z = 0.0;
    double half_size = 0.0;

    // Aggregated mass properties (center of mass)
    double total_mass = 0.0;
    double com_x = 0.0;
    double com_y = 0.0;
    double com_z = 0.0;

    // Quadrupole tensor (5 independent components, traceless symmetric)
    // Q_xx, Q_xy, Q_xz, Q_yy, Q_yz  (Q_zz = -Q_xx - Q_yy)
    double q_xx = 0.0;
    double q_xy = 0.0;
    double q_xz = 0.0;
    double q_yy = 0.0;
    double q_yz = 0.0;

    // Body reference (leaf only), -1 for internal/empty
    int32_t body_index = -1;

    // Child indices into pool array, -1 = no child
    int32_t children[8] = {-1, -1, -1, -1, -1, -1, -1, -1};

    // Flags
    uint8_t is_leaf = 0;

    /// Initialize this node with given center and half-size.
    void init(double cx, double cy, double cz, double hs) {
        center_x = cx; center_y = cy; center_z = cz;
        half_size = hs;
        total_mass = 0.0;
        com_x = 0.0; com_y = 0.0; com_z = 0.0;
        q_xx = 0.0; q_xy = 0.0; q_xz = 0.0; q_yy = 0.0; q_yz = 0.0;
        body_index = -1;
        for (int i = 0; i < 8; i++) children[i] = -1;
        is_leaf = 0;
    }

    /// Determine which octant a point belongs to (0-7).
    CELESTIAL_HOST_DEVICE int octant_for(double px, double py, double pz) const {
        int octant = 0;
        if (px >= center_x) octant |= 1;
        if (py >= center_y) octant |= 2;
        if (pz >= center_z) octant |= 4;
        return octant;
    }

    /// Compute child center for given octant.
    CELESTIAL_HOST_DEVICE void child_center(int octant, double& cx, double& cy, double& cz) const {
        double quarter = half_size * 0.5;
        cx = center_x + ((octant & 1) ? quarter : -quarter);
        cy = center_y + ((octant & 2) ? quarter : -quarter);
        cz = center_z + ((octant & 4) ? quarter : -quarter);
    }

    int32_t get_child(int octant) const { return children[octant]; }
    void set_child(int octant, int32_t idx) { children[octant] = idx; }
};

} // namespace celestial::physics
