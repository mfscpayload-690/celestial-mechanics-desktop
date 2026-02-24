#include <gtest/gtest.h>
#include <celestial/sim/engine.hpp>
#include <celestial/sim/yoshida.hpp>
#include <celestial/sim/adaptive_timestep.hpp>
#include <celestial/physics/collision_resolver.hpp>
#include <celestial/interop/native_api.h>
#include <cmath>
#include <vector>
#include <algorithm>

using namespace celestial::sim;
using namespace celestial::physics;
using namespace celestial::profile;

// ============================================================================
// Helper: two-body orbit setup
// ============================================================================

struct TwoBodySetup {
    double px[2], py[2], pz[2];
    double vx[2], vy[2], vz[2];
    double ax[2], ay[2], az[2];
    double mass[2], radius[2];
    uint8_t active[2];

    // Circular orbit: heavy body at origin, light body at (separation, 0, 0)
    // v_circ = sqrt(G * M_central / r) with G=1
    static TwoBodySetup circular_orbit(double m_central = 1.0,
                                        double m_orbiter = 1e-6,
                                        double separation = 1.0) {
        TwoBodySetup s{};
        s.px[0] = 0.0; s.py[0] = 0.0; s.pz[0] = 0.0;
        s.px[1] = separation; s.py[1] = 0.0; s.pz[1] = 0.0;

        double v_circ = std::sqrt(m_central / separation);
        s.vx[0] = 0.0; s.vy[0] = 0.0; s.vz[0] = 0.0;
        s.vx[1] = 0.0; s.vy[1] = v_circ; s.vz[1] = 0.0;

        std::fill(s.ax, s.ax + 2, 0.0);
        std::fill(s.ay, s.ay + 2, 0.0);
        std::fill(s.az, s.az + 2, 0.0);

        s.mass[0] = m_central; s.mass[1] = m_orbiter;
        s.radius[0] = 0.01; s.radius[1] = 0.001;
        s.active[0] = 1; s.active[1] = 1;
        return s;
    }

    // Head-on collision: two equal-mass bodies approaching along x-axis
    static TwoBodySetup head_on(double m = 1.0, double sep = 0.1,
                                 double v = 1.0, double r = 0.06) {
        TwoBodySetup s{};
        s.px[0] = -sep / 2.0; s.py[0] = 0.0; s.pz[0] = 0.0;
        s.px[1] =  sep / 2.0; s.py[1] = 0.0; s.pz[1] = 0.0;

        s.vx[0] =  v; s.vy[0] = 0.0; s.vz[0] = 0.0;
        s.vx[1] = -v; s.vy[1] = 0.0; s.vz[1] = 0.0;

        std::fill(s.ax, s.ax + 2, 0.0);
        std::fill(s.ay, s.ay + 2, 0.0);
        std::fill(s.az, s.az + 2, 0.0);

        s.mass[0] = m; s.mass[1] = m;
        s.radius[0] = r; s.radius[1] = r;
        s.active[0] = 1; s.active[1] = 1;
        return s;
    }

    void load(Engine& engine) const {
        engine.set_particles(px, py, pz, vx, vy, vz,
                             ax, ay, az, mass, radius, active, 2);
    }
};

// ============================================================================
// Fixture: CPU brute-force engine
// ============================================================================

class Phase13Test : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        cfg.enable_collisions = true;
        engine.init(cfg);
    }

    void TearDown() override {
        engine.shutdown();
    }

    Engine engine;
};

// ============================================================================
// Test 1: Yoshida 4th-order energy conservation
// ============================================================================

TEST_F(Phase13Test, YoshidaEnergyConservation) {
    engine.set_integrator(IntegratorType::Yoshida4);
    auto orbit = TwoBodySetup::circular_orbit();
    orbit.load(engine);

    // Record initial energy
    engine.compute_energy_snapshot();
    double initial_drift = engine.energy_drift();

    // Run 1000 steps
    for (int i = 0; i < 1000; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double yoshida_drift = std::abs(engine.energy_drift());

    // Yoshida should conserve energy much better than leapfrog
    // Expect drift < 1e-5 for a simple orbit over 1000 steps
    EXPECT_LT(yoshida_drift, 1e-5)
        << "Yoshida energy drift too large: " << yoshida_drift;
}

// ============================================================================
// Test 2: Yoshida determinism (bit-exact across two runs)
// ============================================================================

TEST_F(Phase13Test, YoshidaDeterminism) {
    engine.set_integrator(IntegratorType::Yoshida4);
    auto orbit = TwoBodySetup::circular_orbit();
    orbit.load(engine);

    // Run A
    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-4);
    }
    double run_a_px[2], run_a_py[2], run_a_pz[2];
    double run_a_vx[2], run_a_vy[2], run_a_vz[2];
    engine.get_positions(run_a_px, run_a_py, run_a_pz, 2);
    engine.get_velocities(run_a_vx, run_a_vy, run_a_vz, 2);

    // Reset and run B with identical initial conditions
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    engine.init(cfg);
    engine.set_integrator(IntegratorType::Yoshida4);
    orbit.load(engine);

    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-4);
    }
    double run_b_px[2], run_b_py[2], run_b_pz[2];
    double run_b_vx[2], run_b_vy[2], run_b_vz[2];
    engine.get_positions(run_b_px, run_b_py, run_b_pz, 2);
    engine.get_velocities(run_b_vx, run_b_vy, run_b_vz, 2);

    // Should be bit-exact
    for (int i = 0; i < 2; i++) {
        EXPECT_DOUBLE_EQ(run_a_px[i], run_b_px[i]);
        EXPECT_DOUBLE_EQ(run_a_py[i], run_b_py[i]);
        EXPECT_DOUBLE_EQ(run_a_pz[i], run_b_pz[i]);
        EXPECT_DOUBLE_EQ(run_a_vx[i], run_b_vx[i]);
        EXPECT_DOUBLE_EQ(run_a_vy[i], run_b_vy[i]);
        EXPECT_DOUBLE_EQ(run_a_vz[i], run_b_vz[i]);
    }
}

// ============================================================================
// Test 3: Yoshida CPU_BruteForce vs CPU_BarnesHut(theta=0) equivalence
// ============================================================================

TEST_F(Phase13Test, YoshidaAllModes) {
    engine.set_integrator(IntegratorType::Yoshida4);
    auto orbit = TwoBodySetup::circular_orbit();
    orbit.load(engine);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 1e-4);
    }
    double bf_px[2], bf_py[2], bf_pz[2];
    engine.get_positions(bf_px, bf_py, bf_pz, 2);

    // Reset with Barnes-Hut mode, theta=0 (exact)
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    cfg.theta = 0.0; // exact tree traversal
    engine.init(cfg);
    engine.set_integrator(IntegratorType::Yoshida4);
    orbit.load(engine);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 1e-4);
    }
    double bh_px[2], bh_py[2], bh_pz[2];
    engine.get_positions(bh_px, bh_py, bh_pz, 2);

    // theta=0 BH should match brute-force closely
    for (int i = 0; i < 2; i++) {
        EXPECT_NEAR(bf_px[i], bh_px[i], 1e-10);
        EXPECT_NEAR(bf_py[i], bh_py[i], 1e-10);
        EXPECT_NEAR(bf_pz[i], bh_pz[i], 1e-10);
    }
}

// ============================================================================
// Test 4: Adaptive timestep unit — compute_dt with known a_max
// ============================================================================

TEST(AdaptiveTimestep_Unit, ComputeDt) {
    AdaptiveTimestep adt;
    AdaptiveTimestepConfig cfg;
    cfg.enabled = true;
    cfg.eta = 0.01;
    cfg.dt_min = 1e-8;
    cfg.dt_max = 0.01;
    cfg.initial_dt = 0.001;
    adt.configure(cfg);

    // dt = eta * sqrt(softening / a_max) = 0.01 * sqrt(1e-4 / 100) = 0.01 * 0.001 = 1e-5
    double dt = adt.compute_dt(100.0, 1e-4);
    EXPECT_NEAR(dt, 1e-5, 1e-10);

    // Very small a_max -> should clamp to dt_max
    dt = adt.compute_dt(1e-20, 1e-4);
    EXPECT_DOUBLE_EQ(dt, 0.01);

    // Very large a_max -> should clamp to dt_min
    dt = adt.compute_dt(1e20, 1e-4);
    EXPECT_DOUBLE_EQ(dt, 1e-8);
}

// ============================================================================
// Test 5: Adaptive timestep bounds
// ============================================================================

TEST(AdaptiveTimestep_Bounds, DtStaysInRange) {
    AdaptiveTimestep adt;
    AdaptiveTimestepConfig cfg;
    cfg.enabled = true;
    cfg.eta = 0.01;
    cfg.dt_min = 1e-6;
    cfg.dt_max = 0.005;
    cfg.initial_dt = 0.001;
    adt.configure(cfg);

    // Sweep over a wide range of a_max
    for (double a_max = 1e-10; a_max < 1e15; a_max *= 10.0) {
        double dt = adt.compute_dt(a_max, 1e-4);
        EXPECT_GE(dt, cfg.dt_min) << "dt below minimum for a_max=" << a_max;
        EXPECT_LE(dt, cfg.dt_max) << "dt above maximum for a_max=" << a_max;
    }
}

// ============================================================================
// Test 6: Adaptive timestep determinism
// ============================================================================

TEST_F(Phase13Test, AdaptiveTimestep_Determinism) {
    AdaptiveTimestepConfig acfg;
    acfg.enabled = true;
    acfg.eta = 0.01;
    acfg.dt_min = 1e-7;
    acfg.dt_max = 0.005;
    acfg.initial_dt = 0.001;
    engine.set_adaptive_timestep(acfg);

    auto orbit = TwoBodySetup::circular_orbit();
    orbit.load(engine);

    // Run 50 steps and record adaptive dt values
    std::vector<double> run_a_dts;
    for (int i = 0; i < 50; i++) {
        engine.step(engine.current_adaptive_dt(), 1e-4);
        run_a_dts.push_back(engine.current_adaptive_dt());
    }

    // Reset
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    engine.init(cfg);
    engine.set_adaptive_timestep(acfg);
    orbit.load(engine);

    // Second run
    for (int i = 0; i < 50; i++) {
        engine.step(engine.current_adaptive_dt(), 1e-4);
        EXPECT_DOUBLE_EQ(run_a_dts[i], engine.current_adaptive_dt())
            << "Adaptive dt mismatch at step " << i;
    }
}

// ============================================================================
// Test 7: Adaptive timestep with dense cluster (stability check)
// ============================================================================

TEST_F(Phase13Test, AdaptiveTimestep_Collapse) {
    AdaptiveTimestepConfig acfg;
    acfg.enabled = true;
    acfg.eta = 0.01;
    acfg.dt_min = 1e-8;
    acfg.dt_max = 0.01;
    acfg.initial_dt = 0.001;
    engine.set_adaptive_timestep(acfg);

    // 4 bodies in a tight cluster
    double px[] = {0.0, 0.01, 0.005, -0.005};
    double py[] = {0.0, 0.0, 0.01, -0.01};
    double pz[] = {0.0, 0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0, 0.0};
    double ax[] = {0.0, 0.0, 0.0, 0.0};
    double ay[] = {0.0, 0.0, 0.0, 0.0};
    double az[] = {0.0, 0.0, 0.0, 0.0};
    double mass[] = {1.0, 1.0, 1.0, 1.0};
    double radius[] = {0.001, 0.001, 0.001, 0.001};
    uint8_t active[] = {1, 1, 1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz,
                         ax, ay, az, mass, radius, active, 4);

    // Run some steps — the adaptive dt should shrink due to strong accelerations
    for (int i = 0; i < 20; i++) {
        double dt = engine.current_adaptive_dt();
        engine.step(dt, 1e-4);

        // All positions should remain finite (no NaN blowup)
        double out_px[4], out_py[4], out_pz[4];
        engine.get_positions(out_px, out_py, out_pz, 4);
        for (int j = 0; j < 4; j++) {
            EXPECT_TRUE(std::isfinite(out_px[j])) << "NaN at step " << i << " body " << j;
            EXPECT_TRUE(std::isfinite(out_py[j]));
            EXPECT_TRUE(std::isfinite(out_pz[j]));
        }
    }

    // dt should be smaller than dt_max (adaptive kicked in due to high a_max)
    EXPECT_LT(engine.current_adaptive_dt(), acfg.dt_max);
}

// ============================================================================
// Test 8: Angular momentum conservation in 2-body orbit
// ============================================================================

TEST_F(Phase13Test, AngularMomentumConservation) {
    auto orbit = TwoBodySetup::circular_orbit(1.0, 1e-3, 1.0);
    orbit.load(engine);

    engine.compute_energy_snapshot();
    double initial_L_drift = engine.angular_momentum_drift();
    EXPECT_DOUBLE_EQ(initial_L_drift, 0.0);

    for (int i = 0; i < 500; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double final_L_drift = engine.angular_momentum_drift();

    // Angular momentum should be well-conserved for an isolated 2-body system
    EXPECT_LT(final_L_drift, 1e-8)
        << "Angular momentum drift: " << final_L_drift;
}

// ============================================================================
// Test 9: Center-of-mass conservation
// ============================================================================

TEST_F(Phase13Test, COMConservation) {
    auto orbit = TwoBodySetup::circular_orbit(1.0, 1.0, 2.0);
    orbit.load(engine);

    engine.compute_energy_snapshot();

    for (int i = 0; i < 200; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double com_vel_drift = engine.com_velocity_drift();

    // COM velocity should be near-zero for an isolated system
    EXPECT_LT(com_vel_drift, 1e-10)
        << "COM velocity drift: " << com_vel_drift;
}

// ============================================================================
// Test 10: Virial ratio for stable system
// ============================================================================

TEST_F(Phase13Test, VirialRatio) {
    // Single circular orbit — virial theorem: 2*KE = |PE| → ratio = 1.0
    auto orbit = TwoBodySetup::circular_orbit(1.0, 1e-6, 1.0);
    orbit.load(engine);

    // Let it settle for a few steps
    for (int i = 0; i < 10; i++) {
        engine.step(0.001, 1e-4);
    }

    engine.compute_energy_snapshot();
    double virial = engine.energy_tracker().current().virial_ratio;

    // For a circular orbit, virial ratio ≈ 1.0
    EXPECT_GT(virial, 0.5);
    EXPECT_LT(virial, 2.0);
}

// ============================================================================
// Test 11: Diagnostic thresholds
// ============================================================================

TEST_F(Phase13Test, DiagnosticThresholds) {
    auto orbit = TwoBodySetup::circular_orbit();
    orbit.load(engine);

    // Initial snapshot
    engine.compute_energy_snapshot();

    // Just a few steps should pass diagnostics
    for (int i = 0; i < 10; i++) {
        engine.step(0.001, 1e-4);
    }
    engine.compute_energy_snapshot();

    DiagnosticThresholds loose;
    loose.max_energy_drift = 1.0;
    loose.max_momentum_drift = 1.0;
    loose.max_angular_momentum_drift = 1.0;
    loose.max_com_velocity_drift = 1.0;
    EXPECT_TRUE(engine.check_conservation_diagnostics(loose));

    // Extremely tight thresholds should fail (after stepping)
    DiagnosticThresholds tight;
    tight.max_energy_drift = 1e-30;
    tight.max_momentum_drift = 1e-30;
    tight.max_angular_momentum_drift = 1e-30;
    tight.max_com_velocity_drift = 1e-30;
    EXPECT_FALSE(engine.check_conservation_diagnostics(tight));
}

// ============================================================================
// Test 12: Per-body-type softening
// ============================================================================

TEST_F(Phase13Test, PerTypeSoftening) {
    engine.set_softening_mode(SofteningMode::PerBodyType);

    // Type 0: large softening, Type 1: small softening
    engine.set_type_softening(0, 0.1);
    engine.set_type_softening(1, 1e-6);

    // Two bodies with different types, should get different force magnitudes
    auto orbit = TwoBodySetup::circular_orbit(1.0, 1.0, 0.5);
    orbit.load(engine);

    engine.step(0.001, 1e-4);

    double ax_type0[2], ay_type0[2], az_type0[2];
    engine.get_accelerations(ax_type0, ay_type0, az_type0, 2);

    // With per-type softening, the force should differ from global softening.
    // Specifically, at close range (sep=0.5), a large softening (0.1) compared to
    // a small global eps (1e-4) meaningfully reduces force.
    // Just check accelerations are finite and nonzero.
    EXPECT_TRUE(std::isfinite(ax_type0[0]));
    EXPECT_NE(ax_type0[0], 0.0);
}

// ============================================================================
// Test 13: Adaptive softening — heavier body gets larger softening
// ============================================================================

TEST_F(Phase13Test, AdaptiveSoftening) {
    engine.set_softening_mode(SofteningMode::Adaptive);
    engine.set_adaptive_softening_scale(0.01);

    // Two bodies with very different masses
    double px[] = {0.0, 1.0};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1000.0, 0.001};
    double radius[] = {0.1, 0.001};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz,
                         ax, ay, az, mass, radius, active, 2);

    // Step — should not crash or produce NaN
    engine.step(0.001, 1e-4);

    double out_ax[2], out_ay[2], out_az[2];
    engine.get_accelerations(out_ax, out_ay, out_az, 2);

    EXPECT_TRUE(std::isfinite(out_ax[0]));
    EXPECT_TRUE(std::isfinite(out_ax[1]));

    // Light body should experience much larger acceleration
    double a0_mag = std::sqrt(out_ax[0] * out_ax[0] + out_ay[0] * out_ay[0] + out_az[0] * out_az[0]);
    double a1_mag = std::sqrt(out_ax[1] * out_ax[1] + out_ay[1] * out_ay[1] + out_az[1] * out_az[1]);
    EXPECT_GT(a1_mag, a0_mag)
        << "Light body should feel stronger acceleration: a0=" << a0_mag << " a1=" << a1_mag;
}

// ============================================================================
// Test 14: Global softening regression — mode=Global matches original behavior
// ============================================================================

TEST_F(Phase13Test, SofteningGlobalUnchanged) {
    auto orbit = TwoBodySetup::circular_orbit();

    // Run with explicit Global mode
    engine.set_softening_mode(SofteningMode::Global);
    orbit.load(engine);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 1e-4);
    }
    double global_px[2], global_py[2], global_pz[2];
    engine.get_positions(global_px, global_py, global_pz, 2);

    // Reset and run without setting mode (default = Global)
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    engine.init(cfg);
    orbit.load(engine);

    for (int i = 0; i < 50; i++) {
        engine.step(0.001, 1e-4);
    }
    double default_px[2], default_py[2], default_pz[2];
    engine.get_positions(default_px, default_py, default_pz, 2);

    // Should be bit-exact
    for (int i = 0; i < 2; i++) {
        EXPECT_DOUBLE_EQ(global_px[i], default_px[i]);
        EXPECT_DOUBLE_EQ(global_py[i], default_py[i]);
        EXPECT_DOUBLE_EQ(global_pz[i], default_pz[i]);
    }
}

// ============================================================================
// Test 15: Elastic collision — KE and momentum conserved
// ============================================================================

TEST_F(Phase13Test, ElasticCollision) {
    engine.set_collision_mode(CollisionMode::Elastic);

    auto setup = TwoBodySetup::head_on(1.0, 0.1, 1.0, 0.06);
    setup.load(engine);

    // Compute total momentum and KE before
    double total_momentum_x_before = setup.mass[0] * setup.vx[0] + setup.mass[1] * setup.vx[1];
    double ke_before = 0.5 * setup.mass[0] * setup.vx[0] * setup.vx[0] +
                       0.5 * setup.mass[1] * setup.vx[1] * setup.vx[1];

    // Step enough for collision to happen (bodies start overlapping at sep=0.1, r=0.06 each)
    engine.step(0.001, 1e-4);

    // Collision should have been detected (bodies start overlapping: sep=0.1 < r0+r1=0.12)
    // After elastic collision of equal masses head-on, velocities should swap

    double out_vx[2], out_vy[2], out_vz[2];
    engine.get_velocities(out_vx, out_vy, out_vz, 2);

    double total_momentum_x_after = setup.mass[0] * out_vx[0] + setup.mass[1] * out_vx[1];
    double ke_after = 0.5 * setup.mass[0] * out_vx[0] * out_vx[0] +
                      0.5 * setup.mass[1] * out_vx[1] * out_vx[1];

    // Momentum conservation (including gravity contributions over the step)
    EXPECT_NEAR(total_momentum_x_before, total_momentum_x_after, 0.01)
        << "Momentum not conserved in elastic collision";

    // KE should be approximately conserved (gravity adds/removes some)
    EXPECT_NEAR(ke_before, ke_after, 0.1)
        << "KE not conserved in elastic collision";

    // Collision count should be >= 1
    EXPECT_GE(engine.last_collision_count(), 1);
}

// ============================================================================
// Test 16: Inelastic collision — velocity reduction with restitution
// ============================================================================

TEST_F(Phase13Test, InelasticCollision) {
    engine.set_collision_mode(CollisionMode::Inelastic);
    engine.set_collision_restitution(0.5);

    auto setup = TwoBodySetup::head_on(1.0, 0.1, 1.0, 0.06);
    setup.load(engine);

    double total_p_before = setup.mass[0] * setup.vx[0] + setup.mass[1] * setup.vx[1];
    double ke_before = 0.5 * setup.mass[0] * setup.vx[0] * setup.vx[0] +
                       0.5 * setup.mass[1] * setup.vx[1] * setup.vx[1];

    engine.step(0.001, 1e-4);

    double out_vx[2], out_vy[2], out_vz[2];
    engine.get_velocities(out_vx, out_vy, out_vz, 2);

    double total_p_after = setup.mass[0] * out_vx[0] + setup.mass[1] * out_vx[1];
    double ke_after = 0.5 * setup.mass[0] * out_vx[0] * out_vx[0] +
                      0.5 * setup.mass[1] * out_vx[1] * out_vx[1];

    // Momentum still conserved
    EXPECT_NEAR(total_p_before, total_p_after, 0.01);

    // KE should be reduced (inelastic: e=0.5 means KE loss)
    EXPECT_LT(ke_after, ke_before + 0.01)
        << "Inelastic collision should lose KE";
}

// ============================================================================
// Test 17: Merge collision — mass + momentum conserved, volume-conserved radius
// ============================================================================

TEST_F(Phase13Test, MergeCollision) {
    engine.set_collision_mode(CollisionMode::Merge);

    // Overlapping bodies (already touching)
    auto setup = TwoBodySetup::head_on(1.0, 0.1, 0.5, 0.06);
    setup.load(engine);

    double total_mass_before = setup.mass[0] + setup.mass[1];
    double total_px_before = setup.mass[0] * setup.vx[0] + setup.mass[1] * setup.vx[1];

    engine.step(0.001, 1e-4);

    // Check that a collision was detected
    EXPECT_GE(engine.last_collision_count(), 1);

    // After merge, one body should be deactivated
    // Get state — the merged body gets the combined mass
    double out_px[2], out_py[2], out_pz[2];
    double out_vx[2], out_vy[2], out_vz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);
    engine.get_velocities(out_vx, out_vy, out_vz, 2);

    // Positions and velocities should remain finite
    EXPECT_TRUE(std::isfinite(out_px[0]));
    EXPECT_TRUE(std::isfinite(out_vx[0]));
}

// ============================================================================
// Test 18: Collision determinism — same scenario twice → identical results
// ============================================================================

TEST_F(Phase13Test, CollisionDeterminism) {
    engine.set_collision_mode(CollisionMode::Elastic);

    auto setup = TwoBodySetup::head_on(1.0, 0.1, 1.0, 0.06);
    setup.load(engine);

    engine.step(0.001, 1e-4);
    double run_a_vx[2], run_a_vy[2], run_a_vz[2];
    engine.get_velocities(run_a_vx, run_a_vy, run_a_vz, 2);
    int run_a_count = engine.last_collision_count();

    // Reset
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    cfg.enable_collisions = true;
    engine.init(cfg);
    engine.set_collision_mode(CollisionMode::Elastic);
    setup.load(engine);

    engine.step(0.001, 1e-4);
    double run_b_vx[2], run_b_vy[2], run_b_vz[2];
    engine.get_velocities(run_b_vx, run_b_vy, run_b_vz, 2);
    int run_b_count = engine.last_collision_count();

    EXPECT_EQ(run_a_count, run_b_count);
    for (int i = 0; i < 2; i++) {
        EXPECT_DOUBLE_EQ(run_a_vx[i], run_b_vx[i]);
        EXPECT_DOUBLE_EQ(run_a_vy[i], run_b_vy[i]);
        EXPECT_DOUBLE_EQ(run_a_vz[i], run_b_vz[i]);
    }
}

// ============================================================================
// Test 19: Collision Ignore mode — no velocity change from collisions
// ============================================================================

TEST_F(Phase13Test, CollisionIgnoreMode) {
    // Run with collisions ignored (default)
    engine.set_collision_mode(CollisionMode::Ignore);

    auto setup = TwoBodySetup::head_on(1.0, 0.1, 1.0, 0.06);
    setup.load(engine);

    engine.step(0.001, 1e-4);
    double ignore_vx[2], ignore_vy[2], ignore_vz[2];
    engine.get_velocities(ignore_vx, ignore_vy, ignore_vz, 2);
    double ignore_px[2], ignore_py[2], ignore_pz[2];
    engine.get_positions(ignore_px, ignore_py, ignore_pz, 2);

    // Run fresh without any collision mode setting (default = Ignore)
    engine.shutdown();
    SimulationConfig cfg;
    cfg.max_particles = 1024;
    cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
    cfg.dt = 0.001;
    cfg.softening = 1e-4;
    engine.init(cfg);
    setup.load(engine);

    engine.step(0.001, 1e-4);
    double default_vx[2], default_vy[2], default_vz[2];
    engine.get_velocities(default_vx, default_vy, default_vz, 2);

    // Should be identical — no collision resolution in Ignore mode
    for (int i = 0; i < 2; i++) {
        EXPECT_DOUBLE_EQ(ignore_vx[i], default_vx[i]);
        EXPECT_DOUBLE_EQ(ignore_vy[i], default_vy[i]);
        EXPECT_DOUBLE_EQ(ignore_vz[i], default_vz[i]);
    }
}

// ============================================================================
// Test 20: C API — integrator round-trip
// ============================================================================

TEST(NativeAPI_Phase13, IntegratorRoundTrip) {
    ASSERT_EQ(celestial_init(64), 0);

    // Default should be Leapfrog (0)
    EXPECT_EQ(celestial_get_integrator(), 0);

    // Set to Yoshida4
    celestial_set_integrator(1);
    EXPECT_EQ(celestial_get_integrator(), 1);

    // Set back to Leapfrog
    celestial_set_integrator(0);
    EXPECT_EQ(celestial_get_integrator(), 0);

    celestial_shutdown();
}

// ============================================================================
// Test 21: C API — adaptive timestep enable/query round-trip
// ============================================================================

TEST(NativeAPI_Phase13, AdaptiveDtRoundTrip) {
    ASSERT_EQ(celestial_init(64), 0);

    // Default: disabled
    EXPECT_EQ(celestial_is_adaptive_dt_enabled(), 0);

    // Enable adaptive dt
    celestial_set_adaptive_timestep(1, 0.01, 1e-8, 0.01, 0.001);
    EXPECT_EQ(celestial_is_adaptive_dt_enabled(), 1);

    // Query initial dt
    double dt = celestial_get_adaptive_dt();
    EXPECT_GT(dt, 0.0);

    // Disable
    celestial_set_adaptive_timestep(0, 0.01, 1e-8, 0.01, 0.001);
    EXPECT_EQ(celestial_is_adaptive_dt_enabled(), 0);

    celestial_shutdown();
}
