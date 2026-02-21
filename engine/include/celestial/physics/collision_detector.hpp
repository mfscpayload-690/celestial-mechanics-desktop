#pragma once

#include <celestial/physics/particle_system.hpp>
#include <celestial/core/platform.hpp>
#include <vector>

namespace celestial::physics {

/// Collision pair detected by broad-phase.
struct CollisionPair {
    i32 a;
    i32 b;
    double distance;
};

/// Broad-phase sphere-sphere collision detection.
class CELESTIAL_API CollisionDetector {
public:
    /// Detect all colliding pairs. Bodies collide when distance < r_a + r_b.
    void detect(const ParticleSystem& particles, std::vector<CollisionPair>& out_pairs);

    /// Clear cached state.
    void reset();
};

} // namespace celestial::physics
