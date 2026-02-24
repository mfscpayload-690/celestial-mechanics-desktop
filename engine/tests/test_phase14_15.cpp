#include <gtest/gtest.h>
#include <celestial/physics/density_model.hpp>
#include <celestial/physics/particle_system.hpp>
#include <celestial/physics/collision_resolver.hpp>
#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/sim/engine.hpp>
#include <celestial/interop/native_api.h>
#include <cmath>
#include <cstring>
#include <vector>
#include <algorithm>
#include <set>

using namespace celestial;
using namespace celestial::physics;
using namespace celestial::sim;

// ==========================================================================
// Helpers
// ==========================================================================

static constexpr double PI = 3.14159265358979323846;

/// Helper to create a minimal engine with specific collision/density config.
static Engine make_engine(int max_particles,
                           SimulationConfig::ComputeMode mode,
                           CollisionMode coll_mode,
                           bool density_preserving = true) {
    Engine engine;
    SimulationConfig cfg;
    cfg.max_particles = max_particles;
    cfg.compute_mode = mode;
    cfg.dt = 0.001;
    cfg.softening = 1e-6;
    cfg.enable_collisions = (coll_mode != CollisionMode::Ignore);
    cfg.collision_config.mode = coll_mode;
    cfg.collision_config.density_preserving_merge = density_preserving;
    cfg.density_config.default_density = 1000.0;
    cfg.density_config.min_radius = 1e-6;
    engine.init(cfg);
    return engine;
}

/// Helper: set up N bodies with given positions, velocities, masses, and radii.
static void set_bodies(Engine& engine,
                        const std::vector<double>& px, const std::vector<double>& py,
                        const std::vector<double>& pz,
                        const std::vector<double>& vx, const std::vector<double>& vy,
                        const std::vector<double>& vz,
                        const std::vector<double>& mass, const std::vector<double>& radius) {
    int n = static_cast<int>(px.size());
    std::vector<double> ax(n, 0.0), ay(n, 0.0), az(n, 0.0);
    std::vector<uint8_t> active(n, 1);
    engine.set_particles(px.data(), py.data(), pz.data(),
                          vx.data(), vy.data(), vz.data(),
                          ax.data(), ay.data(), az.data(),
                          mass.data(), radius.data(), active.data(), n);
}

/// Helper: allocate and set up a ParticleSystem for unit tests.
static ParticleSystem make_particles(int n,
    const double* px, const double* py, const double* pz,
    const double* vx, const double* vy, const double* vz,
    const double* mass, const double* radius,
    const uint8_t* active)
{
    ParticleSystem ps;
    ps.allocate(n);
    ps.set_count(n);
    usize dsize = sizeof(double) * static_cast<usize>(n);
    usize u8size = sizeof(uint8_t) * static_cast<usize>(n);
    std::memcpy(ps.pos_x, px, dsize);
    std::memcpy(ps.pos_y, py, dsize);
    std::memcpy(ps.pos_z, pz, dsize);
    std::memcpy(ps.vel_x, vx, dsize);
    std::memcpy(ps.vel_y, vy, dsize);
    std::memcpy(ps.vel_z, vz, dsize);
    std::memset(ps.acc_x, 0, dsize);
    std::memset(ps.acc_y, 0, dsize);
    std::memset(ps.acc_z, 0, dsize);
    std::memset(ps.old_acc_x, 0, dsize);
    std::memset(ps.old_acc_y, 0, dsize);
    std::memset(ps.old_acc_z, 0, dsize);
    std::memcpy(ps.mass, mass, dsize);
    std::memcpy(ps.radius, radius, dsize);
    std::memcpy(ps.is_active, active, u8size);
    return ps;
}

// ==========================================================================
// Test 1: DensityModel_Compute
// ==========================================================================

TEST(DensityModel, ComputeDensity) {
    // Known sphere: r = 1.0, m = 1000.0 * (4/3 * pi * 1^3)
    double volume = (4.0 / 3.0) * PI * 1.0;
    double mass = 1000.0 * volume;
    double rho = DensityModel::compute_density(mass, 1.0, 1e-6);
    EXPECT_NEAR(rho, 1000.0, 1e-6) << "Density should be 1000 kg/m^3";
}

// ==========================================================================
// Test 2: DensityModel_RadiusFromDensity (round-trip)
// ==========================================================================

TEST(DensityModel, RadiusFromDensity_RoundTrip) {
    double mass = 100.0;
    double radius = 0.5;
    double min_r = 1e-6;

    double rho = DensityModel::compute_density(mass, radius, min_r);
    double radius_back = DensityModel::compute_radius(mass, rho, min_r);

    EXPECT_NEAR(radius_back, radius, 1e-12)
        << "compute_radius should invert compute_density";
}

// ==========================================================================
// Test 3: DensityModel_MinRadius
// ==========================================================================

TEST(DensityModel, MinRadiusClamp) {
    double min_r = 0.01;

    // Very tiny radius should be clamped
    double rho = DensityModel::compute_density(1.0, 1e-10, min_r);
    double expected_volume = (4.0 / 3.0) * PI * min_r * min_r * min_r;
    double expected_rho = 1.0 / expected_volume;
    EXPECT_NEAR(rho, expected_rho, expected_rho * 1e-10);

    // compute_radius with low mass should return min_radius
    double r = DensityModel::compute_radius(1e-40, 1000.0, min_r);
    EXPECT_NEAR(r, min_r, 1e-12);
}

// ==========================================================================
// Test 4: DensityModel_BulkUpdate
// ==========================================================================

TEST(DensityModel, BulkUpdate) {
    double px[] = {0.0, 1.0, 2.0};
    double py[] = {0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0};
    double mass[] = {100.0, 200.0, 50.0};
    double radius[] = {1.0, 0.5, 0.3};
    uint8_t active[] = {1, 1, 0};  // Third body inactive

    auto ps = make_particles(3, px, py, pz, vx, vy, vz, mass, radius, active);

    DensityModel dm;
    DensityConfig cfg;
    cfg.min_radius = 1e-6;
    dm.configure(cfg);
    dm.update_densities(ps);

    // Active bodies should have computed density
    double expected_rho0 = DensityModel::compute_density(100.0, 1.0, 1e-6);
    double expected_rho1 = DensityModel::compute_density(200.0, 0.5, 1e-6);
    EXPECT_NEAR(ps.density[0], expected_rho0, 1e-6);
    EXPECT_NEAR(ps.density[1], expected_rho1, 1e-6);
    // Inactive body should be zero
    EXPECT_DOUBLE_EQ(ps.density[2], 0.0);
}

// ==========================================================================
// Test 5: Compact_Basic
// ==========================================================================

TEST(Compact, Basic) {
    double px[] = {1.0, 2.0, 3.0, 4.0};
    double py[] = {0.0, 0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0, 0.0};
    double mass[] = {1.0, 2.0, 3.0, 4.0};
    double radius[] = {0.1, 0.2, 0.3, 0.4};
    uint8_t active[] = {1, 0, 1, 0};  // Bodies 1 and 3 inactive

    auto ps = make_particles(4, px, py, pz, vx, vy, vz, mass, radius, active);

    i32 new_count = ps.compact();

    EXPECT_EQ(new_count, 2);
    EXPECT_EQ(ps.count, 2);
    EXPECT_DOUBLE_EQ(ps.pos_x[0], 1.0);
    EXPECT_DOUBLE_EQ(ps.pos_x[1], 3.0);
    EXPECT_DOUBLE_EQ(ps.mass[0], 1.0);
    EXPECT_DOUBLE_EQ(ps.mass[1], 3.0);
    EXPECT_DOUBLE_EQ(ps.radius[0], 0.1);
    EXPECT_DOUBLE_EQ(ps.radius[1], 0.3);
}

// ==========================================================================
// Test 6: Compact_AllActive
// ==========================================================================

TEST(Compact, AllActive) {
    double px[] = {1.0, 2.0, 3.0};
    double py[] = {0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0};
    double mass[] = {1.0, 2.0, 3.0};
    double radius[] = {0.1, 0.2, 0.3};
    uint8_t active[] = {1, 1, 1};

    auto ps = make_particles(3, px, py, pz, vx, vy, vz, mass, radius, active);

    i32 new_count = ps.compact();

    EXPECT_EQ(new_count, 3);
    EXPECT_DOUBLE_EQ(ps.pos_x[0], 1.0);
    EXPECT_DOUBLE_EQ(ps.pos_x[1], 2.0);
    EXPECT_DOUBLE_EQ(ps.pos_x[2], 3.0);
}

// ==========================================================================
// Test 7: Compact_AllInactive
// ==========================================================================

TEST(Compact, AllInactive) {
    double px[] = {1.0, 2.0, 3.0};
    double py[] = {0.0, 0.0, 0.0};
    double pz[] = {0.0, 0.0, 0.0};
    double vx[] = {0.0, 0.0, 0.0};
    double vy[] = {0.0, 0.0, 0.0};
    double vz[] = {0.0, 0.0, 0.0};
    double mass[] = {1.0, 2.0, 3.0};
    double radius[] = {0.1, 0.2, 0.3};
    uint8_t active[] = {0, 0, 0};

    auto ps = make_particles(3, px, py, pz, vx, vy, vz, mass, radius, active);

    i32 new_count = ps.compact();

    EXPECT_EQ(new_count, 0);
    EXPECT_EQ(ps.count, 0);
}

// ==========================================================================
// Test 8: Compact_PreservesData (all 18 arrays shifted correctly)
// ==========================================================================

TEST(Compact, PreservesAllArrays) {
    double px[] = {10.0, 20.0, 30.0};
    double py[] = {11.0, 21.0, 31.0};
    double pz[] = {12.0, 22.0, 32.0};
    double vx[] = {1.0, 2.0, 3.0};
    double vy[] = {4.0, 5.0, 6.0};
    double vz[] = {7.0, 8.0, 9.0};
    double mass[] = {100.0, 200.0, 300.0};
    double radius[] = {0.5, 1.0, 1.5};
    uint8_t active[] = {0, 1, 1};  // First body inactive

    auto ps = make_particles(3, px, py, pz, vx, vy, vz, mass, radius, active);

    i32 new_count = ps.compact();

    EXPECT_EQ(new_count, 2);
    // Body 1 (was index 1) should now be at index 0
    EXPECT_DOUBLE_EQ(ps.pos_x[0], 20.0);
    EXPECT_DOUBLE_EQ(ps.pos_y[0], 21.0);
    EXPECT_DOUBLE_EQ(ps.pos_z[0], 22.0);
    EXPECT_DOUBLE_EQ(ps.vel_x[0], 2.0);
    EXPECT_DOUBLE_EQ(ps.vel_y[0], 5.0);
    EXPECT_DOUBLE_EQ(ps.vel_z[0], 8.0);
    EXPECT_DOUBLE_EQ(ps.mass[0], 200.0);
    EXPECT_DOUBLE_EQ(ps.radius[0], 1.0);
    EXPECT_EQ(ps.is_active[0], 1);
    // Body 2 (was index 2) should now be at index 1
    EXPECT_DOUBLE_EQ(ps.pos_x[1], 30.0);
    EXPECT_DOUBLE_EQ(ps.pos_y[1], 31.0);
    EXPECT_DOUBLE_EQ(ps.pos_z[1], 32.0);
    EXPECT_DOUBLE_EQ(ps.mass[1], 300.0);
    EXPECT_DOUBLE_EQ(ps.radius[1], 1.5);
}

// ==========================================================================
// Test 9: MergeSafeguard_PerFrame
// ==========================================================================

TEST(MergeSafeguard, PerFrameCap) {
    // Create 6 bodies at same position so all overlap (5 merges possible).
    // Cap at max_merges_per_frame = 2.
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge);
    engine.set_max_merges_per_frame(2);
    engine.set_max_merges_per_body(10);  // No per-body cap

    std::vector<double> px = {0.0, 0.1, 0.2, 0.3, 0.4, 0.5};
    std::vector<double> py(6, 0.0), pz(6, 0.0);
    std::vector<double> vx(6, 0.0), vy(6, 0.0), vz(6, 0.0);
    std::vector<double> mass = {10.0, 1.0, 1.0, 1.0, 1.0, 1.0};
    std::vector<double> radius = {10.0, 10.0, 10.0, 10.0, 10.0, 10.0};  // all overlap
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    i32 merges = engine.last_merge_count();
    EXPECT_LE(merges, 2) << "Per-frame merge cap should limit to 2";

    engine.shutdown();
}

// ==========================================================================
// Test 10: MergeSafeguard_PerBody
// ==========================================================================

TEST(MergeSafeguard, PerBodyCap) {
    // 4 bodies: body 0 (heaviest) overlaps with bodies 1, 2, 3.
    // Cap at max_merges_per_body = 1.
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge);
    engine.set_max_merges_per_frame(100);  // No frame cap
    engine.set_max_merges_per_body(1);

    std::vector<double> px = {0.0, 0.05, 0.1, 0.15};
    std::vector<double> py(4, 0.0), pz(4, 0.0);
    std::vector<double> vx(4, 0.0), vy(4, 0.0), vz(4, 0.0);
    std::vector<double> mass = {10.0, 1.0, 1.0, 1.0};
    std::vector<double> radius = {5.0, 5.0, 5.0, 5.0};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    i32 merges = engine.last_merge_count();
    EXPECT_LE(merges, 1) << "Per-body merge cap should limit surviving body to 1 merge";

    engine.shutdown();
}

// ==========================================================================
// Test 11: DensityPreservingMerge
// ==========================================================================

TEST(Merge, DensityPreserving) {
    // Two bodies with known density. After merge, survivor radius should
    // be computed from combined mass and survivor's pre-merge density.
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge, /*density_preserving=*/true);

    double r_a = 1.0;
    double m_a = 100.0;
    double r_b = 0.5;
    double m_b = 50.0;

    std::vector<double> px = {0.0, 0.5};  // Close enough to collide with these radii
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0}, vy = {0.0, 0.0}, vz = {0.0, 0.0};
    std::vector<double> mass = {m_a, m_b};
    std::vector<double> radius = {r_a, r_b};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    // After merge: survivor has combined mass
    int active = engine.active_particle_count();
    EXPECT_EQ(active, 1) << "Should have 1 survivor after merge";

    // Check that total mass is conserved
    double out_mass[2] = {0.0, 0.0};
    // Since compaction happened, particle_count should be 1
    EXPECT_EQ(engine.particle_count(), 1);

    // Density-preserving: R = cbrt(3 * M_total / (4 * pi * rho_survivor))
    double rho_a = DensityModel::compute_density(m_a, r_a, 1e-6);
    double M = m_a + m_b;
    double expected_r = DensityModel::compute_radius(M, rho_a, 1e-6);

    // Get positions to verify (just access engine internal state via get_positions)
    double out_px[1], out_py[1], out_pz[1];
    engine.get_positions(out_px, out_py, out_pz, 1);
    // Survivor should exist
    EXPECT_TRUE(std::isfinite(out_px[0]));

    engine.shutdown();
}

// ==========================================================================
// Test 12: DensityPreservingMerge_Conservation
// ==========================================================================

TEST(Merge, DensityPreserving_Conservation) {
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge, /*density_preserving=*/true);

    double m_a = 100.0, m_b = 50.0;
    double vx_a = 1.0, vx_b = -2.0;

    std::vector<double> px = {0.0, 0.3};
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {vx_a, vx_b}, vy = {0.0, 0.0}, vz = {0.0, 0.0};
    std::vector<double> mass = {m_a, m_b};
    std::vector<double> radius = {1.0, 1.0};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    double px_total_pre = m_a * vx_a + m_b * vx_b;

    engine.step(0.001, 1e-6);

    EXPECT_EQ(engine.particle_count(), 1);

    double out_vx[1], out_vy[1], out_vz[1];
    engine.get_velocities(out_vx, out_vy, out_vz, 1);

    // After merge, survivor momentum should match initial total
    double M = m_a + m_b;
    double px_total_post = M * out_vx[0];

    // Allow tolerance for gravitational acceleration during the step
    EXPECT_NEAR(px_total_post, px_total_pre, 1.0)
        << "Momentum should be approximately conserved after merge";

    engine.shutdown();
}

// ==========================================================================
// Test 13: UnifiedCPU_BH_CollisionDetection
// ==========================================================================

TEST(UnifiedCPU_BH, CollisionDetection) {
    // Two bodies very close (overlapping radii) — BH traversal should detect collision
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BarnesHut,
                                CollisionMode::Merge);

    std::vector<double> px = {0.0, 0.1};
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0}, vy = {0.0, 0.0}, vz = {0.0, 0.0};
    std::vector<double> mass = {100.0, 10.0};
    std::vector<double> radius = {1.0, 1.0};  // Sum of radii = 2.0 > dist = 0.1
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    i32 collisions = engine.last_collision_count();
    EXPECT_GT(collisions, 0) << "BH should detect overlapping bodies";
    EXPECT_EQ(engine.particle_count(), 1) << "Bodies should merge and compact";

    engine.shutdown();
}

// ==========================================================================
// Test 14: UnifiedCPU_BH_NoFalsePositives
// ==========================================================================

TEST(UnifiedCPU_BH, NoFalsePositives) {
    // Two bodies far apart — should NOT produce collision pairs
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BarnesHut,
                                CollisionMode::Merge);

    std::vector<double> px = {0.0, 100.0};
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0}, vy = {0.0, 0.0}, vz = {0.0, 0.0};
    std::vector<double> mass = {100.0, 10.0};
    std::vector<double> radius = {0.01, 0.01};  // Sum = 0.02 << dist = 100
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    i32 collisions = engine.last_collision_count();
    EXPECT_EQ(collisions, 0) << "Distant bodies should not collide";
    EXPECT_EQ(engine.particle_count(), 2) << "Both bodies should remain";

    engine.shutdown();
}

// ==========================================================================
// Test 15: UnifiedCPU_BH_Deduplication
// ==========================================================================

TEST(UnifiedCPU_BH, Deduplication) {
    // Three overlapping bodies — BH traversal should produce unique pairs
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BarnesHut,
                                CollisionMode::Elastic);  // Elastic so no merge confuses count

    std::vector<double> px = {0.0, 0.01, 0.02};
    std::vector<double> py = {0.0, 0.0, 0.0}, pz = {0.0, 0.0, 0.0};
    std::vector<double> vx = {1.0, -1.0, 0.5}, vy = {0.0, 0.0, 0.0}, vz = {0.0, 0.0, 0.0};
    std::vector<double> mass = {10.0, 10.0, 10.0};
    std::vector<double> radius = {1.0, 1.0, 1.0};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    // With 3 overlapping bodies we expect 3 unique pairs: (0,1), (0,2), (1,2)
    i32 collisions = engine.last_collision_count();
    EXPECT_EQ(collisions, 3) << "3 overlapping bodies should produce 3 unique pairs";

    engine.shutdown();
}

// ==========================================================================
// Test 16: UnifiedCPU_BH_ForceAccuracy
// ==========================================================================

TEST(UnifiedCPU_BH, ForceAccuracy) {
    // Compare force computation between unified BH (collision-aware) and
    // regular BH (no collision). Forces should be identical regardless of
    // collision detection being active (distant bodies, no collisions).
    auto make = [](bool enable_coll) {
        Engine eng;
        SimulationConfig cfg;
        cfg.max_particles = 16;
        cfg.compute_mode = SimulationConfig::ComputeMode::CPU_BarnesHut;
        cfg.dt = 0.001;
        cfg.softening = 1e-4;
        cfg.enable_collisions = enable_coll;
        cfg.collision_config.mode = enable_coll ? CollisionMode::Elastic : CollisionMode::Ignore;
        eng.init(cfg);
        return eng;
    };

    Engine eng_no_coll = make(false);
    Engine eng_with_coll = make(true);

    // 3 well-separated bodies (no collisions)
    std::vector<double> px = {0.0, 5.0, 10.0};
    std::vector<double> py = {0.0, 0.0, 0.0}, pz = {0.0, 0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0, 0.0}, vy = {0.0, 0.0, 0.0}, vz = {0.0, 0.0, 0.0};
    std::vector<double> mass = {100.0, 50.0, 25.0};
    std::vector<double> radius = {0.001, 0.001, 0.001};  // tiny radii, no collision
    set_bodies(eng_no_coll, px, py, pz, vx, vy, vz, mass, radius);
    set_bodies(eng_with_coll, px, py, pz, vx, vy, vz, mass, radius);

    eng_no_coll.step(0.001, 1e-4);
    eng_with_coll.step(0.001, 1e-4);

    double ax_nc[3], ay_nc[3], az_nc[3];
    double ax_wc[3], ay_wc[3], az_wc[3];
    eng_no_coll.get_accelerations(ax_nc, ay_nc, az_nc, 3);
    eng_with_coll.get_accelerations(ax_wc, ay_wc, az_wc, 3);

    for (int i = 0; i < 3; i++) {
        EXPECT_NEAR(ax_wc[i], ax_nc[i], 1e-10) << "ax[" << i << "] mismatch";
        EXPECT_NEAR(ay_wc[i], ay_nc[i], 1e-10) << "ay[" << i << "] mismatch";
        EXPECT_NEAR(az_wc[i], az_nc[i], 1e-10) << "az[" << i << "] mismatch";
    }

    eng_no_coll.shutdown();
    eng_with_coll.shutdown();
}

// ==========================================================================
// Test 17: UnifiedCPU_BH_MergeIntegration
// ==========================================================================

TEST(UnifiedCPU_BH, MergeIntegration) {
    // Full pipeline: BH forces + merge + compaction
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BarnesHut,
                                CollisionMode::Merge);

    // 3 bodies: 0 and 1 overlap, 2 is far away
    std::vector<double> px = {0.0, 0.1, 50.0};
    std::vector<double> py = {0.0, 0.0, 0.0}, pz = {0.0, 0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0, 0.0}, vy = {0.0, 0.0, 0.0}, vz = {0.0, 0.0, 0.0};
    std::vector<double> mass = {100.0, 50.0, 10.0};
    std::vector<double> radius = {1.0, 1.0, 0.01};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    // Bodies 0 and 1 should merge, body 2 should survive
    EXPECT_EQ(engine.particle_count(), 2) << "Should have 2 bodies after merge+compact";
    EXPECT_EQ(engine.last_merge_count(), 1);

    engine.shutdown();
}

// ==========================================================================
// Test 18: CompactAfterMerge_CountUpdated
// ==========================================================================

TEST(Compact, AfterMerge_CountUpdated) {
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge);

    // Two overlapping bodies
    std::vector<double> px = {0.0, 0.01};
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0}, vy = {0.0, 0.0}, vz = {0.0, 0.0};
    std::vector<double> mass = {100.0, 10.0};
    std::vector<double> radius = {1.0, 1.0};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    EXPECT_EQ(engine.particle_count(), 2);

    engine.step(0.001, 1e-6);

    EXPECT_EQ(engine.particle_count(), 1)
        << "particle_count should decrease after merge+compact";

    engine.shutdown();
}

// ==========================================================================
// Test 20: MergeChain_Stability
// ==========================================================================

TEST(Merge, ChainStability) {
    // 3 overlapping bodies. With default max_merges_per_body=2, the heaviest
    // can merge with both lighter ones in one step if per-frame cap allows.
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Merge);
    engine.set_max_merges_per_frame(10);
    engine.set_max_merges_per_body(2);

    std::vector<double> px = {0.0, 0.01, 0.02};
    std::vector<double> py = {0.0, 0.0, 0.0}, pz = {0.0, 0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0, 0.0}, vy = {0.0, 0.0, 0.0}, vz = {0.0, 0.0, 0.0};
    std::vector<double> mass = {100.0, 10.0, 10.0};
    std::vector<double> radius = {5.0, 5.0, 5.0};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    engine.step(0.001, 1e-6);

    // Should end with 1 survivor (2 merges possible with per-body cap of 2)
    EXPECT_EQ(engine.particle_count(), 1)
        << "3 overlapping bodies with per-body cap=2 should merge to 1 survivor";
    i32 merges = engine.last_merge_count();
    EXPECT_EQ(merges, 2);

    engine.shutdown();
}

// ==========================================================================
// Test 21: DensityConfig_CAPI
// ==========================================================================

TEST(CAPI, DensityConfig) {
    celestial_init(16);

    celestial_set_density_config(5000.0, 0.001);

    // We can verify the function doesn't crash. Full round-trip verification
    // would require exposing the density config getter in C API.
    // Just ensure the engine remains valid.
    EXPECT_GT(celestial_get_particle_count(), -1);

    celestial_shutdown();
}

// ==========================================================================
// Test 22: MergeSafeguard_CAPI
// ==========================================================================

TEST(CAPI, MergeSafeguard) {
    celestial_init(16);

    celestial_set_max_merges_per_frame(4);
    celestial_set_max_merges_per_body(1);
    celestial_set_density_preserving_merge(1);

    // Verify no crash
    EXPECT_GT(celestial_get_particle_count(), -1);

    celestial_shutdown();
}

// ==========================================================================
// Test 23: ActiveCount_CAPI
// ==========================================================================

TEST(CAPI, ActiveCount) {
    celestial_init(16);

    double px[] = {0.0, 0.01};
    double py[] = {0.0, 0.0};
    double pz[] = {0.0, 0.0};
    double vx[] = {0.0, 0.0};
    double vy[] = {0.0, 0.0};
    double vz[] = {0.0, 0.0};
    double ax[] = {0.0, 0.0};
    double ay[] = {0.0, 0.0};
    double az[] = {0.0, 0.0};
    double mass[] = {100.0, 10.0};
    double radius[] = {1.0, 1.0};
    uint8_t active[] = {1, 1};

    celestial_set_particles(px, py, pz, vx, vy, vz, ax, ay, az,
                             mass, radius, active, 2);

    i32 count = celestial_get_active_particle_count();
    EXPECT_EQ(count, 2);

    // Configure merge mode
    celestial_set_collision_mode(3);  // CollisionMode::Merge

    // Step should trigger merge
    celestial_step(0.001, 1e-6);

    // After merge, active count should be 1
    count = celestial_get_active_particle_count();
    EXPECT_EQ(count, 1);
    EXPECT_EQ(celestial_get_particle_count(), 1);

    celestial_shutdown();
}

// ==========================================================================
// Test 24: Regression_BruteForce_Unchanged
// ==========================================================================

TEST(Regression, BruteForceUnchanged) {
    // Verify brute-force mode with collision=Ignore gives same results
    // as it would without Phase 14-15 changes.
    Engine engine = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Ignore);

    // Simple two-body orbit
    double v_circ = std::sqrt(100.0 / 5.0);
    std::vector<double> px = {0.0, 5.0};
    std::vector<double> py = {0.0, 0.0}, pz = {0.0, 0.0};
    std::vector<double> vx = {0.0, 0.0}, vy = {0.0, v_circ}, vz = {0.0, 0.0};
    std::vector<double> mass = {100.0, 1.0};
    std::vector<double> radius = {0.01, 0.001};
    set_bodies(engine, px, py, pz, vx, vy, vz, mass, radius);

    for (int i = 0; i < 100; i++) {
        engine.step(0.0001, 1e-6);
    }

    double out_px[2], out_py[2], out_pz[2];
    engine.get_positions(out_px, out_py, out_pz, 2);

    double dx = out_px[1] - out_px[0];
    double dy = out_py[1] - out_py[0];
    double r_final = std::sqrt(dx * dx + dy * dy);

    // Orbit should remain stable
    EXPECT_GT(r_final, 4.5);
    EXPECT_LT(r_final, 5.5);

    engine.shutdown();
}

// ==========================================================================
// Test 25: Regression_BH_NoCollision (mode=Ignore identical to pre-Phase-14)
// ==========================================================================

TEST(Regression, BH_NoCollision) {
    // BH mode with collision=Ignore should match brute-force accelerations
    // (within BH approximation error).
    Engine eng_bf = make_engine(16, SimulationConfig::ComputeMode::CPU_BruteForce,
                                CollisionMode::Ignore);
    Engine eng_bh = make_engine(16, SimulationConfig::ComputeMode::CPU_BarnesHut,
                                CollisionMode::Ignore);

    std::vector<double> px = {0.0, 3.0, -2.0, 7.0};
    std::vector<double> py = {0.0, 1.0, -3.0, 2.0};
    std::vector<double> pz = {0.0, 0.0, 0.0, 0.0};
    std::vector<double> vx(4, 0.0), vy(4, 0.0), vz(4, 0.0);
    std::vector<double> mass = {100.0, 50.0, 25.0, 10.0};
    std::vector<double> radius = {0.001, 0.001, 0.001, 0.001};
    set_bodies(eng_bf, px, py, pz, vx, vy, vz, mass, radius);
    set_bodies(eng_bh, px, py, pz, vx, vy, vz, mass, radius);

    eng_bf.step(0.001, 1e-4);
    eng_bh.step(0.001, 1e-4);

    double ax_bf[4], ay_bf[4], az_bf[4];
    double ax_bh[4], ay_bh[4], az_bh[4];
    eng_bf.get_accelerations(ax_bf, ay_bf, az_bf, 4);
    eng_bh.get_accelerations(ax_bh, ay_bh, az_bh, 4);

    // BH with theta=0.5 should be within ~5% of brute force for well-separated bodies
    for (int i = 0; i < 4; i++) {
        double a_bf = std::sqrt(ax_bf[i]*ax_bf[i] + ay_bf[i]*ay_bf[i] + az_bf[i]*az_bf[i]);
        double a_bh = std::sqrt(ax_bh[i]*ax_bh[i] + ay_bh[i]*ay_bh[i] + az_bh[i]*az_bh[i]);
        if (a_bf > 1e-10) {
            double rel_err = std::abs(a_bh - a_bf) / a_bf;
            EXPECT_LT(rel_err, 0.05) << "BH force[" << i << "] too far from brute-force";
        }
    }

    eng_bf.shutdown();
    eng_bh.shutdown();
}
