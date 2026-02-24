#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::physics {
    class BarnesHutSolver;
    class ParticleSystem;
}

namespace celestial::profile {

/// Diagnostic threshold configuration for conservation checks.
struct DiagnosticThresholds {
    double max_energy_drift = 0.001;          ///< 0.1% relative energy drift
    double max_momentum_drift = 1e-8;         ///< Absolute momentum drift
    double max_angular_momentum_drift = 1e-8; ///< Absolute angular momentum drift
    double max_com_velocity_drift = 1e-10;    ///< COM velocity drift (should be ~0 for isolated)
};

/// Tracks energy drift and conservation diagnostics per frame.
/// Monitors kinetic energy, potential energy, total energy, momentum,
/// angular momentum, center of mass, and virial ratio.
class CELESTIAL_API EnergyTracker {
public:
    struct Snapshot {
        double kinetic_energy = 0.0;
        double potential_energy = 0.0;
        double total_energy = 0.0;
        double momentum_x = 0.0;
        double momentum_y = 0.0;
        double momentum_z = 0.0;
        double momentum_magnitude = 0.0;

        // Phase 13: Angular momentum
        double angular_momentum_x = 0.0;
        double angular_momentum_y = 0.0;
        double angular_momentum_z = 0.0;
        double angular_momentum_magnitude = 0.0;

        // Phase 13: Center of mass
        double com_x = 0.0;
        double com_y = 0.0;
        double com_z = 0.0;
        double com_vel_x = 0.0;
        double com_vel_y = 0.0;
        double com_vel_z = 0.0;

        // Phase 13: Virial and mass
        double virial_ratio = 0.0;  ///< 2*KE / |PE|  (≈1 for virialized system)
        double total_mass = 0.0;

        i64 step_number = 0;
    };

    /// Compute energy and momentum from SoA arrays (CPU path).
    Snapshot compute(
        const double* pos_x, const double* pos_y, const double* pos_z,
        const double* vel_x, const double* vel_y, const double* vel_z,
        const double* mass, const u8* is_active,
        i32 count, double softening);

    /// Phase 16-17: Compute energy using Barnes-Hut O(N log N) for PE.
    /// Uses bh_solver.compute_potential() instead of O(N²) pairwise PE.
    Snapshot compute_with_bh(
        const double* pos_x, const double* pos_y, const double* pos_z,
        const double* vel_x, const double* vel_y, const double* vel_z,
        const double* mass, const u8* is_active,
        i32 count, double softening,
        physics::BarnesHutSolver& bh_solver, physics::ParticleSystem& particles);

    /// Record a snapshot. Computes drift relative to initial.
    void record(const Snapshot& snap);

    /// Reset tracking (e.g., on new simulation).
    void reset();

    /// Get the most recent energy drift (relative to initial).
    double energy_drift() const;

    /// Get the most recent momentum drift magnitude.
    double momentum_drift() const;

    /// Get angular momentum drift magnitude (|L_current - L_initial|).
    double angular_momentum_drift() const;

    /// Get COM position drift magnitude from initial.
    double com_position_drift() const;

    /// Get COM velocity drift magnitude from initial.
    double com_velocity_drift() const;

    /// Check all diagnostics against thresholds. Returns true if all pass.
    bool check_diagnostics(const DiagnosticThresholds& thresholds) const;

    /// Get accumulated error (sum of |drift| over all steps).
    double accumulated_error() const { return accumulated_error_; }

    /// Phase 16-17: Rolling average of total energy over the last ROLLING_WINDOW snapshots.
    double rolling_avg_energy() const;

    /// Phase 16-17: Rolling average of energy drift over the last ROLLING_WINDOW snapshots.
    double rolling_avg_drift() const;

    /// Get the initial and current snapshots.
    const Snapshot& initial() const { return initial_; }
    const Snapshot& current() const { return current_; }

    bool has_initial() const { return has_initial_; }

private:
    Snapshot initial_{};
    Snapshot current_{};
    bool has_initial_ = false;
    double accumulated_error_ = 0.0;

    // Phase 16-17: Rolling window for energy and drift
    static constexpr int ROLLING_WINDOW = 300;
    double energy_history_[ROLLING_WINDOW]{};
    double drift_history_[ROLLING_WINDOW]{};
    int history_count_ = 0;
    int history_index_ = 0;
};

} // namespace celestial::profile
