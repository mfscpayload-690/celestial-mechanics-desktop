#include <celestial/physics/collision_detector.hpp>
#include <cmath>

namespace celestial::physics {

void CollisionDetector::detect(const ParticleSystem& particles,
                                std::vector<CollisionPair>& out_pairs) {
    out_pairs.clear();
    i32 n = particles.count;

    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;
        if (!particles.is_collidable[i]) continue;

        for (i32 j = i + 1; j < n; j++) {
            if (!particles.is_active[j]) continue;
            if (!particles.is_collidable[j]) continue;

            double dx = particles.pos_x[i] - particles.pos_x[j];
            double dy = particles.pos_y[i] - particles.pos_y[j];
            double dz = particles.pos_z[i] - particles.pos_z[j];
            double dist2 = dx * dx + dy * dy + dz * dz;

            double sum_r = particles.radius[i] + particles.radius[j];
            if (dist2 < sum_r * sum_r) {
                out_pairs.push_back({i, j, std::sqrt(dist2)});
            }
        }
    }
}

void CollisionDetector::reset() {
    // No cached state in brute-force detector
}

} // namespace celestial::physics
