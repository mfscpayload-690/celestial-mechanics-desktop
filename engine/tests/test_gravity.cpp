#include <gtest/gtest.h>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/physics/particle_system.hpp>
#include <cmath>

using namespace celestial::physics;

class GravityTest : public ::testing::Test {
protected:
    void SetUp() override {
        particles.allocate(1024);
    }
    ParticleSystem particles;
};

TEST_F(GravityTest, TwoBodyNewtonianForce) {
    // Two equal masses separated by distance 1
    particles.set_count(2);
    particles.pos_x[0] = -0.5; particles.pos_y[0] = 0.0; particles.pos_z[0] = 0.0;
    particles.pos_x[1] =  0.5; particles.pos_y[1] = 0.0; particles.pos_z[1] = 0.0;
    particles.mass[0] = 1.0;
    particles.mass[1] = 1.0;
    particles.is_active[0] = 1;
    particles.is_active[1] = 1;

    BarnesHutSolver solver;
    solver.theta = 0.0; // Exact mode
    solver.use_parallel = false;
    solver.compute_forces(particles, 0.0);

    // G=1, m1=m2=1, r=1
    // Force on body 0 from body 1: F = G*m1*m2/r^2 = 1
    // Direction: from 0 toward 1 = +x
    // acc_x[0] should be positive (pulled toward body 1)
    EXPECT_GT(particles.acc_x[0], 0.0);
    // acc_x[1] should be negative (pulled toward body 0)
    EXPECT_LT(particles.acc_x[1], 0.0);

    // Magnitude check: |a| = G*m/r^2 = 1.0
    EXPECT_NEAR(std::abs(particles.acc_x[0]), 1.0, 1e-10);
    EXPECT_NEAR(std::abs(particles.acc_x[1]), 1.0, 1e-10);

    // Newton's third law: equal and opposite
    EXPECT_NEAR(particles.acc_x[0] + particles.acc_x[1], 0.0, 1e-10);
}

TEST_F(GravityTest, SofteningPreventsInfinity) {
    // Two bodies at same position
    particles.set_count(2);
    particles.pos_x[0] = 0.0; particles.pos_y[0] = 0.0; particles.pos_z[0] = 0.0;
    particles.pos_x[1] = 0.0; particles.pos_y[1] = 0.0; particles.pos_z[1] = 0.0;
    particles.mass[0] = 1.0;
    particles.mass[1] = 1.0;
    particles.is_active[0] = 1;
    particles.is_active[1] = 1;

    BarnesHutSolver solver;
    solver.theta = 0.0;
    solver.compute_forces(particles, 0.01); // softening = 0.01

    // Should not be infinite or NaN
    EXPECT_TRUE(std::isfinite(particles.acc_x[0]));
    EXPECT_TRUE(std::isfinite(particles.acc_y[0]));
    EXPECT_TRUE(std::isfinite(particles.acc_z[0]));
}

TEST_F(GravityTest, BarnesHutVsBruteForce) {
    // Set up 20 bodies in random positions
    int n = 20;
    particles.set_count(n);
    for (int i = 0; i < n; i++) {
        particles.pos_x[i] = std::sin(i * 1.0) * 10.0;
        particles.pos_y[i] = std::cos(i * 1.3) * 10.0;
        particles.pos_z[i] = std::sin(i * 0.7) * 10.0;
        particles.mass[i] = 1.0 + std::abs(std::sin(i * 2.0));
        particles.is_active[i] = 1;
    }

    // Compute with BH theta=0 (exact)
    BarnesHutSolver exact_solver;
    exact_solver.theta = 0.0;
    exact_solver.use_parallel = false;
    exact_solver.compute_forces(particles, 1e-4);

    // Save exact results
    std::vector<double> exact_ax(n), exact_ay(n), exact_az(n);
    for (int i = 0; i < n; i++) {
        exact_ax[i] = particles.acc_x[i];
        exact_ay[i] = particles.acc_y[i];
        exact_az[i] = particles.acc_z[i];
    }

    // Compute with BH theta=0.5 (approximate)
    BarnesHutSolver bh_solver;
    bh_solver.theta = 0.5;
    bh_solver.use_parallel = false;
    bh_solver.compute_forces(particles, 1e-4);

    // Compare — should be within ~1% for theta=0.5
    for (int i = 0; i < n; i++) {
        if (std::abs(exact_ax[i]) > 1e-10) {
            double rel_err = std::abs((particles.acc_x[i] - exact_ax[i]) / exact_ax[i]);
            EXPECT_LT(rel_err, 0.05) << "Body " << i << " ax relative error too large";
        }
    }
}

TEST_F(GravityTest, InactiveBodiesSkipped) {
    particles.set_count(3);
    particles.pos_x[0] = 0.0; particles.pos_y[0] = 0.0; particles.pos_z[0] = 0.0;
    particles.pos_x[1] = 1.0; particles.pos_y[1] = 0.0; particles.pos_z[1] = 0.0;
    particles.pos_x[2] = 2.0; particles.pos_y[2] = 0.0; particles.pos_z[2] = 0.0;
    particles.mass[0] = 1.0;
    particles.mass[1] = 1.0;
    particles.mass[2] = 1.0;
    particles.is_active[0] = 1;
    particles.is_active[1] = 0; // Inactive
    particles.is_active[2] = 1;

    BarnesHutSolver solver;
    solver.theta = 0.0;
    solver.compute_forces(particles, 0.0);

    // Body 1 is inactive — should have zero acceleration
    EXPECT_DOUBLE_EQ(particles.acc_x[1], 0.0);
    EXPECT_DOUBLE_EQ(particles.acc_y[1], 0.0);
    EXPECT_DOUBLE_EQ(particles.acc_z[1], 0.0);
}
