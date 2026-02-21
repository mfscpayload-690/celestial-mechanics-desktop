#include <gtest/gtest.h>
#include <celestial/physics/octree_pool.hpp>
#include <celestial/physics/octree_builder.hpp>
#include <celestial/physics/particle_system.hpp>

using namespace celestial::physics;

class OctreeTest : public ::testing::Test {
protected:
    void SetUp() override {
        particles.allocate(100);
    }

    ParticleSystem particles;
};

TEST_F(OctreeTest, EmptyBuild) {
    OctreePool pool(64);
    OctreeBuilder builder;
    i32 root = builder.build(pool, particles);
    EXPECT_EQ(root, -1); // No particles -> no tree
}

TEST_F(OctreeTest, SingleBody) {
    particles.set_count(1);
    particles.pos_x[0] = 1.0;
    particles.pos_y[0] = 2.0;
    particles.pos_z[0] = 3.0;
    particles.mass[0] = 10.0;
    particles.is_active[0] = 1;

    OctreePool pool(64);
    OctreeBuilder builder;
    i32 root = builder.build(pool, particles);

    ASSERT_GE(root, 0);
    EXPECT_TRUE(pool[root].is_leaf);
    EXPECT_DOUBLE_EQ(pool[root].total_mass, 10.0);
    EXPECT_DOUBLE_EQ(pool[root].com_x, 1.0);
    EXPECT_DOUBLE_EQ(pool[root].com_y, 2.0);
    EXPECT_DOUBLE_EQ(pool[root].com_z, 3.0);
}

TEST_F(OctreeTest, TwoBodies) {
    particles.set_count(2);
    particles.pos_x[0] = -1.0; particles.pos_y[0] = 0.0; particles.pos_z[0] = 0.0;
    particles.pos_x[1] = 1.0;  particles.pos_y[1] = 0.0; particles.pos_z[1] = 0.0;
    particles.mass[0] = 5.0;
    particles.mass[1] = 5.0;
    particles.is_active[0] = 1;
    particles.is_active[1] = 1;

    OctreePool pool(64);
    OctreeBuilder builder;
    i32 root = builder.build(pool, particles);

    ASSERT_GE(root, 0);
    // Root should be internal (two bodies were subdivided)
    EXPECT_FALSE(pool[root].is_leaf);
    EXPECT_DOUBLE_EQ(pool[root].total_mass, 10.0);
    // COM should be at origin (equal masses, symmetric positions)
    EXPECT_NEAR(pool[root].com_x, 0.0, 1e-10);
}

TEST_F(OctreeTest, ManyBodies) {
    int n = 50;
    particles.set_count(n);
    for (int i = 0; i < n; i++) {
        particles.pos_x[i] = static_cast<double>(i) * 0.1;
        particles.pos_y[i] = static_cast<double>(i) * 0.2;
        particles.pos_z[i] = static_cast<double>(i) * 0.05;
        particles.mass[i] = 1.0;
        particles.is_active[i] = 1;
    }

    OctreePool pool(256);
    OctreeBuilder builder;
    i32 root = builder.build(pool, particles);

    ASSERT_GE(root, 0);
    EXPECT_DOUBLE_EQ(pool[root].total_mass, static_cast<double>(n));
}

TEST_F(OctreeTest, PoolReset) {
    OctreePool pool(64);
    pool.allocate(0.0, 0.0, 0.0, 10.0);
    pool.allocate(1.0, 1.0, 1.0, 5.0);
    EXPECT_EQ(pool.count(), 2);

    pool.reset();
    EXPECT_EQ(pool.count(), 0);

    // Should be able to reuse
    pool.allocate(2.0, 2.0, 2.0, 3.0);
    EXPECT_EQ(pool.count(), 1);
}

TEST_F(OctreeTest, OctantDetermination) {
    OctreeNode node;
    node.init(0.0, 0.0, 0.0, 10.0);

    EXPECT_EQ(node.octant_for(-1.0, -1.0, -1.0), 0);
    EXPECT_EQ(node.octant_for( 1.0, -1.0, -1.0), 1);
    EXPECT_EQ(node.octant_for(-1.0,  1.0, -1.0), 2);
    EXPECT_EQ(node.octant_for( 1.0,  1.0, -1.0), 3);
    EXPECT_EQ(node.octant_for(-1.0, -1.0,  1.0), 4);
    EXPECT_EQ(node.octant_for( 1.0, -1.0,  1.0), 5);
    EXPECT_EQ(node.octant_for(-1.0,  1.0,  1.0), 6);
    EXPECT_EQ(node.octant_for( 1.0,  1.0,  1.0), 7);
}
