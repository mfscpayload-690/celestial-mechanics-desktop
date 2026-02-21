#include <gtest/gtest.h>
#include <celestial/sim/timestep.hpp>
#include <cmath>

using celestial::sim::Timestep;

TEST(TimestepTest, DefaultValues) {
    Timestep ts;
    EXPECT_DOUBLE_EQ(ts.fixed_dt, 0.001);
    EXPECT_DOUBLE_EQ(ts.current_time, 0.0);
    EXPECT_DOUBLE_EQ(ts.accumulator, 0.0);
    EXPECT_EQ(ts.max_steps_per_frame, 10);
}

TEST(TimestepTest, SingleStep) {
    Timestep ts;
    ts.fixed_dt = 0.01;

    int steps = ts.update(0.01);
    EXPECT_EQ(steps, 1);
    EXPECT_NEAR(ts.current_time, 0.01, 1e-15);
}

TEST(TimestepTest, MultipleSteps) {
    Timestep ts;
    ts.fixed_dt = 0.01;

    int steps = ts.update(0.035);
    EXPECT_EQ(steps, 3);
    EXPECT_NEAR(ts.current_time, 0.03, 1e-15);
    EXPECT_NEAR(ts.accumulator, 0.005, 1e-15);
}

TEST(TimestepTest, MaxStepsCap) {
    Timestep ts;
    ts.fixed_dt = 0.001;
    ts.max_steps_per_frame = 5;

    int steps = ts.update(0.1); // Would be 100 steps without cap
    EXPECT_EQ(steps, 5);
    EXPECT_NEAR(ts.current_time, 0.005, 1e-15);
}

TEST(TimestepTest, InterpolationAlpha) {
    Timestep ts;
    ts.fixed_dt = 0.01;

    ts.update(0.015); // 1 step, 0.005 remainder
    EXPECT_NEAR(ts.interpolation_alpha, 0.5, 1e-10);
}

TEST(TimestepTest, ZeroFrameTime) {
    Timestep ts;
    ts.fixed_dt = 0.01;

    int steps = ts.update(0.0);
    EXPECT_EQ(steps, 0);
    EXPECT_DOUBLE_EQ(ts.current_time, 0.0);
}

TEST(TimestepTest, SmallFrameTimeAccumulates) {
    Timestep ts;
    ts.fixed_dt = 0.01;

    // First call: not enough for a step
    int steps1 = ts.update(0.005);
    EXPECT_EQ(steps1, 0);

    // Second call: accumulated enough
    int steps2 = ts.update(0.006);
    EXPECT_EQ(steps2, 1);
    EXPECT_NEAR(ts.current_time, 0.01, 1e-15);
}

TEST(TimestepTest, Reset) {
    Timestep ts;
    ts.fixed_dt = 0.01;
    ts.update(0.05);

    ts.reset();
    EXPECT_DOUBLE_EQ(ts.current_time, 0.0);
    EXPECT_DOUBLE_EQ(ts.accumulator, 0.0);
    EXPECT_DOUBLE_EQ(ts.interpolation_alpha, 0.0);
}
