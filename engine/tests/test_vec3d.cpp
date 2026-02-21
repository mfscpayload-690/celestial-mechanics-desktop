#include <gtest/gtest.h>
#include <celestial/math/vec3d.hpp>
#include <cmath>

using celestial::math::Vec3d;

TEST(Vec3dTest, DefaultConstructor) {
    Vec3d v;
    EXPECT_DOUBLE_EQ(v.x, 0.0);
    EXPECT_DOUBLE_EQ(v.y, 0.0);
    EXPECT_DOUBLE_EQ(v.z, 0.0);
}

TEST(Vec3dTest, ParameterizedConstructor) {
    Vec3d v(1.0, 2.0, 3.0);
    EXPECT_DOUBLE_EQ(v.x, 1.0);
    EXPECT_DOUBLE_EQ(v.y, 2.0);
    EXPECT_DOUBLE_EQ(v.z, 3.0);
}

TEST(Vec3dTest, Addition) {
    Vec3d a(1.0, 2.0, 3.0);
    Vec3d b(4.0, 5.0, 6.0);
    Vec3d c = a + b;
    EXPECT_DOUBLE_EQ(c.x, 5.0);
    EXPECT_DOUBLE_EQ(c.y, 7.0);
    EXPECT_DOUBLE_EQ(c.z, 9.0);
}

TEST(Vec3dTest, Subtraction) {
    Vec3d a(4.0, 5.0, 6.0);
    Vec3d b(1.0, 2.0, 3.0);
    Vec3d c = a - b;
    EXPECT_DOUBLE_EQ(c.x, 3.0);
    EXPECT_DOUBLE_EQ(c.y, 3.0);
    EXPECT_DOUBLE_EQ(c.z, 3.0);
}

TEST(Vec3dTest, ScalarMultiply) {
    Vec3d v(1.0, 2.0, 3.0);
    Vec3d r = v * 2.0;
    EXPECT_DOUBLE_EQ(r.x, 2.0);
    EXPECT_DOUBLE_EQ(r.y, 4.0);
    EXPECT_DOUBLE_EQ(r.z, 6.0);

    Vec3d l = 3.0 * v;
    EXPECT_DOUBLE_EQ(l.x, 3.0);
    EXPECT_DOUBLE_EQ(l.y, 6.0);
    EXPECT_DOUBLE_EQ(l.z, 9.0);
}

TEST(Vec3dTest, DotProduct) {
    Vec3d a(1.0, 0.0, 0.0);
    Vec3d b(0.0, 1.0, 0.0);
    EXPECT_DOUBLE_EQ(a.dot(b), 0.0);

    Vec3d c(1.0, 2.0, 3.0);
    Vec3d d(4.0, 5.0, 6.0);
    EXPECT_DOUBLE_EQ(c.dot(d), 32.0); // 1*4 + 2*5 + 3*6
}

TEST(Vec3dTest, CrossProduct) {
    Vec3d x = Vec3d::unit_x();
    Vec3d y = Vec3d::unit_y();
    Vec3d z = x.cross(y);
    EXPECT_DOUBLE_EQ(z.x, 0.0);
    EXPECT_DOUBLE_EQ(z.y, 0.0);
    EXPECT_DOUBLE_EQ(z.z, 1.0);
}

TEST(Vec3dTest, Length) {
    Vec3d v(3.0, 4.0, 0.0);
    EXPECT_DOUBLE_EQ(v.length(), 5.0);
    EXPECT_DOUBLE_EQ(v.length_squared(), 25.0);
}

TEST(Vec3dTest, Normalize) {
    Vec3d v(3.0, 4.0, 0.0);
    Vec3d n = v.normalized();
    EXPECT_NEAR(n.x, 0.6, 1e-15);
    EXPECT_NEAR(n.y, 0.8, 1e-15);
    EXPECT_NEAR(n.z, 0.0, 1e-15);
    EXPECT_NEAR(n.length(), 1.0, 1e-15);
}

TEST(Vec3dTest, NormalizeZero) {
    Vec3d v = Vec3d::zero();
    Vec3d n = v.normalized();
    EXPECT_DOUBLE_EQ(n.x, 0.0);
    EXPECT_DOUBLE_EQ(n.y, 0.0);
    EXPECT_DOUBLE_EQ(n.z, 0.0);
}

TEST(Vec3dTest, Distance) {
    Vec3d a(1.0, 0.0, 0.0);
    Vec3d b(4.0, 4.0, 0.0);
    EXPECT_DOUBLE_EQ(Vec3d::distance(a, b), 5.0);
}

TEST(Vec3dTest, Lerp) {
    Vec3d a(0.0, 0.0, 0.0);
    Vec3d b(10.0, 20.0, 30.0);
    Vec3d mid = Vec3d::lerp(a, b, 0.5);
    EXPECT_DOUBLE_EQ(mid.x, 5.0);
    EXPECT_DOUBLE_EQ(mid.y, 10.0);
    EXPECT_DOUBLE_EQ(mid.z, 15.0);
}

TEST(Vec3dTest, CompoundAssignment) {
    Vec3d v(1.0, 2.0, 3.0);
    v += Vec3d(1.0, 1.0, 1.0);
    EXPECT_DOUBLE_EQ(v.x, 2.0);
    v -= Vec3d(0.5, 0.5, 0.5);
    EXPECT_DOUBLE_EQ(v.x, 1.5);
    v *= 2.0;
    EXPECT_DOUBLE_EQ(v.x, 3.0);
}

TEST(Vec3dTest, Negation) {
    Vec3d v(1.0, -2.0, 3.0);
    Vec3d neg = -v;
    EXPECT_DOUBLE_EQ(neg.x, -1.0);
    EXPECT_DOUBLE_EQ(neg.y, 2.0);
    EXPECT_DOUBLE_EQ(neg.z, -3.0);
}
