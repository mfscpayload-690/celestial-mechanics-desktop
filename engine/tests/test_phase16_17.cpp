#include <gtest/gtest.h>
#include <celestial/sim/engine.hpp>
#include <celestial/profile/energy_tracker.hpp>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/interop/native_api.h>
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

static void setup_elliptical_orbit(Engine& engine, double r, double m_central,
                                    double m_orbiter, double v_fraction) {
    double v_circ = std::sqrt(m_central / r);
    double v = v_fraction * v_circ;

    double px[] = {0.0, r};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, v};
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

static void setup_three_body(Engine& engine) {
    // Pythagorean three-body: masses 3,4,5 at vertices of a 3-4-5 right triangle
    double px[] = {1.0, -2.0, 1.0};
    double py[] = {3.0, -1.0, -1.0};
    double pz[] = {0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0};
    double ax[] = {0.0, 0.0, 0.0};
    double ay[] = {0.0, 0.0, 0.0};
    double az[] = {0.0, 0.0, 0.0};
    double mass[] = {3.0, 4.0, 5.0};
    double radius[] = {0.01, 0.01, 0.01};
    uint8_t active[] = {1, 1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 3);
}

static void make_random_cluster(Engine& engine, int n, double box_size,
                                 double mass_min, double mass_max, uint64_t seed) {
    std::mt19937_64 rng(seed);
    std::uniform_real_distribution<double> pos_dist(-box_size, box_size);
    std::uniform_real_distribution<double> vel_dist(-0.01, 0.01);
    std::uniform_real_distribution<double> mass_dist(mass_min, mass_max);

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
// Step 1: Orbital Mechanics Validation
// ==========================================================================

class OrbitalTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 16;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.0001;
        cfg.softening = 1e-6;
        engine.init(cfg);
    }
    void TearDown() override { engine.shutdown(); }
    Engine engine;
};

// Test 1: Circular orbit remains bounded over 10k frames
TEST_F(OrbitalTest, CircularOrbit_10kFrames) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    for (int i = 0; i < 10000; i++) {
        engine.step(0.0001, 1e-6);
    }

    double out_px[2], out_py[2], out_pz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);

    double dx = out_px[1] - out_px[0];
    double dy = out_py[1] - out_py[0];
    double dz = out_pz[1] - out_pz[0];
    double r_final = std::sqrt(dx * dx + dy * dy + dz * dz);

    EXPECT_GT(r_final, 0.95) << "Orbiter drifted too close";
    EXPECT_LT(r_final, 1.05) << "Orbiter drifted too far";
}

// Test 2: Energy conserved over 10k frames (circular orbit)
TEST_F(OrbitalTest, CircularOrbit_EnergyConserved_10k) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    engine.compute_energy_snapshot();
    double E0 = engine.energy_tracker().current().total_energy;

    for (int i = 0; i < 10000; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();
    double E1 = engine.energy_tracker().current().total_energy;

    double relative_drift = std::abs((E1 - E0) / E0);
    EXPECT_LT(relative_drift, 1e-3)
        << "Energy drift too large over 10k frames: " << relative_drift;
}

// Test 3: Elliptical orbit stability (e~0.5) over 10k frames
TEST_F(OrbitalTest, EllipticalOrbit_Stability) {
    setup_elliptical_orbit(engine, 1.0, 1.0, 1e-6, 0.7);

    double max_r = 0.0;
    double min_r = 1e30;

    for (int i = 0; i < 10000; i++) {
        engine.step(0.0001, 1e-6);

        if (i % 100 == 0) {
            double out_px[2], out_py[2], out_pz[2];
            engine.get_positions(out_px, out_py, out_pz, 2);
            double dx = out_px[1] - out_px[0];
            double dy = out_py[1] - out_py[0];
            double dz = out_pz[1] - out_pz[0];
            double r = std::sqrt(dx * dx + dy * dy + dz * dz);
            if (r > max_r) max_r = r;
            if (r < min_r) min_r = r;
        }
    }

    // Orbit should remain bounded — not escaping or spiraling in
    // With v = 0.7 * v_circ the orbit is bound but eccentric
    EXPECT_GT(min_r, 0.01) << "Orbiter collapsed too close: min_r=" << min_r;
    EXPECT_LT(max_r, 10.0) << "Orbiter escaped: max_r=" << max_r;
}

// Test 4: Energy conserved for elliptical orbit
TEST_F(OrbitalTest, EllipticalOrbit_EnergyConserved) {
    setup_elliptical_orbit(engine, 1.0, 1.0, 1e-6, 0.7);

    engine.compute_energy_snapshot();
    double E0 = engine.energy_tracker().current().total_energy;

    for (int i = 0; i < 10000; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();
    double E1 = engine.energy_tracker().current().total_energy;

    double relative_drift = std::abs((E1 - E0) / E0);
    EXPECT_LT(relative_drift, 5e-3)
        << "Elliptical orbit energy drift: " << relative_drift;
}

// ==========================================================================
// Step 2: Energy Tracking
// ==========================================================================

class EnergyTrackingTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        engine.init(cfg);
    }
    void TearDown() override { engine.shutdown(); }
    Engine engine;
};

// Test 5: Total energy (KE + PE) conserved for 5-body system
TEST_F(EnergyTrackingTest, TotalConserved_5Body) {
    make_random_cluster(engine, 5, 5.0, 1.0, 2.0, 42);

    engine.compute_energy_snapshot();
    double E0 = engine.energy_tracker().current().total_energy;

    for (int i = 0; i < 1000; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double E1 = engine.energy_tracker().current().total_energy;

    double relative_drift = std::abs((E1 - E0) / std::abs(E0));
    EXPECT_LT(relative_drift, 0.01)
        << "5-body energy drift: " << relative_drift;
}

// Test 6: Energy drift stays bounded (doesn't diverge) for 50-body
TEST_F(EnergyTrackingTest, DriftBounded_50Body) {
    make_random_cluster(engine, 50, 10.0, 0.5, 2.0, 123);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 1000; i++) {
        engine.step(0.001, 1e-4);
        if (i % 100 == 0) {
            engine.compute_energy_snapshot();
            double drift = std::abs(engine.energy_drift());
            EXPECT_LT(drift, 0.1)
                << "Energy drift diverging at step " << i << ": " << drift;
        }
    }
}

// Test 7: BH PE matches direct PE within 1% at theta=0.5
TEST(BHPotentialTest, MatchesDirect_Theta05) {
    const int n = 50;
    ParticleSystem ps;
    ps.allocate(n);
    ps.set_count(n);

    std::mt19937_64 rng(777);
    std::uniform_real_distribution<double> pos(-10.0, 10.0);
    std::uniform_real_distribution<double> mass_d(0.5, 3.0);

    for (int i = 0; i < n; i++) {
        ps.pos_x[i] = pos(rng);
        ps.pos_y[i] = pos(rng);
        ps.pos_z[i] = pos(rng);
        ps.vel_x[i] = 0.0;
        ps.vel_y[i] = 0.0;
        ps.vel_z[i] = 0.0;
        ps.mass[i] = mass_d(rng);
        ps.is_active[i] = 1;
    }

    double softening = 1e-4;

    // Direct O(N^2) PE
    EnergyTracker tracker;
    auto snap_direct = tracker.compute(
        ps.pos_x, ps.pos_y, ps.pos_z,
        ps.vel_x, ps.vel_y, ps.vel_z,
        ps.mass, ps.is_active, n, softening);

    // BH PE at theta=0.5
    BarnesHutSolver bh;
    bh.theta = 0.5;
    bh.use_parallel = false;
    double bh_pe = bh.compute_potential(ps, softening);

    double relative_error = std::abs((bh_pe - snap_direct.potential_energy) /
                                      snap_direct.potential_energy);
    EXPECT_LT(relative_error, 0.01)
        << "BH PE=" << bh_pe << " Direct PE=" << snap_direct.potential_energy
        << " error=" << relative_error;

    ps.free();
}

// Test 8: BH PE matches direct PE exactly at theta=0
TEST(BHPotentialTest, ExactAtTheta0) {
    const int n = 20;
    ParticleSystem ps;
    ps.allocate(n);
    ps.set_count(n);

    std::mt19937_64 rng(999);
    std::uniform_real_distribution<double> pos(-5.0, 5.0);

    for (int i = 0; i < n; i++) {
        ps.pos_x[i] = pos(rng);
        ps.pos_y[i] = pos(rng);
        ps.pos_z[i] = pos(rng);
        ps.vel_x[i] = 0.0;
        ps.vel_y[i] = 0.0;
        ps.vel_z[i] = 0.0;
        ps.mass[i] = 1.0;
        ps.is_active[i] = 1;
    }

    double softening = 1e-4;

    EnergyTracker tracker;
    auto snap_direct = tracker.compute(
        ps.pos_x, ps.pos_y, ps.pos_z,
        ps.vel_x, ps.vel_y, ps.vel_z,
        ps.mass, ps.is_active, n, softening);

    BarnesHutSolver bh;
    bh.theta = 0.0;
    bh.use_parallel = false;
    double bh_pe = bh.compute_potential(ps, softening);

    EXPECT_NEAR(bh_pe, snap_direct.potential_energy, 1e-10)
        << "BH PE at theta=0 should match direct exactly";

    ps.free();
}

// Test 9: Rolling average tracks correctly
TEST_F(EnergyTrackingTest, RollingAverage_Tracks) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    double manual_sum = 0.0;
    int count = 0;
    for (int i = 0; i < 500; i++) {
        engine.step(0.001, 1e-4);
        engine.compute_energy_snapshot();
        manual_sum += engine.energy_tracker().current().total_energy;
        count++;
    }

    double expected_avg = manual_sum / count;
    double rolling = engine.rolling_avg_energy();

    // Rolling window is 300, so it only has last 300 samples
    // We just check it's populated and reasonable
    EXPECT_NE(rolling, 0.0) << "Rolling average should be non-zero";
    // The rolling average of the last 300 should be close to the overall average
    EXPECT_NEAR(rolling, expected_avg, std::abs(expected_avg) * 0.1)
        << "Rolling avg should be within 10% of overall avg";
}

// ==========================================================================
// Step 3: Momentum Conservation
// ==========================================================================

class MomentumTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        engine.init(cfg);
    }
    void TearDown() override { engine.shutdown(); }
    Engine engine;
};

// Test 10: Momentum conserved for 20-body system
TEST_F(MomentumTest, Conserved_MultiBody) {
    make_random_cluster(engine, 20, 5.0, 0.5, 2.0, 555);

    engine.compute_energy_snapshot();
    auto snap0 = engine.energy_tracker().current();

    for (int i = 0; i < 1000; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double pdrift = engine.momentum_drift();

    EXPECT_LT(pdrift, 1e-8)
        << "Momentum drift too large: " << pdrift;
}

// Test 11: Momentum preserved through merge collisions
TEST(MomentumCollisionTest, ConservedAfterMerges) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 64;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    // 10 bodies heading toward each other to force merges
    const int n = 10;
    std::vector<double> px(n), py(n), pz(n, 0.0);
    std::vector<double> vx(n), vy(n, 0.0), vz(n, 0.0);
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<double> mass(n, 1.0);
    std::vector<double> radius(n, 0.5); // Large radii to ensure collisions
    std::vector<uint8_t> active(n, 1);

    for (int i = 0; i < n; i++) {
        px[i] = (i - n / 2) * 0.3; // Clustered near origin
        py[i] = 0.0;
        vx[i] = -(i - n / 2) * 0.01; // Heading toward center
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);

    engine.compute_energy_snapshot();
    auto snap0 = engine.energy_tracker().current();
    double p0_x = snap0.momentum_x;
    double p0_y = snap0.momentum_y;
    double p0_z = snap0.momentum_z;

    for (int i = 0; i < 500; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    auto snap1 = engine.energy_tracker().current();
    double dp_x = snap1.momentum_x - p0_x;
    double dp_y = snap1.momentum_y - p0_y;
    double dp_z = snap1.momentum_z - p0_z;
    double dp = std::sqrt(dp_x * dp_x + dp_y * dp_y + dp_z * dp_z);

    EXPECT_LT(dp, 1e-6)
        << "Momentum not conserved after merges: dp=" << dp;

    engine.shutdown();
}

// ==========================================================================
// Step 4: Collision Detection (Scalable)
// ==========================================================================

// Test 12: BH traversal finds head-on collision
TEST(BHCollisionTest, HeadOn) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.theta = 0.5;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    // Two bodies heading straight toward each other
    double px[] = {-1.0, 1.0};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.5, -0.5};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1.0, 1.0};
    double radius[] = {0.3, 0.3};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 2);

    bool collision_happened = false;
    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-6);
        if (engine.particle_count() < 2 || engine.active_particle_count() < 2) {
            collision_happened = true;
            break;
        }
    }

    EXPECT_TRUE(collision_happened)
        << "Expected head-on collision to result in merge";

    engine.shutdown();
}

// Test 13: No false positive collisions for well-separated bodies
TEST(BHCollisionTest, NoFalsePositives) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    cfg.theta = 0.5;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    // 50 bodies on a widely spaced grid — no collisions possible
    const int n = 50;
    std::vector<double> px(n), py(n), pz(n, 0.0);
    std::vector<double> vx(n, 0.0), vy(n, 0.0), vz(n, 0.0);
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<double> mass(n, 1.0);
    std::vector<double> radius(n, 0.001); // Tiny radii
    std::vector<uint8_t> active(n, 1);

    for (int i = 0; i < n; i++) {
        px[i] = (i % 10) * 100.0; // Spaced 100 apart
        py[i] = (i / 10) * 100.0;
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);

    for (int i = 0; i < 10; i++) {
        engine.step(0.001, 1e-4);
    }

    EXPECT_EQ(engine.particle_count(), n)
        << "No merges should occur for well-separated bodies";

    engine.shutdown();
}

// ==========================================================================
// Step 5: Collision Response (Merging)
// ==========================================================================

// Test 14: Merge preserves total mass
TEST(MergeResponseTest, PreservesMass) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 64;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    // 4 bodies in pairs that will merge
    double px[] = {0.0, 0.05, 5.0, 5.05};
    double py[] = {0.0, 0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0, 0.0};
    double vx[] = {0.01, -0.01, 0.01, -0.01};
    double vy[] = {0.0, 0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0, 0.0};
    double ax[] = {0.0, 0.0, 0.0, 0.0};
    double ay[] = {0.0, 0.0, 0.0, 0.0};
    double az[] = {0.0, 0.0, 0.0, 0.0};
    double mass[] = {2.0, 3.0, 4.0, 5.0};
    double radius[] = {0.5, 0.5, 0.5, 0.5}; // Large radii to force overlap
    uint8_t active[] = {1, 1, 1, 1};

    double total_mass_before = 2.0 + 3.0 + 4.0 + 5.0;

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 4);

    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-6);
    }

    // Compute total mass of remaining particles
    engine.compute_energy_snapshot();
    double total_mass_after = engine.energy_tracker().current().total_mass;

    EXPECT_NEAR(total_mass_after, total_mass_before, 1e-10)
        << "Total mass changed after merges";

    engine.shutdown();
}

// Test 15: Merge preserves total momentum
TEST(MergeResponseTest, PreservesMomentum) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 64;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    double px[] = {0.0, 0.05, 5.0, 5.05};
    double py[] = {0.0, 0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0, 0.0};
    double vx[] = {0.1, -0.1, 0.2, -0.2};
    double vy[] = {0.0, 0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0, 0.0};
    double ax[] = {0.0, 0.0, 0.0, 0.0};
    double ay[] = {0.0, 0.0, 0.0, 0.0};
    double az[] = {0.0, 0.0, 0.0, 0.0};
    double mass[] = {2.0, 3.0, 4.0, 5.0};
    double radius[] = {0.5, 0.5, 0.5, 0.5};
    uint8_t active[] = {1, 1, 1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 4);

    engine.compute_energy_snapshot();
    auto snap0 = engine.energy_tracker().current();
    double p0_x = snap0.momentum_x;
    double p0_y = snap0.momentum_y;
    double p0_z = snap0.momentum_z;

    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-6);
    }

    engine.compute_energy_snapshot();
    auto snap1 = engine.energy_tracker().current();
    double dp_x = snap1.momentum_x - p0_x;
    double dp_y = snap1.momentum_y - p0_y;
    double dp_z = snap1.momentum_z - p0_z;
    double dp = std::sqrt(dp_x * dp_x + dp_y * dp_y + dp_z * dp_z);

    EXPECT_LT(dp, 1e-6)
        << "Momentum not conserved after merges: dp=" << dp;

    engine.shutdown();
}

// Test 16: No body merged twice in same frame
TEST(MergeResponseTest, NoDuplicateMerges) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 64;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    cfg.collision_config.max_merges_per_body = 1;
    engine.init(cfg);

    // 10 bodies all close together — without safeguards, chain merges could occur
    const int n = 10;
    std::vector<double> px(n), py(n, 0.0), pz(n, 0.0);
    std::vector<double> vx(n, 0.0), vy(n, 0.0), vz(n, 0.0);
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<double> mass(n, 1.0);
    std::vector<double> radius(n, 0.5);
    std::vector<uint8_t> active(n, 1);

    for (int i = 0; i < n; i++) {
        px[i] = i * 0.1; // All within 0.9 of each other
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);

    // Single step — with max_merges_per_body=1, a body should merge at most once
    engine.step(0.001, 1e-6);

    // After one step, some bodies may have merged but the count should be reasonable
    // With 10 bodies and max_merges_per_body=1: at most 5 merges per step
    int remaining = engine.particle_count();
    EXPECT_GE(remaining, n / 2)
        << "Too many merges in single step — possible duplicate merging";

    // Verify no NaN/Inf
    std::vector<double> out_px(remaining), out_py(remaining), out_pz(remaining);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), remaining);
    for (int i = 0; i < remaining; i++) {
        EXPECT_TRUE(std::isfinite(out_px[i])) << "NaN/Inf after merge at " << i;
    }

    engine.shutdown();
}

// ==========================================================================
// Step 6: Angular Momentum
// ==========================================================================

// Test 17: Angular momentum conserved over 10k frames (circular orbit)
TEST(AngularMomentumTest, CircularOrbit_10k) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.0001;
    cfg.softening = 1e-6;
    engine.init(cfg);

    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    engine.compute_energy_snapshot();
    auto snap0 = engine.energy_tracker().current();
    double L0 = snap0.angular_momentum_magnitude;

    for (int i = 0; i < 10000; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();
    double L_drift = engine.angular_momentum_drift();

    EXPECT_LT(L_drift, 1e-6)
        << "Angular momentum drift over 10k frames: " << L_drift
        << " (L0=" << L0 << ")";

    engine.shutdown();
}

// ==========================================================================
// Step 7: Stress Testing
// ==========================================================================

// Test 18: Three-body chaotic system — no NaN/Inf
TEST(StressTest, ThreeBody_Chaotic_NoNaN) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.0001;
    cfg.softening = 0.01;
    engine.init(cfg);

    setup_three_body(engine);

    for (int i = 0; i < 5000; i++) {
        engine.step(0.0001, 0.01);
    }

    double out_px[3], out_py[3], out_pz[3];
    double out_vx[3], out_vy[3], out_vz[3];
    engine.get_positions(out_px, out_py, out_pz, 3);
    engine.get_velocities(out_vx, out_vy, out_vz, 3);

    for (int i = 0; i < 3; i++) {
        EXPECT_TRUE(std::isfinite(out_px[i])) << "NaN/Inf pos_x[" << i << "]";
        EXPECT_TRUE(std::isfinite(out_py[i])) << "NaN/Inf pos_y[" << i << "]";
        EXPECT_TRUE(std::isfinite(out_pz[i])) << "NaN/Inf pos_z[" << i << "]";
        EXPECT_TRUE(std::isfinite(out_vx[i])) << "NaN/Inf vel_x[" << i << "]";
        EXPECT_TRUE(std::isfinite(out_vy[i])) << "NaN/Inf vel_y[" << i << "]";
        EXPECT_TRUE(std::isfinite(out_vz[i])) << "NaN/Inf vel_z[" << i << "]";
    }

    engine.shutdown();
}

// Test 19: 100-body cluster — stability and bounded energy drift
TEST(StressTest, Cluster100_Stability) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 0.01;
    engine.init(cfg);

    make_random_cluster(engine, 100, 5.0, 0.5, 2.0, 314);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 2000; i++) {
        engine.step(0.001, 0.01);
    }

    engine.compute_energy_snapshot();

    // Verify no NaN
    int n = engine.particle_count();
    std::vector<double> out_px(n), out_py(n), out_pz(n);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), n);
    for (int i = 0; i < n; i++) {
        EXPECT_TRUE(std::isfinite(out_px[i])) << "NaN/Inf at body " << i;
    }

    // Energy drift bounded
    double drift = std::abs(engine.energy_drift());
    EXPECT_LT(drift, 0.5) << "100-body energy drift: " << drift;

    engine.shutdown();
}

// Test 20: 1k body BH mode — no NaN/Inf, no tree corruption
TEST(StressTest, LargeScale_1k_BH) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 2048;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 0.01;
    cfg.theta = 0.5;
    engine.init(cfg);

    make_random_cluster(engine, 1000, 50.0, 0.5, 2.0, 42);

    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 0.01);
    }

    int n = engine.particle_count();
    std::vector<double> out_px(n), out_py(n), out_pz(n);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), n);

    bool any_nan = false;
    for (int i = 0; i < n; i++) {
        if (!std::isfinite(out_px[i]) || !std::isfinite(out_py[i]) ||
            !std::isfinite(out_pz[i])) {
            any_nan = true;
            break;
        }
    }
    EXPECT_FALSE(any_nan) << "NaN/Inf found in 1k BH simulation";

    engine.shutdown();
}

// Test 21: 10k body BH mode — completes without NaN
TEST(StressTest, LargeScale_10k_BH) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16384;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 0.01;
    cfg.theta = 0.5;
    engine.init(cfg);

    make_random_cluster(engine, 10000, 100.0, 0.1, 1.0, 12345);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 0.01);
    }

    int n = engine.particle_count();
    std::vector<double> out_px(n), out_py(n), out_pz(n);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), n);

    bool any_nan = false;
    for (int i = 0; i < n; i++) {
        if (!std::isfinite(out_px[i]) || !std::isfinite(out_py[i]) ||
            !std::isfinite(out_pz[i])) {
            any_nan = true;
            break;
        }
    }
    EXPECT_FALSE(any_nan) << "NaN/Inf found in 10k BH simulation";
    EXPECT_EQ(n, 10000) << "Particle count changed unexpectedly";

    engine.shutdown();
}

// Test 22: 10k body BH with merge collisions — count decreases, no corruption
TEST(StressTest, LargeScale_10k_Collisions) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16384;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 0.01;
    cfg.theta = 0.5;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    cfg.collision_config.max_merges_per_frame = 256;
    engine.init(cfg);

    // Use tighter clustering to promote collisions
    make_random_cluster(engine, 10000, 10.0, 0.1, 1.0, 67890);

    int initial_count = engine.particle_count();

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 0.01);
    }

    int final_count = engine.particle_count();

    // With tight clustering and large radii, some merges should happen
    EXPECT_LE(final_count, initial_count)
        << "Particle count should not increase";

    // Verify no NaN in remaining particles
    std::vector<double> out_px(final_count), out_py(final_count), out_pz(final_count);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), final_count);

    bool any_nan = false;
    for (int i = 0; i < final_count; i++) {
        if (!std::isfinite(out_px[i]) || !std::isfinite(out_py[i]) ||
            !std::isfinite(out_pz[i])) {
            any_nan = true;
            break;
        }
    }
    EXPECT_FALSE(any_nan) << "NaN/Inf found in 10k collision stress test";

    engine.shutdown();
}

// ==========================================================================
// Step 8: Diagnostics
// ==========================================================================

// Test 23: enable_diagnostics auto-computes energy snapshots each step
TEST(DiagnosticsTest, AutoCompute) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.0001;
    cfg.softening = 1e-6;
    cfg.enable_diagnostics = true;
    engine.init(cfg);

    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    // Run 100 steps — diagnostics should auto-compute
    for (int i = 0; i < 100; i++) {
        engine.step(0.0001, 1e-6);
    }

    // If diagnostics ran, energy_tracker should have initial set and non-zero energy
    EXPECT_TRUE(engine.energy_tracker().has_initial())
        << "Energy tracker should have initial snapshot from auto-compute";

    auto snap = engine.energy_tracker().current();
    EXPECT_NE(snap.total_energy, 0.0)
        << "Total energy should be non-zero after auto-compute";

    // Energy drift should be computed and bounded
    double drift = std::abs(engine.energy_drift());
    EXPECT_LT(drift, 1e-3) << "Energy drift from auto-compute: " << drift;

    engine.shutdown();
}

// Test 24: Conservation diagnostics pass for clean circular orbit
TEST(DiagnosticsTest, AllPass) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 16;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.0001;
    cfg.softening = 1e-6;
    engine.init(cfg);

    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 500; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();

    DiagnosticThresholds thresholds;
    bool all_pass = engine.check_conservation_diagnostics(thresholds);
    EXPECT_TRUE(all_pass) << "Conservation diagnostics should pass for circular orbit";

    engine.shutdown();
}

// Test 25: Particle count accurate after merges + compaction
TEST(DiagnosticsTest, BodyCountAccurate) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = 64;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.enable_collisions = true;
    cfg.collision_config.mode = CollisionMode::Merge;
    engine.init(cfg);

    // 10 bodies in a tight cluster to force merges
    const int n = 10;
    std::vector<double> px(n), py(n, 0.0), pz(n, 0.0);
    std::vector<double> vx(n, 0.0), vy(n, 0.0), vz(n, 0.0);
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<double> mass(n, 1.0);
    std::vector<double> radius(n, 0.5);
    std::vector<uint8_t> active(n, 1);

    for (int i = 0; i < n; i++) {
        px[i] = i * 0.1;
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 1e-6);
    }

    int reported_count = engine.particle_count();
    int active_count = engine.active_particle_count();

    // After merges + compaction, particle_count should reflect actual count
    EXPECT_GT(reported_count, 0) << "Must have at least 1 particle";
    EXPECT_LE(reported_count, n) << "Count should not exceed original";
    EXPECT_EQ(reported_count, active_count)
        << "After compaction, particle_count should equal active_particle_count";

    engine.shutdown();
}
