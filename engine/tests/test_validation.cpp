#include <gtest/gtest.h>
#include <celestial/sim/engine.hpp>
#include <celestial/sim/deterministic.hpp>
#include <celestial/profile/energy_tracker.hpp>
#include <celestial/profile/benchmark.hpp>
#include <cmath>
#include <vector>
#include <numeric>

using namespace celestial::sim;
using namespace celestial::profile;

// ==========================================================================
// Helper: set up a two-body circular orbit (m1 heavy at origin, m2 light orbiting)
// ==========================================================================
static void setup_circular_orbit(Engine& engine, double r, double m_central,
                                  double m_orbiter) {
    // v_circular = sqrt(G * M / r), with G=1
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

// ==========================================================================
// Test 1: Binary orbit stability — orbit should remain bounded
// ==========================================================================
class BinaryOrbitTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 16;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.0001;
        cfg.softening = 1e-6;
        engine.init(cfg);
    }

    void TearDown() override {
        engine.shutdown();
    }

    Engine engine;
};

TEST_F(BinaryOrbitTest, OrbitRemainsStable_1000Steps) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    // Record initial energy
    engine.compute_energy_snapshot();
    double initial_energy = engine.energy_tracker().current().total_energy;

    for (int i = 0; i < 1000; i++) {
        engine.step(0.0001, 1e-6);
    }

    // Check orbit radius (body 1 relative to body 0)
    double out_px[2], out_py[2], out_pz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);

    double dx = out_px[1] - out_px[0];
    double dy = out_py[1] - out_py[0];
    double dz = out_pz[1] - out_pz[0];
    double r_final = std::sqrt(dx * dx + dy * dy + dz * dz);

    // Orbit should be within 5% of initial radius after 1000 steps
    EXPECT_GT(r_final, 0.95) << "Orbiter drifted too close";
    EXPECT_LT(r_final, 1.05) << "Orbiter drifted too far";
}

TEST_F(BinaryOrbitTest, EnergyConserved_Leapfrog) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    engine.compute_energy_snapshot();
    double E0 = engine.energy_tracker().current().total_energy;

    for (int i = 0; i < 5000; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();
    double E1 = engine.energy_tracker().current().total_energy;

    // Leapfrog integrator with dt=0.0001 should conserve energy to ~1e-4
    double relative_drift = std::abs((E1 - E0) / E0);
    EXPECT_LT(relative_drift, 1e-3)
        << "Energy drift too large: E0=" << E0 << " E1=" << E1
        << " drift=" << relative_drift;
}

TEST_F(BinaryOrbitTest, MomentumConserved) {
    setup_circular_orbit(engine, 1.0, 1.0, 1e-6);

    engine.compute_energy_snapshot();
    auto snap0 = engine.energy_tracker().current();
    double p0 = snap0.momentum_magnitude;

    for (int i = 0; i < 1000; i++) {
        engine.step(0.0001, 1e-6);
    }

    engine.compute_energy_snapshot();
    auto snap1 = engine.energy_tracker().current();
    double p1 = snap1.momentum_magnitude;

    // Momentum should be conserved to machine precision for isolated system
    // (softening introduces small errors)
    double drift = std::abs(p1 - p0);
    EXPECT_LT(drift, 1e-8)
        << "Momentum drift: p0=" << p0 << " p1=" << p1;
}

// ==========================================================================
// Test 2: CPU Brute-Force vs Barnes-Hut parity
// ==========================================================================
class ParityTest : public ::testing::Test {
protected:
    static constexpr int N = 50;

    void init_engine(Engine& eng, SimulationConfig::ComputeMode mode) {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = mode;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        cfg.theta = 0.0; // Exact mode for BH
        eng.init(cfg);
    }

    void load_bodies(Engine& eng) {
        std::vector<double> px(N), py(N), pz(N);
        std::vector<double> vx(N), vy(N), vz(N);
        std::vector<double> ax(N), ay(N), az(N);
        std::vector<double> mass(N), radius(N);
        std::vector<uint8_t> active(N);

        for (int i = 0; i < N; i++) {
            px[i] = std::sin(i * 1.0) * 10.0;
            py[i] = std::cos(i * 1.3) * 10.0;
            pz[i] = std::sin(i * 0.7 + 0.5) * 10.0;
            vx[i] = std::cos(i * 0.3) * 0.1;
            vy[i] = std::sin(i * 0.9) * 0.1;
            vz[i] = std::cos(i * 1.1) * 0.1;
            ax[i] = 0.0;
            ay[i] = 0.0;
            az[i] = 0.0;
            mass[i] = 1.0 + std::abs(std::sin(i * 2.0));
            radius[i] = 0.01;
            active[i] = 1;
        }

        eng.set_particles(px.data(), py.data(), pz.data(),
                          vx.data(), vy.data(), vz.data(),
                          ax.data(), ay.data(), az.data(),
                          mass.data(), radius.data(), active.data(), N);
    }
};

TEST_F(ParityTest, CPUBruteForceVsBarnesHut_Exact) {
    Engine bf_engine, bh_engine;
    init_engine(bf_engine, SimulationConfig::ComputeMode::CPU_BruteForce);
    init_engine(bh_engine, SimulationConfig::ComputeMode::CPU_BarnesHut);
    load_bodies(bf_engine);
    load_bodies(bh_engine);

    // Run 10 steps on each
    for (int s = 0; s < 10; s++) {
        bf_engine.step(0.001, 1e-4);
        bh_engine.step(0.001, 1e-4);
    }

    // Compare positions
    std::vector<double> bf_px(N), bf_py(N), bf_pz(N);
    std::vector<double> bh_px(N), bh_py(N), bh_pz(N);
    bf_engine.get_positions(bf_px.data(), bf_py.data(), bf_pz.data(), N);
    bh_engine.get_positions(bh_px.data(), bh_py.data(), bh_pz.data(), N);

    for (int i = 0; i < N; i++) {
        EXPECT_NEAR(bf_px[i], bh_px[i], 1e-6)
            << "Position X mismatch at body " << i;
        EXPECT_NEAR(bf_py[i], bh_py[i], 1e-6)
            << "Position Y mismatch at body " << i;
        EXPECT_NEAR(bf_pz[i], bh_pz[i], 1e-6)
            << "Position Z mismatch at body " << i;
    }

    // Compare velocities
    std::vector<double> bf_vx(N), bf_vy(N), bf_vz(N);
    std::vector<double> bh_vx(N), bh_vy(N), bh_vz(N);
    bf_engine.get_velocities(bf_vx.data(), bf_vy.data(), bf_vz.data(), N);
    bh_engine.get_velocities(bh_vx.data(), bh_vy.data(), bh_vz.data(), N);

    for (int i = 0; i < N; i++) {
        EXPECT_NEAR(bf_vx[i], bh_vx[i], 1e-6)
            << "Velocity X mismatch at body " << i;
        EXPECT_NEAR(bf_vy[i], bh_vy[i], 1e-6)
            << "Velocity Y mismatch at body " << i;
        EXPECT_NEAR(bf_vz[i], bh_vz[i], 1e-6)
            << "Velocity Z mismatch at body " << i;
    }

    bf_engine.shutdown();
    bh_engine.shutdown();
}

// ==========================================================================
// Test 3: Dense cluster collapse — no NaN/Inf singularities
// ==========================================================================
class CollapseTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 0.01; // Important for preventing singularities
        engine.init(cfg);
    }

    void TearDown() override {
        engine.shutdown();
    }

    Engine engine;
};

TEST_F(CollapseTest, DenseCluster_NoNaNs) {
    // Place 100 bodies in a very small sphere (radius 0.1)
    const int n = 100;
    std::vector<double> px(n), py(n), pz(n);
    std::vector<double> vx(n), vy(n), vz(n);
    std::vector<double> ax(n), ay(n), az(n);
    std::vector<double> mass(n), radius(n);
    std::vector<uint8_t> active(n);

    for (int i = 0; i < n; i++) {
        double t = static_cast<double>(i) / static_cast<double>(n);
        double phi = t * 3.14159265358979 * 2.0 * 7.0; // spiral
        double r = 0.1 * t;
        px[i] = r * std::cos(phi);
        py[i] = r * std::sin(phi);
        pz[i] = 0.05 * std::sin(i * 0.7);
        vx[i] = 0.0;
        vy[i] = 0.0;
        vz[i] = 0.0;
        ax[i] = 0.0;
        ay[i] = 0.0;
        az[i] = 0.0;
        mass[i] = 1.0;
        radius[i] = 0.001;
        active[i] = 1;
    }

    engine.set_particles(px.data(), py.data(), pz.data(),
                         vx.data(), vy.data(), vz.data(),
                         ax.data(), ay.data(), az.data(),
                         mass.data(), radius.data(), active.data(), n);

    // Run 500 steps — bodies will collapse inward
    for (int s = 0; s < 500; s++) {
        engine.step(0.001, 0.01);
    }

    // Verify no NaN or Inf in any state array
    std::vector<double> out_px(n), out_py(n), out_pz(n);
    std::vector<double> out_vx(n), out_vy(n), out_vz(n);
    std::vector<double> out_ax(n), out_ay(n), out_az(n);
    engine.get_positions(out_px.data(), out_py.data(), out_pz.data(), n);
    engine.get_velocities(out_vx.data(), out_vy.data(), out_vz.data(), n);
    engine.get_accelerations(out_ax.data(), out_ay.data(), out_az.data(), n);

    for (int i = 0; i < n; i++) {
        EXPECT_TRUE(std::isfinite(out_px[i]))
            << "NaN/Inf in pos_x[" << i << "]=" << out_px[i];
        EXPECT_TRUE(std::isfinite(out_py[i]))
            << "NaN/Inf in pos_y[" << i << "]=" << out_py[i];
        EXPECT_TRUE(std::isfinite(out_pz[i]))
            << "NaN/Inf in pos_z[" << i << "]=" << out_pz[i];
        EXPECT_TRUE(std::isfinite(out_vx[i]))
            << "NaN/Inf in vel_x[" << i << "]=" << out_vx[i];
        EXPECT_TRUE(std::isfinite(out_vy[i]))
            << "NaN/Inf in vel_y[" << i << "]=" << out_vy[i];
        EXPECT_TRUE(std::isfinite(out_vz[i]))
            << "NaN/Inf in vel_z[" << i << "]=" << out_vz[i];
        EXPECT_TRUE(std::isfinite(out_ax[i]))
            << "NaN/Inf in acc_x[" << i << "]=" << out_ax[i];
        EXPECT_TRUE(std::isfinite(out_ay[i]))
            << "NaN/Inf in acc_y[" << i << "]=" << out_ay[i];
        EXPECT_TRUE(std::isfinite(out_az[i]))
            << "NaN/Inf in acc_z[" << i << "]=" << out_az[i];
    }
}

TEST_F(CollapseTest, CoincidentBodies_SofteningProtects) {
    // Place two bodies at exactly the same location
    double px[] = {0.0, 0.0};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1.0, 1.0};
    double radius_arr[] = {0.01, 0.01};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius_arr, active, 2);

    engine.step(0.001, 0.01);

    double out_px[2], out_py[2], out_pz[2];
    double out_vx[2], out_vy[2], out_vz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);
    engine.get_velocities(out_vx, out_vy, out_vz, 2);

    for (int i = 0; i < 2; i++) {
        EXPECT_TRUE(std::isfinite(out_px[i]));
        EXPECT_TRUE(std::isfinite(out_py[i]));
        EXPECT_TRUE(std::isfinite(out_pz[i]));
        EXPECT_TRUE(std::isfinite(out_vx[i]));
        EXPECT_TRUE(std::isfinite(out_vy[i]));
        EXPECT_TRUE(std::isfinite(out_vz[i]));
    }
}

// ==========================================================================
// Test 4: Deterministic mode — identical results from identical seeds
// ==========================================================================
class DeterminismTest : public ::testing::Test {
protected:
    static constexpr int N = 30;

    void run_simulation(Engine& eng, int steps) {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        cfg.deterministic = true;
        cfg.deterministic_seed = 12345;
        eng.init(cfg);

        std::vector<double> px(N), py(N), pz(N);
        std::vector<double> vx(N), vy(N), vz(N);
        std::vector<double> ax(N), ay(N), az(N);
        std::vector<double> mass(N), radius(N);
        std::vector<uint8_t> active(N);

        for (int i = 0; i < N; i++) {
            px[i] = std::sin(i * 1.0) * 5.0;
            py[i] = std::cos(i * 1.3) * 5.0;
            pz[i] = std::sin(i * 0.7) * 5.0;
            vx[i] = std::cos(i * 0.3) * 0.05;
            vy[i] = std::sin(i * 0.9) * 0.05;
            vz[i] = std::cos(i * 1.1) * 0.05;
            ax[i] = 0.0;
            ay[i] = 0.0;
            az[i] = 0.0;
            mass[i] = 1.0;
            radius[i] = 0.01;
            active[i] = 1;
        }

        eng.set_particles(px.data(), py.data(), pz.data(),
                          vx.data(), vy.data(), vz.data(),
                          ax.data(), ay.data(), az.data(),
                          mass.data(), radius.data(), active.data(), N);

        for (int s = 0; s < steps; s++) {
            eng.step(0.001, 1e-4);
        }
    }
};

TEST_F(DeterminismTest, IdenticalSeed_IdenticalResults) {
    Engine engine1, engine2;

    run_simulation(engine1, 100);
    run_simulation(engine2, 100);

    std::vector<double> px1(N), py1(N), pz1(N);
    std::vector<double> px2(N), py2(N), pz2(N);
    engine1.get_positions(px1.data(), py1.data(), pz1.data(), N);
    engine2.get_positions(px2.data(), py2.data(), pz2.data(), N);

    for (int i = 0; i < N; i++) {
        EXPECT_DOUBLE_EQ(px1[i], px2[i])
            << "Position X differs at body " << i;
        EXPECT_DOUBLE_EQ(py1[i], py2[i])
            << "Position Y differs at body " << i;
        EXPECT_DOUBLE_EQ(pz1[i], pz2[i])
            << "Position Z differs at body " << i;
    }

    std::vector<double> vx1(N), vy1(N), vz1(N);
    std::vector<double> vx2(N), vy2(N), vz2(N);
    engine1.get_velocities(vx1.data(), vy1.data(), vz1.data(), N);
    engine2.get_velocities(vx2.data(), vy2.data(), vz2.data(), N);

    for (int i = 0; i < N; i++) {
        EXPECT_DOUBLE_EQ(vx1[i], vx2[i])
            << "Velocity X differs at body " << i;
        EXPECT_DOUBLE_EQ(vy1[i], vy2[i])
            << "Velocity Y differs at body " << i;
        EXPECT_DOUBLE_EQ(vz1[i], vz2[i])
            << "Velocity Z differs at body " << i;
    }

    engine1.shutdown();
    engine2.shutdown();
}

// ==========================================================================
// Test 5: DeterministicMode unit tests
// ==========================================================================
TEST(DeterministicModeTest, SplitMix64_Deterministic) {
    DeterministicMode dm;
    dm.set_enabled(true);
    dm.set_seed(42);

    u64 h1 = dm.deterministic_hash(0);
    u64 h2 = dm.deterministic_hash(0);
    EXPECT_EQ(h1, h2) << "Same seed+step+channel should give same hash";

    u64 h3 = dm.deterministic_hash(1);
    EXPECT_NE(h1, h3) << "Different channels should give different hashes";

    dm.advance_step();
    u64 h4 = dm.deterministic_hash(0);
    EXPECT_NE(h1, h4) << "Different steps should give different hashes";
}

TEST(DeterministicModeTest, DeterministicDouble_InRange) {
    DeterministicMode dm;
    dm.set_enabled(true);
    dm.set_seed(99);

    for (u64 c = 0; c < 100; c++) {
        double val = dm.deterministic_double(c);
        EXPECT_GE(val, 0.0) << "Channel " << c;
        EXPECT_LT(val, 1.0) << "Channel " << c;
    }
}

// ==========================================================================
// Test 6: Energy tracker unit tests
// ==========================================================================
TEST(EnergyTrackerTest, TwoBody_EnergyComputation) {
    EnergyTracker tracker;

    // Two bodies: m=1 at x=-0.5 and x=0.5, both stationary
    double px[] = {-0.5, 0.5};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double mass[] = {1.0, 1.0};
    uint8_t active[] = {1, 1};

    auto snap = tracker.compute(px, py, pz, vx, vy, vz, mass, active, 2, 0.0);

    // KE = 0 (stationary)
    EXPECT_DOUBLE_EQ(snap.kinetic_energy, 0.0);

    // PE = -G * m1 * m2 / r = -1.0 * 1.0 / 1.0 = -1.0
    EXPECT_NEAR(snap.potential_energy, -1.0, 1e-10);

    // Total = KE + PE
    EXPECT_NEAR(snap.total_energy, -1.0, 1e-10);

    // Momentum = 0 (both stationary)
    EXPECT_NEAR(snap.momentum_magnitude, 0.0, 1e-15);
}

TEST(EnergyTrackerTest, DriftTracking) {
    EnergyTracker tracker;

    EnergyTracker::Snapshot s1;
    s1.total_energy = -1.0;
    s1.momentum_magnitude = 0.0;
    tracker.record(s1);

    EXPECT_DOUBLE_EQ(tracker.energy_drift(), 0.0);

    EnergyTracker::Snapshot s2;
    s2.total_energy = -1.001;
    s2.momentum_magnitude = 0.001;
    tracker.record(s2);

    // Drift = |(E1 - E0) / E0| = |(-1.001 - (-1.0)) / (-1.0)| = 0.001
    EXPECT_NEAR(tracker.energy_drift(), 0.001, 1e-10);
    EXPECT_NEAR(tracker.momentum_drift(), 0.001, 1e-10);
}

// ==========================================================================
// Test 7: Benchmark logger functionality
// ==========================================================================
TEST(BenchmarkLoggerTest, RecordAndAverage) {
    BenchmarkLogger logger;

    for (int i = 0; i < 10; i++) {
        BenchmarkMetrics m;
        m.total_frame_ms = 10.0 + static_cast<double>(i);
        m.body_count = 1000;
        logger.record(m);
    }

    EXPECT_EQ(logger.sample_count(), 10);

    auto avg = logger.average();
    // Average of 10, 11, ..., 19 = 14.5
    EXPECT_NEAR(avg.total_frame_ms, 14.5, 1e-10);
}

TEST(BenchmarkLoggerTest, FPSEstimate) {
    BenchmarkLogger logger;

    BenchmarkMetrics m;
    m.total_frame_ms = 10.0; // 10ms per frame = 100 FPS
    m.body_count = 10000;
    logger.record(m);

    EXPECT_NEAR(logger.estimated_fps(), 100.0, 1e-5);
}

// ==========================================================================
// Test 8: BarnesHut with theta > 0 — approximate but bounded error
// ==========================================================================
class ApproximateBarnesHutTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;

        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        bf_engine.init(cfg);

        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
        cfg.theta = 0.5;
        bh_engine.init(cfg);
    }

    void TearDown() override {
        bf_engine.shutdown();
        bh_engine.shutdown();
    }

    Engine bf_engine, bh_engine;
};

TEST_F(ApproximateBarnesHutTest, BoundedError_50Bodies) {
    const int n = 50;
    std::vector<double> px(n), py(n), pz(n);
    std::vector<double> vx(n), vy(n), vz(n);
    std::vector<double> ax(n), ay(n), az(n);
    std::vector<double> mass(n), radius(n);
    std::vector<uint8_t> active(n);

    for (int i = 0; i < n; i++) {
        px[i] = std::sin(i * 1.0) * 20.0;
        py[i] = std::cos(i * 1.3) * 20.0;
        pz[i] = std::sin(i * 0.7 + 0.5) * 20.0;
        vx[i] = 0.0;
        vy[i] = 0.0;
        vz[i] = 0.0;
        ax[i] = 0.0;
        ay[i] = 0.0;
        az[i] = 0.0;
        mass[i] = 1.0 + std::abs(std::sin(i * 2.0));
        radius[i] = 0.01;
        active[i] = 1;
    }

    bf_engine.set_particles(px.data(), py.data(), pz.data(),
                            vx.data(), vy.data(), vz.data(),
                            ax.data(), ay.data(), az.data(),
                            mass.data(), radius.data(), active.data(), n);

    bh_engine.set_particles(px.data(), py.data(), pz.data(),
                            vx.data(), vy.data(), vz.data(),
                            ax.data(), ay.data(), az.data(),
                            mass.data(), radius.data(), active.data(), n);

    // Run 10 steps
    for (int s = 0; s < 10; s++) {
        bf_engine.step(0.001, 1e-4);
        bh_engine.step(0.001, 1e-4);
    }

    // Compare — with theta=0.5, relative error should be < 5%
    std::vector<double> bf_out_px(n), bf_out_py(n), bf_out_pz(n);
    std::vector<double> bh_out_px(n), bh_out_py(n), bh_out_pz(n);
    bf_engine.get_positions(bf_out_px.data(), bf_out_py.data(), bf_out_pz.data(), n);
    bh_engine.get_positions(bh_out_px.data(), bh_out_py.data(), bh_out_pz.data(), n);

    double max_rel_err = 0.0;
    for (int i = 0; i < n; i++) {
        double dx = bf_out_px[i] - bh_out_px[i];
        double dy = bf_out_py[i] - bh_out_py[i];
        double dz = bf_out_pz[i] - bh_out_pz[i];
        double err = std::sqrt(dx * dx + dy * dy + dz * dz);

        double bf_r = std::sqrt(bf_out_px[i] * bf_out_px[i] +
                                bf_out_py[i] * bf_out_py[i] +
                                bf_out_pz[i] * bf_out_pz[i]);
        if (bf_r > 1e-10) {
            double rel = err / bf_r;
            if (rel > max_rel_err) max_rel_err = rel;
        }
    }

    EXPECT_LT(max_rel_err, 0.05)
        << "Max relative position error between brute-force and BH(theta=0.5): "
        << max_rel_err;
}

// ==========================================================================
// Test 9: Engine lifecycle stress test
// ==========================================================================
TEST(EngineLifecycleTest, MultipleInitShutdownCycles) {
    for (int cycle = 0; cycle < 5; cycle++) {
        Engine eng;
        SimulationConfig cfg;
        cfg.max_particles = 256;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        eng.init(cfg);

        EXPECT_TRUE(eng.is_initialized());

        double px[] = {0.0, 1.0};
        double py[] = {0.0, 0.0};
        double pz[] = {0.0, 0.0};
        double vx[] = {0.0, 0.0};
        double vy[] = {0.0, 0.5};
        double vz[] = {0.0, 0.0};
        double ax[] = {0.0, 0.0};
        double ay[] = {0.0, 0.0};
        double az[] = {0.0, 0.0};
        double mass[] = {1.0, 0.001};
        double radius[] = {0.01, 0.001};
        uint8_t active[] = {1, 1};

        eng.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                          mass, radius, active, 2);

        for (int s = 0; s < 50; s++) {
            eng.step(0.001, 1e-4);
        }

        eng.shutdown();
        EXPECT_FALSE(eng.is_initialized());
    }
}

// ==========================================================================
// Test 10: Inactive bodies do not affect dynamics
// ==========================================================================
TEST(InactiveBodiesTest, InactiveDoNotAffectActive) {
    // Run with 2 active bodies
    Engine eng1;
    SimulationConfig cfg;
    cfg.max_particles = 256;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    eng1.init(cfg);

    {
        double px[] = {0.0, 1.0};
        double py[] = {0.0, 0.0};
        double pz[] = {0.0, 0.0};
        double vx[] = {0.0, 0.0};
        double vy[] = {0.0, 0.5};
        double vz[] = {0.0, 0.0};
        double ax[] = {0.0, 0.0};
        double ay[] = {0.0, 0.0};
        double az[] = {0.0, 0.0};
        double mass[] = {1.0, 0.001};
        double radius[] = {0.01, 0.001};
        uint8_t active[] = {1, 1};

        eng1.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                           mass, radius, active, 2);
    }

    // Run with 2 active + 1 inactive body at same position as body 1
    Engine eng2;
    eng2.init(cfg);

    {
        double px[] = {0.0, 1.0, 0.5};
        double py[] = {0.0, 0.0, 0.5};
        double pz[] = {0.0, 0.0, 0.0};
        double vx[] = {0.0, 0.0, 10.0};
        double vy[] = {0.0, 0.5, 10.0};
        double vz[] = {0.0, 0.0, 0.0};
        double ax[] = {0.0, 0.0, 0.0};
        double ay[] = {0.0, 0.0, 0.0};
        double az[] = {0.0, 0.0, 0.0};
        double mass[] = {1.0, 0.001, 1000.0};
        double radius[] = {0.01, 0.001, 0.01};
        uint8_t active[] = {1, 1, 0}; // Third body inactive

        eng2.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                           mass, radius, active, 3);
    }

    for (int s = 0; s < 100; s++) {
        eng1.step(0.001, 1e-4);
        eng2.step(0.001, 1e-4);
    }

    double px1[2], py1[2], pz1[2];
    double px2[3], py2[3], pz2[3];
    eng1.get_positions(px1, py1, pz1, 2);
    eng2.get_positions(px2, py2, pz2, 3);

    // Active bodies should have identical trajectories
    for (int i = 0; i < 2; i++) {
        EXPECT_DOUBLE_EQ(px1[i], px2[i])
            << "Active body " << i << " pos_x different with inactive present";
        EXPECT_DOUBLE_EQ(py1[i], py2[i])
            << "Active body " << i << " pos_y different with inactive present";
        EXPECT_DOUBLE_EQ(pz1[i], pz2[i])
            << "Active body " << i << " pos_z different with inactive present";
    }

    eng1.shutdown();
    eng2.shutdown();
}

// ==========================================================================
// Test 11: Interop API lifecycle
// ==========================================================================
#include <celestial/interop/native_api.h>

TEST(InteropAPITest, InitStepShutdown) {
    int32_t result = celestial_init(256);
    EXPECT_EQ(result, 0);

    double px[] = {0.0, 1.0};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.5};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1.0, 0.001};
    double radius[] = {0.01, 0.001};
    uint8_t active[] = {1, 1};

    celestial_set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                            mass, radius, active, 2);

    EXPECT_EQ(celestial_get_particle_count(), 2);

    celestial_set_compute_mode(0); // CPU_BruteForce
    celestial_step(0.001, 1e-4);

    double out_px[2], out_py[2], out_pz[2];
    celestial_get_positions(out_px, out_py, out_pz, 2);
    EXPECT_TRUE(std::isfinite(out_px[0]));
    EXPECT_TRUE(std::isfinite(out_px[1]));

    // Test deterministic API
    celestial_set_deterministic(1);
    EXPECT_EQ(celestial_is_deterministic(), 1);
    celestial_set_deterministic_seed(999);
    celestial_set_deterministic(0);
    EXPECT_EQ(celestial_is_deterministic(), 0);

    // Test energy API
    celestial_compute_energy_snapshot();
    double drift = celestial_get_energy_drift();
    EXPECT_TRUE(std::isfinite(drift));

    celestial_shutdown();
}
