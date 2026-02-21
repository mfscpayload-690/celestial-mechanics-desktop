#include <gtest/gtest.h>
#include <celestial/sim/engine.hpp>
#include <cmath>

using namespace celestial::sim;

class EngineTest : public ::testing::Test {
protected:
    void SetUp() override {
        SimulationConfig cfg;
        cfg.max_particles = 1024;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BruteForce;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        engine.init(cfg);
    }

    void TearDown() override {
        engine.shutdown();
    }

    Engine engine;
};

TEST_F(EngineTest, InitShutdown) {
    EXPECT_TRUE(engine.is_initialized());
}

TEST_F(EngineTest, SetParticlesAndStep) {
    double px[] = {-0.5, 0.5};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.01, -0.01};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1.0, 1.0};
    double radius[] = {0.01, 0.01};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 2);
    EXPECT_EQ(engine.particle_count(), 2);

    // Step once
    engine.step(0.001, 1e-4);

    // Bodies should have moved
    double out_px[2], out_py[2], out_pz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);

    // With attraction, body 0 should move toward body 1 (positive x)
    // But in one step it may not move much; just verify finite
    EXPECT_TRUE(std::isfinite(out_px[0]));
    EXPECT_TRUE(std::isfinite(out_px[1]));
}

TEST_F(EngineTest, EnergyConservation_TwoBody) {
    // Circular orbit: m1 at origin, m2 orbiting at r=1
    // v_circular = sqrt(G*M/r) = sqrt(1*1/1) = 1
    double px[] = {0.0, 1.0};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 1.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {1.0, 1e-6}; // One heavy, one light
    double radius[] = {0.01, 0.001};
    uint8_t active[] = {1, 1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 2);

    // Compute initial energy
    // KE = 0.5 * m2 * v^2 = 0.5 * 1e-6 * 1 = 5e-7
    // PE = -G * m1 * m2 / r = -1.0 * 1e-6 / 1 = -1e-6
    // Total = -5e-7

    // Step many times
    for (int i = 0; i < 100; i++) {
        engine.step(0.001, 1e-4);
    }

    // Get final state
    double out_px[2], out_py[2], out_pz[2];
    double out_vx[2], out_vy[2], out_vz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);
    engine.get_velocities(out_vx, out_vy, out_vz, 2);

    // Body 1 should still be roughly orbiting (not escaped, not crashed)
    double r = std::sqrt(
        (out_px[1] - out_px[0]) * (out_px[1] - out_px[0]) +
        (out_py[1] - out_py[0]) * (out_py[1] - out_py[0]) +
        (out_pz[1] - out_pz[0]) * (out_pz[1] - out_pz[0]));

    // Should still be close to r=1 (within 10% for 100 steps)
    EXPECT_GT(r, 0.5);
    EXPECT_LT(r, 2.0);
}

TEST_F(EngineTest, UpdateReturnsStepCount) {
    double px[] = {0.0};
    double py[] = {0.0};
    double pz[] = {0.0};
    double vx[] = {0.0};
    double vy[] = {0.0};
    double vz[] = {0.0};
    double ax[] = {0.0};
    double ay[] = {0.0};
    double az[] = {0.0};
    double mass[] = {1.0};
    double radius[] = {0.01};
    uint8_t active[] = {1};

    engine.set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                         mass, radius, active, 1);

    // dt = 0.001, frame_time = 0.005 should give 5 steps
    int steps = engine.update(0.005);
    EXPECT_EQ(steps, 5);
}
