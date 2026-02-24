#include <celestial/profile/energy_tracker.hpp>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/physics/particle_system.hpp>
#include <cmath>

namespace celestial::profile {

EnergyTracker::Snapshot EnergyTracker::compute(
    const double* pos_x, const double* pos_y, const double* pos_z,
    const double* vel_x, const double* vel_y, const double* vel_z,
    const double* mass, const u8* is_active,
    i32 count, double softening)
{
    Snapshot snap{};
    double eps2 = softening * softening;

    // Single pass: kinetic energy, momentum, angular momentum, COM accumulators
    double total_mass = 0.0;
    double com_mx = 0.0, com_my = 0.0, com_mz = 0.0;
    double com_mvx = 0.0, com_mvy = 0.0, com_mvz = 0.0;

    for (i32 i = 0; i < count; i++) {
        if (!is_active[i]) continue;
        double mi = mass[i];
        double vxi = vel_x[i], vyi = vel_y[i], vzi = vel_z[i];
        double pxi = pos_x[i], pyi = pos_y[i], pzi = pos_z[i];

        double v2 = vxi * vxi + vyi * vyi + vzi * vzi;
        snap.kinetic_energy += 0.5 * mi * v2;

        // Linear momentum
        snap.momentum_x += mi * vxi;
        snap.momentum_y += mi * vyi;
        snap.momentum_z += mi * vzi;

        // Angular momentum: L = m * (r x v)
        snap.angular_momentum_x += mi * (pyi * vzi - pzi * vyi);
        snap.angular_momentum_y += mi * (pzi * vxi - pxi * vzi);
        snap.angular_momentum_z += mi * (pxi * vyi - pyi * vxi);

        // COM accumulators
        total_mass += mi;
        com_mx += mi * pxi;
        com_my += mi * pyi;
        com_mz += mi * pzi;
        com_mvx += mi * vxi;
        com_mvy += mi * vyi;
        com_mvz += mi * vzi;
    }

    // Potential energy (pairwise, O(n^2))
    for (i32 i = 0; i < count; i++) {
        if (!is_active[i]) continue;
        for (i32 j = i + 1; j < count; j++) {
            if (!is_active[j]) continue;
            double dx = pos_x[i] - pos_x[j];
            double dy = pos_y[i] - pos_y[j];
            double dz = pos_z[i] - pos_z[j];
            double dist2 = dx * dx + dy * dy + dz * dz + eps2;
            double dist = std::sqrt(dist2);
            snap.potential_energy -= mass[i] * mass[j] / dist;
        }
    }

    snap.total_energy = snap.kinetic_energy + snap.potential_energy;
    snap.momentum_magnitude = std::sqrt(
        snap.momentum_x * snap.momentum_x +
        snap.momentum_y * snap.momentum_y +
        snap.momentum_z * snap.momentum_z);

    // Angular momentum magnitude
    snap.angular_momentum_magnitude = std::sqrt(
        snap.angular_momentum_x * snap.angular_momentum_x +
        snap.angular_momentum_y * snap.angular_momentum_y +
        snap.angular_momentum_z * snap.angular_momentum_z);

    // Center of mass
    snap.total_mass = total_mass;
    if (total_mass > 0.0) {
        snap.com_x = com_mx / total_mass;
        snap.com_y = com_my / total_mass;
        snap.com_z = com_mz / total_mass;
        snap.com_vel_x = com_mvx / total_mass;
        snap.com_vel_y = com_mvy / total_mass;
        snap.com_vel_z = com_mvz / total_mass;
    }

    // Virial ratio: 2*KE / |PE|
    double abs_pe = std::abs(snap.potential_energy);
    snap.virial_ratio = (abs_pe > 0.0) ? (2.0 * snap.kinetic_energy / abs_pe) : 0.0;

    return snap;
}

// --------------------------------------------------------------------------
// Phase 16-17: O(N log N) PE via Barnes-Hut
// --------------------------------------------------------------------------

EnergyTracker::Snapshot EnergyTracker::compute_with_bh(
    const double* pos_x, const double* pos_y, const double* pos_z,
    const double* vel_x, const double* vel_y, const double* vel_z,
    const double* mass, const u8* is_active,
    i32 count, double softening,
    physics::BarnesHutSolver& bh_solver, physics::ParticleSystem& particles)
{
    Snapshot snap{};

    // Single pass: kinetic energy, momentum, angular momentum, COM accumulators
    double total_mass = 0.0;
    double com_mx = 0.0, com_my = 0.0, com_mz = 0.0;
    double com_mvx = 0.0, com_mvy = 0.0, com_mvz = 0.0;

    for (i32 i = 0; i < count; i++) {
        if (!is_active[i]) continue;
        double mi = mass[i];
        double vxi = vel_x[i], vyi = vel_y[i], vzi = vel_z[i];
        double pxi = pos_x[i], pyi = pos_y[i], pzi = pos_z[i];

        double v2 = vxi * vxi + vyi * vyi + vzi * vzi;
        snap.kinetic_energy += 0.5 * mi * v2;

        snap.momentum_x += mi * vxi;
        snap.momentum_y += mi * vyi;
        snap.momentum_z += mi * vzi;

        snap.angular_momentum_x += mi * (pyi * vzi - pzi * vyi);
        snap.angular_momentum_y += mi * (pzi * vxi - pxi * vzi);
        snap.angular_momentum_z += mi * (pxi * vyi - pyi * vxi);

        total_mass += mi;
        com_mx += mi * pxi;
        com_my += mi * pyi;
        com_mz += mi * pzi;
        com_mvx += mi * vxi;
        com_mvy += mi * vyi;
        com_mvz += mi * vzi;
    }

    // O(N log N) potential energy via Barnes-Hut traversal
    snap.potential_energy = bh_solver.compute_potential(particles, softening);

    snap.total_energy = snap.kinetic_energy + snap.potential_energy;
    snap.momentum_magnitude = std::sqrt(
        snap.momentum_x * snap.momentum_x +
        snap.momentum_y * snap.momentum_y +
        snap.momentum_z * snap.momentum_z);

    snap.angular_momentum_magnitude = std::sqrt(
        snap.angular_momentum_x * snap.angular_momentum_x +
        snap.angular_momentum_y * snap.angular_momentum_y +
        snap.angular_momentum_z * snap.angular_momentum_z);

    snap.total_mass = total_mass;
    if (total_mass > 0.0) {
        snap.com_x = com_mx / total_mass;
        snap.com_y = com_my / total_mass;
        snap.com_z = com_mz / total_mass;
        snap.com_vel_x = com_mvx / total_mass;
        snap.com_vel_y = com_mvy / total_mass;
        snap.com_vel_z = com_mvz / total_mass;
    }

    double abs_pe = std::abs(snap.potential_energy);
    snap.virial_ratio = (abs_pe > 0.0) ? (2.0 * snap.kinetic_energy / abs_pe) : 0.0;

    return snap;
}

void EnergyTracker::record(const Snapshot& snap) {
    if (!has_initial_) {
        initial_ = snap;
        has_initial_ = true;
    }
    current_ = snap;
    double drift = std::abs(energy_drift());
    accumulated_error_ += drift;

    // Phase 16-17: Push to rolling window
    energy_history_[history_index_] = snap.total_energy;
    drift_history_[history_index_] = drift;
    history_index_ = (history_index_ + 1) % ROLLING_WINDOW;
    if (history_count_ < ROLLING_WINDOW) history_count_++;
}

void EnergyTracker::reset() {
    initial_ = {};
    current_ = {};
    has_initial_ = false;
    accumulated_error_ = 0.0;
    history_count_ = 0;
    history_index_ = 0;
}

double EnergyTracker::energy_drift() const {
    if (!has_initial_ || initial_.total_energy == 0.0) return 0.0;
    return (current_.total_energy - initial_.total_energy) / std::abs(initial_.total_energy);
}

double EnergyTracker::momentum_drift() const {
    if (!has_initial_) return 0.0;
    double dx = current_.momentum_x - initial_.momentum_x;
    double dy = current_.momentum_y - initial_.momentum_y;
    double dz = current_.momentum_z - initial_.momentum_z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

double EnergyTracker::angular_momentum_drift() const {
    if (!has_initial_) return 0.0;
    double dx = current_.angular_momentum_x - initial_.angular_momentum_x;
    double dy = current_.angular_momentum_y - initial_.angular_momentum_y;
    double dz = current_.angular_momentum_z - initial_.angular_momentum_z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

double EnergyTracker::com_position_drift() const {
    if (!has_initial_) return 0.0;
    double dx = current_.com_x - initial_.com_x;
    double dy = current_.com_y - initial_.com_y;
    double dz = current_.com_z - initial_.com_z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

double EnergyTracker::com_velocity_drift() const {
    if (!has_initial_) return 0.0;
    double dx = current_.com_vel_x - initial_.com_vel_x;
    double dy = current_.com_vel_y - initial_.com_vel_y;
    double dz = current_.com_vel_z - initial_.com_vel_z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

bool EnergyTracker::check_diagnostics(const DiagnosticThresholds& thresholds) const {
    if (!has_initial_) return true; // No data yet — nothing to check

    if (std::abs(energy_drift()) > thresholds.max_energy_drift) return false;
    if (momentum_drift() > thresholds.max_momentum_drift) return false;
    if (angular_momentum_drift() > thresholds.max_angular_momentum_drift) return false;
    if (com_velocity_drift() > thresholds.max_com_velocity_drift) return false;

    return true;
}

// --------------------------------------------------------------------------
// Phase 16-17: Rolling averages
// --------------------------------------------------------------------------

double EnergyTracker::rolling_avg_energy() const {
    if (history_count_ == 0) return 0.0;
    double sum = 0.0;
    for (int i = 0; i < history_count_; i++) {
        sum += energy_history_[i];
    }
    return sum / history_count_;
}

double EnergyTracker::rolling_avg_drift() const {
    if (history_count_ == 0) return 0.0;
    double sum = 0.0;
    for (int i = 0; i < history_count_; i++) {
        sum += drift_history_[i];
    }
    return sum / history_count_;
}

} // namespace celestial::profile
