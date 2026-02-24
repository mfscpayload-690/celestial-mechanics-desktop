#include <gtest/gtest.h>
#include <celestial/sim/engine.hpp>
#include <celestial/profile/energy_tracker.hpp>
#include <celestial/physics/particle_system.hpp>
#include <cmath>
#include <vector>
#include <random>

using namespace celestial::sim;
using namespace celestial::profile;
using namespace celestial::physics;

// ==========================================================================
// Helpers
// ==========================================================================

static void setup_circular_orbit(Engine& engine, double r, double m_central,
                                  double m_orbiter) {
    double v_circ = std::sqrt(m_central / r);

    double px[] = {0.0, r};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, v_circ};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {m_central, m_orbiter};
    double radius[] = {0.01, 0.001};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 2);
}

static void setup_random_cluster(Engine& engine, int n, double box_size,
                                  double mass_range, uint64_t seed) {
    std::mt19937_64 rng(seed);
    std::uniform_real_distribution<double> pos_dist(-box_size / 2.0, box_size / 2.0);
    std::uniform_real_distribution<double> vel_dist(-0.01, 0.01);
    std::uniform_real_distribution<double> mass_dist(0.5, mass_range);

    std::vector<double> px(n), py(n), pz(n);
    std::vector<double> vx(n), vy(n), vz(n);
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<double> mass(n), radius(n);
    std::vector<uint8_t> active(n, 1);

    for (int i = 0; i < n; i++) {
        px[i] = pos_dist(rng);
        py[i] = pos_dist(rng);
        pz[i] = pos_dist(rng);
        vx[i] = vel_dist(rng);
        vy[i] = vel_dist(rng);
        vz[i] = vel_dist(rng);
        mass[i] = mass_dist(rng);
        radius[i] = 0.001 * std::cbrt(mass[i]);
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);
}

// ==========================================================================
// MODULE 7: Barnes-Hut Accuracy Audit
// ==========================================================================

// Compare BH forces (theta=0.5) against brute-force reference for a cluster.
// Reports RMS relative error.
TEST(Phase18_19_BarnesHutAudit, ForceErrorVsBruteForce) {
    constexpr int N = 128;

    // --- Brute-force reference ---
    SimulationConfig bf_config{};
    bf_config.max_particles = N;
    bf_config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    bf_config.softening = 1e-3;
    bf_config.dt = 0.001;

    Engine bf_engine;
    bf_engine.init(bf_config);
    setup_random_cluster(bf_engine, N, 10.0, 5.0, 12345);

    // Compute one step to populate accelerations
    bf_engine.step(0.001, 1e-3);

    std::vector<double> ref_ax(N), ref_ay(N), ref_az(N);
    bf_engine.get_accelerations(ref_ax.data(), ref_ay.data(), ref_az.data(), N);
    bf_engine.shutdown();

    // --- Barnes-Hut ---
    SimulationConfig bh_config{};
    bh_config.max_particles = N;
    bh_config.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    bh_config.softening = 1e-3;
    bh_config.theta = 0.5;
    bh_config.dt = 0.001;

    Engine bh_engine;
    bh_engine.init(bh_config);
    setup_random_cluster(bh_engine, N, 10.0, 5.0, 12345);

    bh_engine.step(0.001, 1e-3);

    std::vector<double> bh_ax(N), bh_ay(N), bh_az(N);
    bh_engine.get_accelerations(bh_ax.data(), bh_ay.data(), bh_az.data(), N);
    bh_engine.shutdown();

    // Compute RMS relative force error
    double sum_err2 = 0.0;
    int count = 0;
    for (int i = 0; i < N; i++) {
        double ref_mag = std::sqrt(ref_ax[i]*ref_ax[i] + ref_ay[i]*ref_ay[i]
                                   + ref_az[i]*ref_az[i]);
        if (ref_mag < 1e-15) continue;

        double dx = bh_ax[i] - ref_ax[i];
        double dy = bh_ay[i] - ref_ay[i];
        double dz = bh_az[i] - ref_az[i];
        double err_mag = std::sqrt(dx*dx + dy*dy + dz*dz);
        double rel_err = err_mag / ref_mag;
        sum_err2 += rel_err * rel_err;
        count++;
    }

    double rms_error = std::sqrt(sum_err2 / std::max(count, 1));

    // BH with theta=0.5 should have < 1% RMS relative force error
    EXPECT_LT(rms_error, 0.01)
        << "Barnes-Hut RMS relative force error = " << rms_error
        << " (expected < 1% for theta=0.5)";
}

// Test that lower theta gives higher accuracy
TEST(Phase18_19_BarnesHutAudit, AccuracyScalesWithTheta) {
    constexpr int N = 64;

    // Brute-force reference
    SimulationConfig bf_config{};
    bf_config.max_particles = N;
    bf_config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    bf_config.softening = 1e-3;
    bf_config.dt = 0.001;

    Engine bf_engine;
    bf_engine.init(bf_config);
    setup_random_cluster(bf_engine, N, 10.0, 5.0, 54321);
    bf_engine.step(0.001, 1e-3);

    std::vector<double> ref_ax(N), ref_ay(N), ref_az(N);
    bf_engine.get_accelerations(ref_ax.data(), ref_ay.data(), ref_az.data(), N);
    bf_engine.shutdown();

    double prev_err = 1.0;
    double thetas[] = {0.8, 0.5, 0.3};

    for (double theta : thetas) {
        SimulationConfig bh_config{};
        bh_config.max_particles = N;
        bh_config.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
        bh_config.softening = 1e-3;
        bh_config.theta = theta;
        bh_config.dt = 0.001;

        Engine bh_engine;
        bh_engine.init(bh_config);
        setup_random_cluster(bh_engine, N, 10.0, 5.0, 54321);
        bh_engine.step(0.001, 1e-3);

        std::vector<double> bh_ax(N), bh_ay(N), bh_az(N);
        bh_engine.get_accelerations(bh_ax.data(), bh_ay.data(), bh_az.data(), N);
        bh_engine.shutdown();

        double sum_err2 = 0.0;
        int count = 0;
        for (int i = 0; i < N; i++) {
            double ref_mag = std::sqrt(ref_ax[i]*ref_ax[i] + ref_ay[i]*ref_ay[i]
                                       + ref_az[i]*ref_az[i]);
            if (ref_mag < 1e-15) continue;

            double dx = bh_ax[i] - ref_ax[i];
            double dy = bh_ay[i] - ref_ay[i];
            double dz = bh_az[i] - ref_az[i];
            double err_mag = std::sqrt(dx*dx + dy*dy + dz*dz);
            sum_err2 += (err_mag / ref_mag) * (err_mag / ref_mag);
            count++;
        }

        double rms_error = std::sqrt(sum_err2 / std::max(count, 1));

        // Each lower theta should give equal or better accuracy
        EXPECT_LE(rms_error, prev_err + 1e-12)
            << "theta=" << theta << " error=" << rms_error
            << " not better than previous=" << prev_err;
        prev_err = rms_error;
    }
}

// ==========================================================================
// MODULE 8: Drift Stress Tests
// ==========================================================================

// Test energy drift over 1000 steps for a stable circular orbit
TEST(Phase18_19_DriftStress, EnergyDrift_CircularOrbit_1000Steps) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-6;
    config.dt = 0.0001;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    // Initial energy snapshot
    engine.compute_energy_snapshot();
    double initial_drift = engine.energy_drift();

    // Run 1000 steps
    for (int i = 0; i < 1000; i++) {
        engine.step(config.dt, config.softening);
    }

    double final_drift = std::abs(engine.energy_drift());

    // Energy drift should be below 1e-4 for 1000 Leapfrog steps of a circular orbit
    EXPECT_LT(final_drift, 1e-4)
        << "Energy drift after 1000 steps = " << final_drift;

    engine.shutdown();
}

// Test momentum drift over 1000 steps (should be near-exact for isolated system)
TEST(Phase18_19_DriftStress, MomentumDrift_1000Steps) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-6;
    config.dt = 0.0005;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 1000; i++) {
        engine.step(config.dt, config.softening);
    }

    double momentum_drift = std::abs(engine.momentum_drift());

    // Momentum should be conserved to machine precision for Newton III
    EXPECT_LT(momentum_drift, 1e-8)
        << "Momentum drift after 1000 steps = " << momentum_drift;

    engine.shutdown();
}

// Test angular momentum drift for a circular orbit
TEST(Phase18_19_DriftStress, AngularMomentumDrift_1000Steps) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-6;
    config.dt = 0.0001;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 1000; i++) {
        engine.step(config.dt, config.softening);
    }

    double am_drift = std::abs(engine.angular_momentum_drift());

    // Angular momentum should be very well conserved
    EXPECT_LT(am_drift, 1e-6)
        << "Angular momentum drift after 1000 steps = " << am_drift;

    engine.shutdown();
}

// Test energy drift with Barnes-Hut (higher tolerance due to BH approx error)
TEST(Phase18_19_DriftStress, EnergyDrift_BarnesHut_1000Steps) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    config.softening = 1e-4;
    config.theta = 0.5;
    config.dt = 0.0005;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 1000; i++) {
        engine.step(config.dt, config.softening);
    }

    double final_drift = std::abs(engine.energy_drift());

    // BH drift can be higher due to force approximation noise, but bounded
    EXPECT_LT(final_drift, 1e-2)
        << "BH energy drift after 1000 steps = " << final_drift;

    engine.shutdown();
}

// Test Yoshida integrator energy drift (should be much better than leapfrog)
TEST(Phase18_19_DriftStress, EnergyDrift_Yoshida4_1000Steps) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-6;
    config.dt = 0.001;
    config.integrator = IntegratorType::Yoshida4;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 1000; i++) {
        engine.step(config.dt, config.softening);
    }

    double final_drift = std::abs(engine.energy_drift());

    // Yoshida4 should have much better energy conservation than Leapfrog
    // (4th order vs 2nd order)
    EXPECT_LT(final_drift, 1e-6)
        << "Yoshida4 energy drift after 1000 steps = " << final_drift;

    engine.shutdown();
}

// Stress test: Random N-body cluster with mass conservation check
TEST(Phase18_19_DriftStress, MassConservation_RandomCluster) {
    constexpr int N = 64;

    SimulationConfig config{};
    config.max_particles = N;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-3;
    config.dt = 0.001;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);
    setup_random_cluster(engine, N, 5.0, 2.0, 99999);

    engine.compute_energy_snapshot();
    double initial_mass = engine.energy_tracker().current().total_mass;

    for (int i = 0; i < 500; i++) {
        engine.step(config.dt, config.softening);
    }

    double final_mass = engine.energy_tracker().current().total_mass;

    // Mass must be EXACTLY conserved (no merges in this test)
    EXPECT_DOUBLE_EQ(initial_mass, final_mass)
        << "Mass changed: initial=" << initial_mass << " final=" << final_mass;

    engine.shutdown();
}

// ==========================================================================
// MODULE 6: CPU vs GPU Validation (requires CUDA, skipped if no GPU)
// ==========================================================================

TEST(Phase18_19_GpuValidation, ValidationFramework) {
    // Test the validation configuration API even without GPU
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.enable_gpu_validation = true;
    config.gpu_validation_tolerance = 1e-4;

    Engine engine;
    engine.init(config);
    setup_circular_orbit(engine, 1.0, 1000.0, 1.0);

    // Validation in CPU mode should just return default (passed=true)
    auto result = engine.validate_gpu_cpu_parity();
    EXPECT_TRUE(result.passed);

    engine.shutdown();
}

// ==========================================================================
// ENGINE WIRING: GPU-resident merge path (CPU test via collision resolver)
// ==========================================================================

TEST(Phase18_19_MergePath, MergeConservesMass) {
    SimulationConfig config{};
    config.max_particles = 16;
    config.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    config.softening = 1e-4;
    config.dt = 0.01;
    config.enable_collisions = true;
    config.collision_config.mode = CollisionMode::Merge;
    config.collision_config.max_merges_per_frame = 64;
    config.collision_config.max_merges_per_body = 4;
    config.enable_diagnostics = true;

    Engine engine;
    engine.init(config);

    // Two particles directly on top of each other → immediate merge
    double px[] = {0.0, 0.001};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {3.0, 7.0};
    double radius[] = {0.1, 0.1};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 2);

    double initial_total_mass = 10.0;  // 3 + 7

    engine.step(config.dt, config.softening);

    // After merge, should have 1 particle with mass = 10
    int active_count = engine.active_particle_count();
    EXPECT_EQ(active_count, 1);
    EXPECT_EQ(engine.particle_count(), 1);

    // Mass must be exactly conserved
    engine.compute_energy_snapshot();
    double final_mass = engine.energy_tracker().current().total_mass;
    EXPECT_DOUBLE_EQ(initial_total_mass, final_mass);

    engine.shutdown();
}
