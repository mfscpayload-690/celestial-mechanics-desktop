#pragma once

#include <celestial/physics/collision_detector.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/sim/simulation_config.hpp>
#include <celestial/core/types.hpp>
#include <vector>

namespace celestial::physics {

// Forward declaration
class DensityModel;

/// Resolves detected collision pairs according to the configured mode.
/// All operations maintain mass & momentum conservation.
/// Collision pairs are processed in deterministic order (sorted by min(a,b), then max(a,b)).
class CELESTIAL_API CollisionResolver {
public:
    /// Verification result: mass and momentum conservation check.
    struct ConservationCheck {
        double mass_error = 0.0;
        double momentum_error = 0.0;
        bool passed = true;
    };

    /// Configure resolution mode.
    void configure(const CollisionResolverConfig& cfg) { config_ = cfg; }

    /// Set density model for density-preserving merges (Phase 14-15).
    void set_density_model(const DensityModel* model) { density_model_ = model; }

    /// Resolve all collision pairs. Modifies particle velocities (and possibly mass/active flags).
    /// Pairs must be sorted for deterministic ordering.
    void resolve(ParticleSystem& particles, std::vector<CollisionPair>& pairs);

    /// Get last collision count (from most recent resolve call).
    i32 last_collision_count() const { return last_collision_count_; }

    /// Get number of merges performed in last resolve call.
    i32 last_merge_count() const { return last_merge_count_; }

    /// Get last conservation check result.
    const ConservationCheck& last_check() const { return last_check_; }

    const CollisionResolverConfig& config() const { return config_; }

private:
    CollisionResolverConfig config_{};
    ConservationCheck last_check_{};
    i32 last_collision_count_ = 0;
    i32 last_merge_count_ = 0;

    /// Phase 14-15: Density model for density-preserving merges.
    const DensityModel* density_model_ = nullptr;

    /// Phase 14-15: Per-body merge count tracking (resized per resolve call).
    std::vector<i32> merge_counts_;
    i32 total_merges_this_frame_ = 0;

    /// Sort pairs for deterministic processing order: (min(a,b), max(a,b)).
    static void sort_pairs(std::vector<CollisionPair>& pairs);

    /// Elastic collision: exchange velocity components along collision normal.
    void resolve_elastic(ParticleSystem& p, const CollisionPair& pair);

    /// Inelastic collision: apply coefficient of restitution.
    void resolve_inelastic(ParticleSystem& p, const CollisionPair& pair);

    /// Merge: combine bodies, conserve mass and momentum, deactivate lighter body.
    void resolve_merge(ParticleSystem& p, const CollisionPair& pair);
};

} // namespace celestial::physics
