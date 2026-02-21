#include <celestial/physics/barnes_hut_solver.hpp>
#include <celestial/job/job_system.hpp>
#include <cmath>
#include <cstring>
#include <chrono>
#include <algorithm>

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

} // namespace celestial::physics
