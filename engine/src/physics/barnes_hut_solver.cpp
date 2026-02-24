#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/job/job_system.hpp>
#include <cmath>
#include <cstring>
#include <chrono>
#include <algorithm>
#include <mutex>

namespace celestial::physics {

void BarnesHutSolver::compute_forces(ParticleSystem& particles, double softening) {
    i32 n = particles.count;
    if (n == 0) return;

    double eps2 = softening * softening;
    double effective_theta = std::max(theta, 0.2);
    double theta2 = effective_theta * effective_theta;

    // Zero accelerations
    particles.zero_accelerations();

    auto t0 = std::chrono::high_resolution_clock::now();

    // Build octree
    builder_.min_node_size = min_node_size;
    i32 root = builder_.build(pool_, particles);
    if (root < 0) return;

    auto t1 = std::chrono::high_resolution_clock::now();
    last_build_time_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    const OctreeNode* nodes = pool_.nodes();

    if (use_parallel) {
        // Parallel traversal via job system
        auto& js = job::JobSystem::instance();
        int num_workers = std::max(1, js.num_workers());
        i32 chunk_size = (n + num_workers - 1) / num_workers;

        struct TraverseData {
            BarnesHutSolver* solver;
            const OctreeNode* nodes;
            i32 root;
            ParticleSystem* particles;
            double eps2;
            double theta2;
            i32 start;
            i32 end;
        };

        // Stack-allocate data for up to 64 workers
        TraverseData datas[64]{};
        int num_jobs = std::min(num_workers, 64);
        chunk_size = (n + num_jobs - 1) / num_jobs;

        auto* parent_job = js.create_job(
            [](job::Job*, const void*) {}, nullptr, job::JobPriority::High);

        for (int j = 0; j < num_jobs; j++) {
            i32 start = j * chunk_size;
            i32 end = std::min(start + chunk_size, n);
            if (start >= n) break;

            datas[j] = {this, nodes, root, &particles, eps2, theta2, start, end};

            auto* child = js.create_child_job(parent_job,
                [](job::Job*, const void* data) {
                    auto* td = static_cast<const TraverseData*>(data);
                    td->solver->traverse_range(td->nodes, td->root, *td->particles,
                                               td->eps2, td->theta2, td->start, td->end);
                },
                &datas[j], job::JobPriority::High);
            js.submit(child);
        }
        js.submit(parent_job);
        js.wait(parent_job);
    } else {
        // Serial traversal
        traverse_range(nodes, root, particles, eps2, theta2, 0, n);
    }

    auto t2 = std::chrono::high_resolution_clock::now();
    last_traversal_time_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();
    last_total_time_ms = last_build_time_ms + last_traversal_time_ms;
}

// --------------------------------------------------------------------------
// Phase 14-15: Unified force + collision traversal
// --------------------------------------------------------------------------

void BarnesHutSolver::compute_forces_with_collisions(
    ParticleSystem& particles, double softening,
    std::vector<CollisionPair>& out_pairs)
{
    i32 n = particles.count;
    out_pairs.clear();
    if (n == 0) return;

    double eps2 = softening * softening;
    double effective_theta = std::max(theta, 0.2);
    double theta2 = effective_theta * effective_theta;

    // Zero accelerations
    particles.zero_accelerations();

    auto t0 = std::chrono::high_resolution_clock::now();

    // Build octree (same as compute_forces)
    builder_.min_node_size = min_node_size;
    i32 root = builder_.build(pool_, particles);
    if (root < 0) return;

    auto t1 = std::chrono::high_resolution_clock::now();
    last_build_time_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    const OctreeNode* nodes = pool_.nodes();

    if (use_parallel) {
        auto& js = job::JobSystem::instance();
        int num_workers = std::max(1, js.num_workers());
        int num_jobs = std::min(num_workers, 64);
        i32 chunk_size = (n + num_jobs - 1) / num_jobs;

        // Thread-local collision pair buffers
        std::vector<CollisionPair> thread_pairs[64];

        struct TraverseCollideData {
            BarnesHutSolver* solver;
            const OctreeNode* nodes;
            i32 root;
            ParticleSystem* particles;
            double eps2;
            double theta2;
            i32 start;
            i32 end;
            std::vector<CollisionPair>* pairs;
        };

        TraverseCollideData datas[64]{};

        auto* parent_job = js.create_job(
            [](job::Job*, const void*) {}, nullptr, job::JobPriority::High);

        int actual_jobs = 0;
        for (int j = 0; j < num_jobs; j++) {
            i32 start = j * chunk_size;
            i32 end = std::min(start + chunk_size, n);
            if (start >= n) break;

            datas[j] = {this, nodes, root, &particles, eps2, theta2, start, end, &thread_pairs[j]};
            actual_jobs++;

            auto* child = js.create_child_job(parent_job,
                [](job::Job*, const void* data) {
                    auto* td = static_cast<const TraverseCollideData*>(data);
                    td->solver->traverse_range_with_collisions(
                        td->nodes, td->root, *td->particles,
                        td->eps2, td->theta2, td->start, td->end, *td->pairs);
                },
                &datas[j], job::JobPriority::High);
            js.submit(child);
        }
        js.submit(parent_job);
        js.wait(parent_job);

        // Merge thread-local collision pairs
        for (int j = 0; j < actual_jobs; j++) {
            out_pairs.insert(out_pairs.end(), thread_pairs[j].begin(), thread_pairs[j].end());
        }
    } else {
        // Serial traversal with collision detection
        traverse_range_with_collisions(nodes, root, particles, eps2, theta2, 0, n, out_pairs);
    }

    auto t2 = std::chrono::high_resolution_clock::now();
    last_traversal_time_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();
    last_total_time_ms = last_build_time_ms + last_traversal_time_ms;
}

// --------------------------------------------------------------------------
// Traversal implementations
// --------------------------------------------------------------------------

void BarnesHutSolver::traverse_range(
    const OctreeNode* nodes, i32 root,
    ParticleSystem& particles,
    double eps2, double theta2,
    i32 start, i32 end)
{
    for (i32 i = start; i < end; i++) {
        if (!particles.is_active[i]) continue;

        double axi = 0.0, ayi = 0.0, azi = 0.0;
        compute_force_on_body(nodes, root, i,
                              particles.pos_x[i], particles.pos_y[i], particles.pos_z[i],
                              eps2, theta2, axi, ayi, azi);
        particles.acc_x[i] = axi;
        particles.acc_y[i] = ayi;
        particles.acc_z[i] = azi;
    }
}

void BarnesHutSolver::traverse_range_with_collisions(
    const OctreeNode* nodes, i32 root,
    ParticleSystem& particles,
    double eps2, double theta2,
    i32 start, i32 end,
    std::vector<CollisionPair>& thread_pairs)
{
    for (i32 i = start; i < end; i++) {
        if (!particles.is_active[i]) continue;

        double axi = 0.0, ayi = 0.0, azi = 0.0;
        double br = particles.radius ? particles.radius[i] : 0.0;
        compute_force_on_body_with_collisions(
            nodes, root, i,
            particles.pos_x[i], particles.pos_y[i], particles.pos_z[i],
            br, eps2, theta2, particles,
            axi, ayi, azi, thread_pairs);
        particles.acc_x[i] = axi;
        particles.acc_y[i] = ayi;
        particles.acc_z[i] = azi;
    }
}

// --------------------------------------------------------------------------
// Per-body recursive force computation
// --------------------------------------------------------------------------

void BarnesHutSolver::compute_force_on_body(
    const OctreeNode* nodes, i32 node_idx,
    i32 body_idx, double bx, double by, double bz,
    double eps2, double theta2,
    double& axi, double& ayi, double& azi)
{
    const OctreeNode& node = nodes[node_idx];

    // Skip empty nodes
    if (node.total_mass == 0.0) return;

    // Displacement from body to node's center of mass
    double dx = bx - node.com_x;
    double dy = by - node.com_y;
    double dz = bz - node.com_z;
    double dist2 = dx * dx + dy * dy + dz * dz;

    if (node.is_leaf) {
        // Skip self-interaction
        if (node.body_index == body_idx) return;

        // Direct force
        double soft_dist2 = dist2 + eps2;
        double inv_dist = 1.0 / std::sqrt(soft_dist2);
        double inv_dist3 = inv_dist * inv_dist * inv_dist;
        double factor = node.total_mass * inv_dist3;

        axi -= factor * dx;
        ayi -= factor * dy;
        azi -= factor * dz;
        return;
    }

    // Opening angle criterion: (2*halfSize)^2 / dist^2 < theta^2
    double node_size2 = 4.0 * node.half_size * node.half_size;

    if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
        // Far enough — approximate as monopole
        double soft_dist2 = dist2 + eps2;
        double inv_dist = 1.0 / std::sqrt(soft_dist2);
        double inv_dist3 = inv_dist * inv_dist * inv_dist;
        double factor = node.total_mass * inv_dist3;

        axi -= factor * dx;
        ayi -= factor * dy;
        azi -= factor * dz;
        return;
    }

    // Recurse into children (fixed octant order 0-7 for determinism)
    for (int c = 0; c < 8; c++) {
        if (node.children[c] != -1) {
            compute_force_on_body(nodes, node.children[c], body_idx, bx, by, bz,
                                  eps2, theta2, axi, ayi, azi);
        }
    }
}

// --------------------------------------------------------------------------
// Phase 14-15: Per-body recursive force + collision detection
// --------------------------------------------------------------------------

void BarnesHutSolver::compute_force_on_body_with_collisions(
    const OctreeNode* nodes, i32 node_idx,
    i32 body_idx, double bx, double by, double bz,
    double br,
    double eps2, double theta2,
    const ParticleSystem& particles,
    double& axi, double& ayi, double& azi,
    std::vector<CollisionPair>& thread_pairs)
{
    const OctreeNode& node = nodes[node_idx];

    // Skip empty nodes
    if (node.total_mass == 0.0) return;

    // Displacement from body to node's center of mass
    double dx = bx - node.com_x;
    double dy = by - node.com_y;
    double dz = bz - node.com_z;
    double dist2 = dx * dx + dy * dy + dz * dz;

    if (node.is_leaf) {
        i32 leaf_idx = node.body_index;
        // Skip self-interaction
        if (leaf_idx == body_idx) return;

        // Direct force
        double soft_dist2 = dist2 + eps2;
        double inv_dist = 1.0 / std::sqrt(soft_dist2);
        double inv_dist3 = inv_dist * inv_dist * inv_dist;
        double factor = node.total_mass * inv_dist3;

        axi -= factor * dx;
        ayi -= factor * dy;
        azi -= factor * dz;

        // Phase 14-15: Collision check at leaf node
        // Canonical ordering: body_idx < leaf_idx to prevent duplicate pairs
        if (body_idx < leaf_idx && particles.radius) {
            double dist = std::sqrt(dist2);
            double leaf_r = particles.radius[leaf_idx];
            double sum_r = br + leaf_r;
            if (dist < sum_r) {
                thread_pairs.push_back(CollisionPair{body_idx, leaf_idx, dist});
            }
        }
        return;
    }

    // Opening angle criterion: (2*halfSize)^2 / dist^2 < theta^2
    double node_size2 = 4.0 * node.half_size * node.half_size;

    if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
        // Far enough — approximate as monopole (no collision check for far nodes)
        double soft_dist2 = dist2 + eps2;
        double inv_dist = 1.0 / std::sqrt(soft_dist2);
        double inv_dist3 = inv_dist * inv_dist * inv_dist;
        double factor = node.total_mass * inv_dist3;

        axi -= factor * dx;
        ayi -= factor * dy;
        azi -= factor * dz;
        return;
    }

    // Recurse into children (fixed octant order 0-7 for determinism)
    for (int c = 0; c < 8; c++) {
        if (node.children[c] != -1) {
            compute_force_on_body_with_collisions(
                nodes, node.children[c], body_idx, bx, by, bz,
                br, eps2, theta2, particles,
                axi, ayi, azi, thread_pairs);
        }
    }
}

// --------------------------------------------------------------------------
// Phase 16-17: O(N log N) potential energy computation
// --------------------------------------------------------------------------

double BarnesHutSolver::compute_potential(ParticleSystem& particles, double softening) {
    i32 n = particles.count;
    if (n == 0) return 0.0;

    double eps2 = softening * softening;
    double effective_theta = std::max(theta, 0.2);
    double theta2 = effective_theta * effective_theta;

    // Build octree
    builder_.min_node_size = min_node_size;
    i32 root = builder_.build(pool_, particles);
    if (root < 0) return 0.0;

    const OctreeNode* nodes = pool_.nodes();

    // Accumulate φ_i * m_i for each active body
    // PE = 0.5 * Σ(m_i * φ_i) to correct for double-counting
    double total_potential = 0.0;

    if (use_parallel) {
        auto& js = job::JobSystem::instance();
        int num_workers = std::max(1, js.num_workers());
        int num_jobs = std::min(num_workers, 64);
        i32 chunk_size = (n + num_jobs - 1) / num_jobs;

        struct PotentialData {
            BarnesHutSolver* solver;
            const OctreeNode* nodes;
            i32 root;
            ParticleSystem* particles;
            double eps2;
            double theta2;
            i32 start;
            i32 end;
            double partial_pe;
        };

        PotentialData datas[64]{};

        auto* parent_job = js.create_job(
            [](job::Job*, const void*) {}, nullptr, job::JobPriority::High);

        int actual_jobs = 0;
        for (int j = 0; j < num_jobs; j++) {
            i32 start = j * chunk_size;
            i32 end = std::min(start + chunk_size, n);
            if (start >= n) break;

            datas[j] = {this, nodes, root, &particles, eps2, theta2, start, end, 0.0};
            actual_jobs++;

            auto* child = js.create_child_job(parent_job,
                [](job::Job*, const void* data) {
                    auto* pd = const_cast<PotentialData*>(static_cast<const PotentialData*>(data));
                    double pe = 0.0;
                    for (i32 i = pd->start; i < pd->end; i++) {
                        if (!pd->particles->is_active[i]) continue;
                        double phi = compute_potential_on_body(
                            pd->nodes, pd->root, i,
                            pd->particles->pos_x[i], pd->particles->pos_y[i], pd->particles->pos_z[i],
                            pd->eps2, pd->theta2);
                        pe += pd->particles->mass[i] * phi;
                    }
                    pd->partial_pe = pe;
                },
                &datas[j], job::JobPriority::High);
            js.submit(child);
        }
        js.submit(parent_job);
        js.wait(parent_job);

        for (int j = 0; j < actual_jobs; j++) {
            total_potential += datas[j].partial_pe;
        }
    } else {
        // Serial path
        for (i32 i = 0; i < n; i++) {
            if (!particles.is_active[i]) continue;
            double phi = compute_potential_on_body(
                nodes, root, i,
                particles.pos_x[i], particles.pos_y[i], particles.pos_z[i],
                eps2, theta2);
            total_potential += particles.mass[i] * phi;
        }
    }

    // Correct for double-counting (each pair counted from both sides)
    return 0.5 * total_potential;
}

double BarnesHutSolver::compute_potential_on_body(
    const OctreeNode* nodes, i32 node_idx,
    i32 body_idx, double bx, double by, double bz,
    double eps2, double theta2)
{
    const OctreeNode& node = nodes[node_idx];

    // Skip empty nodes
    if (node.total_mass == 0.0) return 0.0;

    // Displacement from body to node's center of mass
    double dx = bx - node.com_x;
    double dy = by - node.com_y;
    double dz = bz - node.com_z;
    double dist2 = dx * dx + dy * dy + dz * dz;

    if (node.is_leaf) {
        // Skip self-interaction
        if (node.body_index == body_idx) return 0.0;

        // Direct potential: φ = -M / sqrt(r² + ε²)
        double soft_dist = std::sqrt(dist2 + eps2);
        return -node.total_mass / soft_dist;
    }

    // Opening angle criterion: (2*halfSize)² / dist² < theta²
    double node_size2 = 4.0 * node.half_size * node.half_size;

    if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
        // Far enough — monopole approximation
        double soft_dist = std::sqrt(dist2 + eps2);
        return -node.total_mass / soft_dist;
    }

    // Recurse into children (fixed octant order 0-7 for determinism)
    double phi = 0.0;
    for (int c = 0; c < 8; c++) {
        if (node.children[c] != -1) {
            phi += compute_potential_on_body(
                nodes, node.children[c], body_idx, bx, by, bz,
                eps2, theta2);
        }
    }
    return phi;
}

} // namespace celestial::physics
