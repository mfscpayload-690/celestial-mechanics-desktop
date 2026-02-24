#include <celestial/physics/collision_resolver.hpp>
#include <celestial/physics/density_model.hpp>
#include <cmath>
#include <algorithm>

namespace celestial::physics {

void CollisionResolver::sort_pairs(std::vector<CollisionPair>& pairs) {
    std::sort(pairs.begin(), pairs.end(),
        [](const CollisionPair& x, const CollisionPair& y) {
            i32 x_lo = (x.a < x.b) ? x.a : x.b;
            i32 x_hi = (x.a < x.b) ? x.b : x.a;
            i32 y_lo = (y.a < y.b) ? y.a : y.b;
            i32 y_hi = (y.a < y.b) ? y.b : y.a;
            if (x_lo != y_lo) return x_lo < y_lo;
            return x_hi < y_hi;
        });
}

void CollisionResolver::resolve(ParticleSystem& particles,
                                 std::vector<CollisionPair>& pairs) {
    if (config_.mode == CollisionMode::Ignore || pairs.empty()) {
        last_collision_count_ = 0;
        last_merge_count_ = 0;
        last_check_ = {};
        return;
    }

    // Sort for deterministic ordering
    sort_pairs(pairs);

    // Pre-resolution conservation quantities
    double total_mass_pre = 0.0;
    double px_pre = 0.0, py_pre = 0.0, pz_pre = 0.0;
    i32 n = particles.count;
    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;
        double mi = particles.mass[i];
        total_mass_pre += mi;
        px_pre += mi * particles.vel_x[i];
        py_pre += mi * particles.vel_y[i];
        pz_pre += mi * particles.vel_z[i];
    }

    // Phase 14-15: Initialize merge safeguard tracking
    total_merges_this_frame_ = 0;
    if (config_.mode == CollisionMode::Merge) {
        merge_counts_.assign(static_cast<size_t>(n), 0);
    }

    // Resolve each pair
    last_collision_count_ = static_cast<i32>(pairs.size());

    for (auto& pair : pairs) {
        // Skip if either body was deactivated by a prior merge this step
        if (!particles.is_active[pair.a] || !particles.is_active[pair.b]) continue;

        switch (config_.mode) {
            case CollisionMode::Elastic:
                resolve_elastic(particles, pair);
                break;
            case CollisionMode::Inelastic:
                resolve_inelastic(particles, pair);
                break;
            case CollisionMode::Merge: {
                // Phase 14-15: Check merge safeguards
                if (total_merges_this_frame_ >= config_.max_merges_per_frame) break;
                i32 a = pair.a, b = pair.b;
                if (merge_counts_[a] >= config_.max_merges_per_body ||
                    merge_counts_[b] >= config_.max_merges_per_body) break;

                resolve_merge(particles, pair);

                // Track merge counts
                i32 survivor = (particles.mass[a] > 0 && particles.is_active[a]) ? a : b;
                merge_counts_[survivor]++;
                total_merges_this_frame_++;
                break;
            }
            default:
                break;
        }
    }

    last_merge_count_ = total_merges_this_frame_;

    // Post-resolution conservation check
    double total_mass_post = 0.0;
    double px_post = 0.0, py_post = 0.0, pz_post = 0.0;
    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;
        double mi = particles.mass[i];
        total_mass_post += mi;
        px_post += mi * particles.vel_x[i];
        py_post += mi * particles.vel_y[i];
        pz_post += mi * particles.vel_z[i];
    }

    last_check_.mass_error = std::abs(total_mass_post - total_mass_pre);
    double dpx = px_post - px_pre;
    double dpy = py_post - py_pre;
    double dpz = pz_post - pz_pre;
    last_check_.momentum_error = std::sqrt(dpx * dpx + dpy * dpy + dpz * dpz);
    last_check_.passed = (last_check_.mass_error < 1e-10) &&
                          (last_check_.momentum_error < 1e-10);
}

void CollisionResolver::resolve_elastic(ParticleSystem& p,
                                         const CollisionPair& pair) {
    i32 a = pair.a, b = pair.b;
    double dist = pair.distance;
    if (dist < 1e-30) return;  // Degenerate — use softening to avoid division by zero

    // Collision normal: n = (pos_b - pos_a) / |pos_b - pos_a|
    double dx = p.pos_x[b] - p.pos_x[a];
    double dy = p.pos_y[b] - p.pos_y[a];
    double dz = p.pos_z[b] - p.pos_z[a];
    double inv_dist = 1.0 / dist;
    double nx = dx * inv_dist;
    double ny = dy * inv_dist;
    double nz = dz * inv_dist;

    // Relative velocity of a w.r.t. b along normal
    double dvx = p.vel_x[a] - p.vel_x[b];
    double dvy = p.vel_y[a] - p.vel_y[b];
    double dvz = p.vel_z[a] - p.vel_z[b];
    double v_rel_n = dvx * nx + dvy * ny + dvz * nz;

    // Skip if bodies are separating
    if (v_rel_n > 0.0) return;

    double ma = p.mass[a];
    double mb = p.mass[b];
    double m_sum = ma + mb;
    if (m_sum < 1e-30) return;

    // Elastic impulse: perfectly elastic (e=1)
    double factor_a = 2.0 * mb / m_sum * v_rel_n;
    double factor_b = 2.0 * ma / m_sum * v_rel_n;

    p.vel_x[a] -= factor_a * nx;
    p.vel_y[a] -= factor_a * ny;
    p.vel_z[a] -= factor_a * nz;

    p.vel_x[b] += factor_b * nx;
    p.vel_y[b] += factor_b * ny;
    p.vel_z[b] += factor_b * nz;
}

void CollisionResolver::resolve_inelastic(ParticleSystem& p,
                                            const CollisionPair& pair) {
    i32 a = pair.a, b = pair.b;
    double dist = pair.distance;
    if (dist < 1e-30) return;

    double dx = p.pos_x[b] - p.pos_x[a];
    double dy = p.pos_y[b] - p.pos_y[a];
    double dz = p.pos_z[b] - p.pos_z[a];
    double inv_dist = 1.0 / dist;
    double nx = dx * inv_dist;
    double ny = dy * inv_dist;
    double nz = dz * inv_dist;

    double dvx = p.vel_x[a] - p.vel_x[b];
    double dvy = p.vel_y[a] - p.vel_y[b];
    double dvz = p.vel_z[a] - p.vel_z[b];
    double v_rel_n = dvx * nx + dvy * ny + dvz * nz;

    if (v_rel_n > 0.0) return;

    double ma = p.mass[a];
    double mb = p.mass[b];
    if (ma + mb < 1e-30) return;

    // Impulse with coefficient of restitution
    double e = config_.restitution;
    double j = -(1.0 + e) * v_rel_n / (1.0 / ma + 1.0 / mb);

    p.vel_x[a] += (j / ma) * nx;
    p.vel_y[a] += (j / ma) * ny;
    p.vel_z[a] += (j / ma) * nz;

    p.vel_x[b] -= (j / mb) * nx;
    p.vel_y[b] -= (j / mb) * ny;
    p.vel_z[b] -= (j / mb) * nz;
}

void CollisionResolver::resolve_merge(ParticleSystem& p,
                                       const CollisionPair& pair) {
    i32 a = pair.a, b = pair.b;
    double ma = p.mass[a];
    double mb = p.mass[b];
    double M = ma + mb;
    if (M < 1e-30) return;

    // Merged velocity: conserve momentum
    double vx_merged = (ma * p.vel_x[a] + mb * p.vel_x[b]) / M;
    double vy_merged = (ma * p.vel_y[a] + mb * p.vel_y[b]) / M;
    double vz_merged = (ma * p.vel_z[a] + mb * p.vel_z[b]) / M;

    // Merged position: mass-weighted average
    double px_merged = (ma * p.pos_x[a] + mb * p.pos_x[b]) / M;
    double py_merged = (ma * p.pos_y[a] + mb * p.pos_y[b]) / M;
    double pz_merged = (ma * p.pos_z[a] + mb * p.pos_z[b]) / M;

    // Survivor = heavier body, victim = lighter body
    i32 survivor = (ma >= mb) ? a : b;
    i32 victim = (ma >= mb) ? b : a;

    // Compute merged radius
    double r_merged;
    if (config_.density_preserving_merge && density_model_ && p.density) {
        // Phase 14-15: Density-preserving merge
        // Use survivor's pre-merge density to compute new radius from combined mass
        double survivor_density = p.density[survivor];
        if (survivor_density < 1e-30) {
            // Fallback: compute from survivor's current mass/radius
            survivor_density = DensityModel::compute_density(
                p.mass[survivor], p.radius[survivor], density_model_->config().min_radius);
        }
        r_merged = DensityModel::compute_radius(M, survivor_density,
                                                 density_model_->config().min_radius);
    } else {
        // Volume-conserving radius (original behavior)
        double ra3 = p.radius[a] * p.radius[a] * p.radius[a];
        double rb3 = p.radius[b] * p.radius[b] * p.radius[b];
        r_merged = std::cbrt(ra3 + rb3);
    }

    p.pos_x[survivor] = px_merged;
    p.pos_y[survivor] = py_merged;
    p.pos_z[survivor] = pz_merged;
    p.vel_x[survivor] = vx_merged;
    p.vel_y[survivor] = vy_merged;
    p.vel_z[survivor] = vz_merged;
    p.mass[survivor] = M;
    p.radius[survivor] = r_merged;

    // Update density for survivor if density array exists
    if (p.density && density_model_) {
        p.density[survivor] = DensityModel::compute_density(
            M, r_merged, density_model_->config().min_radius);
    }

    // Deactivate victim
    p.is_active[victim] = 0;
}

} // namespace celestial::physics
