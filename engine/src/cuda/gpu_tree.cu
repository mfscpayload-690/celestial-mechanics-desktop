#include <celestial/cuda/gpu_tree.hpp>
#include <celestial/cuda/morton.hpp>
#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/error.hpp>
#include <chrono>
#include <cmath>
#include <algorithm>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// GpuTreeBuffers allocation
// --------------------------------------------------------------------------

void GpuTreeBuffers::allocate(i32 max_n) {
    max_nodes = max_n;
    usize dsize = sizeof(double) * static_cast<usize>(max_n);
    usize i32size = sizeof(i32) * static_cast<usize>(max_n);
    usize u8size = sizeof(u8) * static_cast<usize>(max_n);

    CUDA_CHECK(cudaMalloc(&d_node_center_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_center_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_center_z, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_half_size, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_mass, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_com_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_com_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_com_z, dsize));
    CUDA_CHECK(cudaMalloc(&d_node_children, sizeof(i32) * static_cast<usize>(max_n) * 8));
    CUDA_CHECK(cudaMalloc(&d_node_body, i32size));
    CUDA_CHECK(cudaMalloc(&d_node_is_leaf, u8size));
    CUDA_CHECK(cudaMalloc(&d_node_count, sizeof(i32)));
}

void GpuTreeBuffers::free() {
    auto sf = [](auto& p) { if (p) { cudaFree(p); p = nullptr; } };
    sf(d_node_center_x); sf(d_node_center_y); sf(d_node_center_z);
    sf(d_node_half_size); sf(d_node_mass);
    sf(d_node_com_x); sf(d_node_com_y); sf(d_node_com_z);
    sf(d_node_children); sf(d_node_body); sf(d_node_is_leaf);
    sf(d_node_count);
    max_nodes = 0;
}

// --------------------------------------------------------------------------
// GpuTreeSolver
// --------------------------------------------------------------------------

void GpuTreeSolver::init(i32 max_bodies) {
    if (initialized_) return;

    i32 max_nodes = max_bodies * GPU_TREE_MAX_NODES_FACTOR;
    tree_.allocate(max_nodes);

    // Sort alternating buffers
    sort_capacity_ = max_bodies;
    CUDA_CHECK(cudaMalloc(&d_morton_alt_, sizeof(u64) * static_cast<usize>(max_bodies)));
    CUDA_CHECK(cudaMalloc(&d_indices_alt_, sizeof(i32) * static_cast<usize>(max_bodies)));

    // Pre-allocate radix sort workspace
    ensure_sort_workspace(max_bodies);

    // Phase 14-15: Allocate collision output buffers
    max_pairs_ = max_bodies / 4;  // generous estimate
    if (max_pairs_ < 256) max_pairs_ = 256;
    CUDA_CHECK(cudaMalloc(&d_pair_a_, sizeof(i32) * static_cast<usize>(max_pairs_)));
    CUDA_CHECK(cudaMalloc(&d_pair_b_, sizeof(i32) * static_cast<usize>(max_pairs_)));
    CUDA_CHECK(cudaMalloc(&d_pair_dist_, sizeof(double) * static_cast<usize>(max_pairs_)));
    CUDA_CHECK(cudaMalloc(&d_pair_count_, sizeof(i32)));

    initialized_ = true;
}

void GpuTreeSolver::destroy() {
    if (!initialized_) return;
    tree_.free();
    if (d_morton_alt_) { cudaFree(d_morton_alt_); d_morton_alt_ = nullptr; }
    if (d_indices_alt_) { cudaFree(d_indices_alt_); d_indices_alt_ = nullptr; }
    if (d_sort_histograms_) { cudaFree(d_sort_histograms_); d_sort_histograms_ = nullptr; }
    if (d_sort_offsets_) { cudaFree(d_sort_offsets_); d_sort_offsets_ = nullptr; }
    if (d_pair_a_) { cudaFree(d_pair_a_); d_pair_a_ = nullptr; }
    if (d_pair_b_) { cudaFree(d_pair_b_); d_pair_b_ = nullptr; }
    if (d_pair_dist_) { cudaFree(d_pair_dist_); d_pair_dist_ = nullptr; }
    if (d_pair_count_) { cudaFree(d_pair_count_); d_pair_count_ = nullptr; }
    sort_capacity_ = 0;
    sort_grid_capacity_ = 0;
    max_pairs_ = 0;
    initialized_ = false;
}

void GpuTreeSolver::ensure_sort_workspace(i32 n) {
    constexpr int RADIX_SORT_BLOCK = 256;
    constexpr int RADIX_BUCKETS = 16;
    i32 grid = (n + RADIX_SORT_BLOCK - 1) / RADIX_SORT_BLOCK;

    if (grid <= sort_grid_capacity_) return;

    if (d_sort_histograms_) { cudaFree(d_sort_histograms_); d_sort_histograms_ = nullptr; }
    if (d_sort_offsets_) { cudaFree(d_sort_offsets_); d_sort_offsets_ = nullptr; }

    // Allocate with headroom
    i32 alloc_grid = grid + grid / 4;
    usize hist_size = sizeof(i32) * static_cast<usize>(alloc_grid) * RADIX_BUCKETS;
    usize offset_size = sizeof(i32) * RADIX_BUCKETS;

    CUDA_CHECK(cudaMalloc(&d_sort_histograms_, hist_size));
    CUDA_CHECK(cudaMalloc(&d_sort_offsets_, offset_size));
    sort_grid_capacity_ = alloc_grid;
}

void GpuTreeSolver::compute_forces(memory::GpuPool& pool, i32 n, double softening,
                                     cudaStream_t stream) {
    if (n <= 0) return;

    // Expand sort buffers if needed (only at capacity changes, not per-frame)
    if (n > sort_capacity_) {
        if (d_morton_alt_) cudaFree(d_morton_alt_);
        if (d_indices_alt_) cudaFree(d_indices_alt_);
        sort_capacity_ = n + n / 4; // 25% headroom
        CUDA_CHECK(cudaMalloc(&d_morton_alt_, sizeof(u64) * static_cast<usize>(sort_capacity_)));
        CUDA_CHECK(cudaMalloc(&d_indices_alt_, sizeof(i32) * static_cast<usize>(sort_capacity_)));
        ensure_sort_workspace(sort_capacity_);
    }

    // Expand tree buffers if needed
    i32 required_nodes = n * GPU_TREE_MAX_NODES_FACTOR;
    if (required_nodes > tree_.max_nodes) {
        tree_.free();
        tree_.allocate(required_nodes);
    }

    auto t0 = std::chrono::high_resolution_clock::now();

    // Step 1: Compute bounding box via GPU reduction
    double min_x, min_y, min_z, max_x, max_y, max_z;
    launch_bbox_reduction(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_bbox_min, pool.d_bbox_max,
        min_x, min_y, min_z, max_x, max_y, max_z,
        stream);

    // Step 2: Generate Morton codes
    launch_morton_codes(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_morton_codes, pool.d_sorted_indices,
        min_x, min_y, min_z, max_x, max_y, max_z,
        stream);

    // Step 3: Radix sort by Morton code (using pre-allocated workspace)
    launch_radix_sort(
        pool.d_morton_codes, pool.d_sorted_indices, n,
        d_morton_alt_, d_indices_alt_,
        d_sort_histograms_, d_sort_offsets_,
        stream);

    auto t1 = std::chrono::high_resolution_clock::now();
    last_sort_time_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    // Step 4: Build octree from sorted bodies
    double cx = (min_x + max_x) * 0.5;
    double cy = (min_y + max_y) * 0.5;
    double cz = (min_z + max_z) * 0.5;
    double half_size = std::max({max_x - min_x, max_y - min_y, max_z - min_z}) * 0.5;
    half_size = std::max(half_size, 1e-10) * 1.001;

    launch_tree_build(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_mass, pool.d_is_active,
        pool.d_sorted_indices, n,
        tree_, cx, cy, cz, half_size,
        stream);

    auto t2 = std::chrono::high_resolution_clock::now();
    last_build_time_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();

    // Step 5: Zero accelerations
    pool.zero_accelerations(n, stream);

    // Step 6: Traverse tree for force computation
    launch_tree_traverse(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active,
        pool.d_acc_x, pool.d_acc_y, pool.d_acc_z,
        tree_, n, softening, theta,
        stream);

    auto t3 = std::chrono::high_resolution_clock::now();
    last_traverse_time_ms = std::chrono::duration<double, std::milli>(t3 - t2).count();
    last_total_time_ms = std::chrono::duration<double, std::milli>(t3 - t0).count();
}

// --------------------------------------------------------------------------
// Bounding box GPU reduction
// --------------------------------------------------------------------------

// Reuse the existing bounding box kernel from reduction_kernel.cu
extern void launch_bounding_box_kernel(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const uint8_t* d_is_active, int32_t n,
    double* d_out_min_x, double* d_out_min_y, double* d_out_min_z,
    double* d_out_max_x, double* d_out_max_y, double* d_out_max_z,
    cudaStream_t stream);

void launch_bbox_reduction(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active, i32 n,
    double* d_work_min, double* d_work_max,
    double& out_min_x, double& out_min_y, double& out_min_z,
    double& out_max_x, double& out_max_y, double& out_max_z,
    cudaStream_t stream)
{
    int block = 256;
    int grid = (n + block - 1) / block;

    // Use the d_work buffers: d_work_min holds [grid*3], d_work_max holds [grid*3]
    // Layout: [min_x_0..min_x_g, min_y_0..min_y_g, min_z_0..min_z_g]
    double* d_min_x = d_work_min;
    double* d_min_y = d_work_min + grid;
    double* d_min_z = d_work_min + grid * 2;
    double* d_max_x = d_work_max;
    double* d_max_y = d_work_max + grid;
    double* d_max_z = d_work_max + grid * 2;

    // Launch existing bounding box reduction kernel
    launch_bounding_box_kernel(
        d_pos_x, d_pos_y, d_pos_z, d_is_active, n,
        d_min_x, d_min_y, d_min_z,
        d_max_x, d_max_y, d_max_z,
        stream);

    // Synchronize and perform final reduction on host
    CUDA_CHECK(cudaStreamSynchronize(stream));

    // Download per-block results
    std::vector<double> h_min_x(grid), h_min_y(grid), h_min_z(grid);
    std::vector<double> h_max_x(grid), h_max_y(grid), h_max_z(grid);

    CUDA_CHECK(cudaMemcpy(h_min_x.data(), d_min_x, sizeof(double) * grid, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_min_y.data(), d_min_y, sizeof(double) * grid, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_min_z.data(), d_min_z, sizeof(double) * grid, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_max_x.data(), d_max_x, sizeof(double) * grid, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_max_y.data(), d_max_y, sizeof(double) * grid, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_max_z.data(), d_max_z, sizeof(double) * grid, cudaMemcpyDeviceToHost));

    out_min_x = 1e308; out_min_y = 1e308; out_min_z = 1e308;
    out_max_x = -1e308; out_max_y = -1e308; out_max_z = -1e308;

    for (int i = 0; i < grid; i++) {
        out_min_x = std::min(out_min_x, h_min_x[i]);
        out_min_y = std::min(out_min_y, h_min_y[i]);
        out_min_z = std::min(out_min_z, h_min_z[i]);
        out_max_x = std::max(out_max_x, h_max_x[i]);
        out_max_y = std::max(out_max_y, h_max_y[i]);
        out_max_z = std::max(out_max_z, h_max_z[i]);
    }
}

// --------------------------------------------------------------------------
// Tree build kernel: insert bodies into octree from sorted order
// --------------------------------------------------------------------------

// GPU octree build: serial insertion from sorted bodies.
// This is launched as a single-thread kernel for correctness.
// For large N, a parallel hierarchical approach would be better,
// but this ensures determinism and correctness.
__global__ void tree_build_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    const int32_t* __restrict__ sorted_indices,
    int32_t n,
    // Tree SoA arrays
    double* __restrict__ center_x, double* __restrict__ center_y, double* __restrict__ center_z,
    double* __restrict__ half_size,
    double* __restrict__ node_mass,
    double* __restrict__ com_x, double* __restrict__ com_y, double* __restrict__ com_z,
    int32_t* __restrict__ children, // [max_nodes * 8]
    int32_t* __restrict__ body_idx,
    uint8_t* __restrict__ is_leaf,
    int32_t* __restrict__ node_count,
    double root_cx, double root_cy, double root_cz, double root_hs,
    int32_t max_nodes)
{
    // Single thread kernel for tree construction
    if (threadIdx.x != 0 || blockIdx.x != 0) return;

    // Initialize root node (index 0)
    int32_t count = 1;
    center_x[0] = root_cx; center_y[0] = root_cy; center_z[0] = root_cz;
    half_size[0] = root_hs;
    node_mass[0] = 0.0;
    com_x[0] = 0.0; com_y[0] = 0.0; com_z[0] = 0.0;
    body_idx[0] = -1;
    is_leaf[0] = 0;
    for (int c = 0; c < 8; c++) children[c] = -1;

    constexpr double MIN_NODE_SIZE = 1e-12;

    // Insert each body in Morton-sorted order
    for (int32_t si = 0; si < n; si++) {
        int32_t bi = sorted_indices[si];
        if (!is_active[bi]) continue;

        double bx = pos_x[bi];
        double by = pos_y[bi];
        double bz = pos_z[bi];
        double bm = mass[bi];
        if (bm <= 0.0) continue;

        // Walk down the tree to find insertion point
        int32_t cur = 0;

        while (true) {
            if (count >= max_nodes - 1) break;  // Safety cap

            double cur_hs = half_size[cur];
            double cur_cx = center_x[cur];
            double cur_cy = center_y[cur];
            double cur_cz = center_z[cur];

            // Determine octant
            int octant = 0;
            if (bx >= cur_cx) octant |= 1;
            if (by >= cur_cy) octant |= 2;
            if (bz >= cur_cz) octant |= 4;

            int32_t* child_ptr = &children[cur * 8 + octant];

            if (is_leaf[cur] && body_idx[cur] != -1) {
                // Node is a leaf with a body — must subdivide
                if (cur_hs * 0.5 < MIN_NODE_SIZE) {
                    // Too small to subdivide; aggregate mass
                    double total = node_mass[cur] + bm;
                    double inv = 1.0 / total;
                    com_x[cur] = (com_x[cur] * node_mass[cur] + bx * bm) * inv;
                    com_y[cur] = (com_y[cur] * node_mass[cur] + by * bm) * inv;
                    com_z[cur] = (com_z[cur] * node_mass[cur] + bz * bm) * inv;
                    node_mass[cur] = total;
                    break;
                }

                // Save existing body data
                int32_t existing_bi = body_idx[cur];
                double ex = com_x[cur], ey = com_y[cur], ez = com_z[cur];
                double em = node_mass[cur];

                // Convert to internal node
                body_idx[cur] = -1;
                is_leaf[cur] = 0;
                node_mass[cur] = 0.0;
                com_x[cur] = 0.0; com_y[cur] = 0.0; com_z[cur] = 0.0;

                // Re-insert existing body into child
                int eoct = 0;
                if (ex >= cur_cx) eoct |= 1;
                if (ey >= cur_cy) eoct |= 2;
                if (ez >= cur_cz) eoct |= 4;

                double quarter = cur_hs * 0.5;
                double ecx = cur_cx + ((eoct & 1) ? quarter : -quarter);
                double ecy = cur_cy + ((eoct & 2) ? quarter : -quarter);
                double ecz = cur_cz + ((eoct & 4) ? quarter : -quarter);

                int32_t echild = count++;
                center_x[echild] = ecx; center_y[echild] = ecy; center_z[echild] = ecz;
                half_size[echild] = quarter;
                node_mass[echild] = em;
                com_x[echild] = ex; com_y[echild] = ey; com_z[echild] = ez;
                body_idx[echild] = existing_bi;
                is_leaf[echild] = 1;
                for (int c = 0; c < 8; c++) children[echild * 8 + c] = -1;
                children[cur * 8 + eoct] = echild;

                // Update aggregate for parent
                double total = em;
                double tcx = ex * em, tcy = ey * em, tcz = ez * em;

                // Now insert the new body
                int noct = 0;
                if (bx >= cur_cx) noct |= 1;
                if (by >= cur_cy) noct |= 2;
                if (bz >= cur_cz) noct |= 4;

                double nqcx = cur_cx + ((noct & 1) ? quarter : -quarter);
                double nqcy = cur_cy + ((noct & 2) ? quarter : -quarter);
                double nqcz = cur_cz + ((noct & 4) ? quarter : -quarter);

                if (noct == eoct) {
                    // Same octant — recurse into that child
                    total += bm;
                    tcx += bx * bm; tcy += by * bm; tcz += bz * bm;
                    double inv = 1.0 / total;
                    node_mass[cur] = total;
                    com_x[cur] = tcx * inv; com_y[cur] = tcy * inv; com_z[cur] = tcz * inv;
                    cur = echild;
                    continue;
                }

                // Different octant — create new leaf for new body
                int32_t nchild = count++;
                center_x[nchild] = nqcx; center_y[nchild] = nqcy; center_z[nchild] = nqcz;
                half_size[nchild] = quarter;
                node_mass[nchild] = bm;
                com_x[nchild] = bx; com_y[nchild] = by; com_z[nchild] = bz;
                body_idx[nchild] = bi;
                is_leaf[nchild] = 1;
                for (int c = 0; c < 8; c++) children[nchild * 8 + c] = -1;
                children[cur * 8 + noct] = nchild;

                // Update parent aggregate
                total += bm;
                tcx += bx * bm; tcy += by * bm; tcz += bz * bm;
                double inv = 1.0 / total;
                node_mass[cur] = total;
                com_x[cur] = tcx * inv; com_y[cur] = tcy * inv; com_z[cur] = tcz * inv;
                break;
            }

            if (*child_ptr == -1) {
                // Empty slot — create a leaf
                double quarter = cur_hs * 0.5;
                double ccx = cur_cx + ((octant & 1) ? quarter : -quarter);
                double ccy = cur_cy + ((octant & 2) ? quarter : -quarter);
                double ccz = cur_cz + ((octant & 4) ? quarter : -quarter);

                int32_t new_node = count++;
                center_x[new_node] = ccx; center_y[new_node] = ccy; center_z[new_node] = ccz;
                half_size[new_node] = quarter;
                node_mass[new_node] = bm;
                com_x[new_node] = bx; com_y[new_node] = by; com_z[new_node] = bz;
                body_idx[new_node] = bi;
                is_leaf[new_node] = 1;
                for (int c = 0; c < 8; c++) children[new_node * 8 + c] = -1;
                *child_ptr = new_node;

                // Update ancestor mass/COM
                double total = node_mass[cur] + bm;
                double inv = (total > 0.0) ? 1.0 / total : 0.0;
                com_x[cur] = (com_x[cur] * node_mass[cur] + bx * bm) * inv;
                com_y[cur] = (com_y[cur] * node_mass[cur] + by * bm) * inv;
                com_z[cur] = (com_z[cur] * node_mass[cur] + bz * bm) * inv;
                node_mass[cur] = total;
                break;
            }

            // Internal node with existing child — update aggregate and descend
            double total = node_mass[cur] + bm;
            double inv = (total > 0.0) ? 1.0 / total : 0.0;
            com_x[cur] = (com_x[cur] * node_mass[cur] + bx * bm) * inv;
            com_y[cur] = (com_y[cur] * node_mass[cur] + by * bm) * inv;
            com_z[cur] = (com_z[cur] * node_mass[cur] + bz * bm) * inv;
            node_mass[cur] = total;

            cur = *child_ptr;
        }
    }

    *node_count = count;
}

void launch_tree_build(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_mass, const u8* d_is_active,
    const i32* d_sorted_indices, i32 n,
    GpuTreeBuffers& tree,
    double bbox_cx, double bbox_cy, double bbox_cz, double bbox_half_size,
    cudaStream_t stream)
{
    if (n <= 0) return;

    // Reset node count
    CUDA_CHECK(cudaMemsetAsync(tree.d_node_count, 0, sizeof(i32), stream));

    // Initialize children to -1
    usize children_bytes = sizeof(i32) * static_cast<usize>(tree.max_nodes) * 8;
    CUDA_CHECK(cudaMemsetAsync(tree.d_node_children, 0xFF, children_bytes, stream));

    // Single-thread tree construction (deterministic, sequential insert)
    tree_build_kernel<<<1, 1, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active,
        d_sorted_indices, n,
        tree.d_node_center_x, tree.d_node_center_y, tree.d_node_center_z,
        tree.d_node_half_size,
        tree.d_node_mass,
        tree.d_node_com_x, tree.d_node_com_y, tree.d_node_com_z,
        tree.d_node_children,
        tree.d_node_body,
        tree.d_node_is_leaf,
        tree.d_node_count,
        bbox_cx, bbox_cy, bbox_cz, bbox_half_size,
        tree.max_nodes);

    CUDA_CHECK(cudaGetLastError());
}

// --------------------------------------------------------------------------
// Tree traversal kernel: compute forces using Barnes-Hut opening criterion
// --------------------------------------------------------------------------

// Stack-based iterative traversal (avoids recursion depth limits on GPU)
static constexpr int GPU_TREE_STACK_SIZE = 128;

__global__ void tree_traverse_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const uint8_t* __restrict__ is_active,
    double* __restrict__ acc_x,
    double* __restrict__ acc_y,
    double* __restrict__ acc_z,
    // Tree SoA (read-only)
    const double* __restrict__ node_mass,
    const double* __restrict__ node_com_x,
    const double* __restrict__ node_com_y,
    const double* __restrict__ node_com_z,
    const double* __restrict__ node_half_size,
    const int32_t* __restrict__ node_children,
    const int32_t* __restrict__ node_body,
    const uint8_t* __restrict__ node_is_leaf,
    int32_t n,
    double eps2,
    double theta2)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) return;

    double bx = pos_x[i], by = pos_y[i], bz = pos_z[i];
    double axi = 0.0, ayi = 0.0, azi = 0.0;

    // Stack-based iterative traversal
    int32_t stack[GPU_TREE_STACK_SIZE];
    int sp = 0;
    stack[sp++] = 0; // Push root

    while (sp > 0) {
        int32_t node = stack[--sp];
        if (node < 0) continue;

        double nm = node_mass[node];
        if (nm <= 0.0) continue;

        double dx = bx - node_com_x[node];
        double dy = by - node_com_y[node];
        double dz = bz - node_com_z[node];
        double dist2 = dx * dx + dy * dy + dz * dz;

        if (node_is_leaf[node]) {
            // Skip self-interaction
            if (node_body[node] == i) continue;

            // Direct force computation
            double soft_dist2 = dist2 + eps2;
            double inv_dist = rsqrt(soft_dist2);
            double inv_dist3 = inv_dist * inv_dist * inv_dist;
            double factor = nm * inv_dist3;
            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;
            continue;
        }

        // Opening angle criterion: (2*halfSize)^2 / dist^2 < theta^2
        double hs = node_half_size[node];
        double node_size2 = 4.0 * hs * hs;

        if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
            // Far enough — use monopole approximation
            double soft_dist2 = dist2 + eps2;
            double inv_dist = rsqrt(soft_dist2);
            double inv_dist3 = inv_dist * inv_dist * inv_dist;
            double factor = nm * inv_dist3;
            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;
            continue;
        }

        // Need to open node — push children (deterministic order: 0-7)
        const int32_t* children_base = &node_children[node * 8];
        #pragma unroll
        for (int c = 7; c >= 0; c--) {
            int32_t child = children_base[c];
            if (child >= 0 && sp < GPU_TREE_STACK_SIZE) {
                stack[sp++] = child;
            }
        }
    }

    acc_x[i] = axi;
    acc_y[i] = ayi;
    acc_z[i] = azi;
}

void launch_tree_traverse(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    const GpuTreeBuffers& tree,
    i32 n, double softening, double theta,
    cudaStream_t stream)
{
    if (n <= 0) return;

    double eps2 = softening * softening;
    double theta2 = theta * theta;

    int block = 256;
    int grid = (n + block - 1) / block;

    tree_traverse_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_is_active,
        d_acc_x, d_acc_y, d_acc_z,
        tree.d_node_mass,
        tree.d_node_com_x, tree.d_node_com_y, tree.d_node_com_z,
        tree.d_node_half_size,
        tree.d_node_children,
        tree.d_node_body,
        tree.d_node_is_leaf,
        n, eps2, theta2);

    CUDA_CHECK(cudaGetLastError());
}

// --------------------------------------------------------------------------
// Phase 14-15: Tree traversal kernel with leaf-level collision detection
// --------------------------------------------------------------------------

__global__ void tree_traverse_collide_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ radius,
    const uint8_t* __restrict__ is_active,
    double* __restrict__ acc_x,
    double* __restrict__ acc_y,
    double* __restrict__ acc_z,
    // Tree SoA (read-only)
    const double* __restrict__ node_mass,
    const double* __restrict__ node_com_x,
    const double* __restrict__ node_com_y,
    const double* __restrict__ node_com_z,
    const double* __restrict__ node_half_size,
    const int32_t* __restrict__ node_children,
    const int32_t* __restrict__ node_body,
    const uint8_t* __restrict__ node_is_leaf,
    int32_t n,
    double eps2,
    double theta2,
    // Collision output
    int32_t* __restrict__ pair_a,
    int32_t* __restrict__ pair_b,
    double* __restrict__ pair_dist,
    int32_t* __restrict__ pair_count,
    int32_t max_pairs)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) return;

    double bx = pos_x[i], by = pos_y[i], bz = pos_z[i];
    double br = radius[i];
    double axi = 0.0, ayi = 0.0, azi = 0.0;

    // Stack-based iterative traversal
    int32_t stack[GPU_TREE_STACK_SIZE];
    int sp = 0;
    stack[sp++] = 0; // Push root

    while (sp > 0) {
        int32_t node = stack[--sp];
        if (node < 0) continue;

        double nm = node_mass[node];
        if (nm <= 0.0) continue;

        double dx = bx - node_com_x[node];
        double dy = by - node_com_y[node];
        double dz = bz - node_com_z[node];
        double dist2 = dx * dx + dy * dy + dz * dz;

        if (node_is_leaf[node]) {
            int32_t leaf_body = node_body[node];
            // Skip self-interaction
            if (leaf_body == i) continue;

            // Direct force computation
            double soft_dist2 = dist2 + eps2;
            double inv_dist = rsqrt(soft_dist2);
            double inv_dist3 = inv_dist * inv_dist * inv_dist;
            double factor = nm * inv_dist3;
            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;

            // Collision check at leaf: dist < r_i + r_j
            // Canonical ordering: only record when i < leaf_body
            if (i < leaf_body && leaf_body >= 0) {
                double dist = sqrt(dist2);
                double r_sum = br + radius[leaf_body];
                if (dist < r_sum) {
                    int32_t slot = atomicAdd(pair_count, 1);
                    if (slot < max_pairs) {
                        pair_a[slot] = i;
                        pair_b[slot] = leaf_body;
                        pair_dist[slot] = dist;
                    }
                }
            }
            continue;
        }

        // Opening angle criterion: (2*halfSize)^2 / dist^2 < theta^2
        double hs = node_half_size[node];
        double node_size2 = 4.0 * hs * hs;

        if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
            // Far enough — use monopole approximation
            double soft_dist2 = dist2 + eps2;
            double inv_dist = rsqrt(soft_dist2);
            double inv_dist3 = inv_dist * inv_dist * inv_dist;
            double factor = nm * inv_dist3;
            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;
            continue;
        }

        // Need to open node — push children (deterministic order: 0-7)
        const int32_t* children_base = &node_children[node * 8];
        #pragma unroll
        for (int c = 7; c >= 0; c--) {
            int32_t child = children_base[c];
            if (child >= 0 && sp < GPU_TREE_STACK_SIZE) {
                stack[sp++] = child;
            }
        }
    }

    acc_x[i] = axi;
    acc_y[i] = ayi;
    acc_z[i] = azi;
}

void launch_tree_traverse_collide(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const double* d_radius, const u8* d_is_active,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    const GpuTreeBuffers& tree,
    i32 n, double softening, double theta,
    i32* d_pair_a, i32* d_pair_b, double* d_pair_dist,
    i32* d_pair_count, i32 max_pairs,
    cudaStream_t stream)
{
    if (n <= 0) return;

    double eps2 = softening * softening;
    double theta2 = theta * theta;

    int block = 256;
    int grid = (n + block - 1) / block;

    tree_traverse_collide_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_radius, d_is_active,
        d_acc_x, d_acc_y, d_acc_z,
        tree.d_node_mass,
        tree.d_node_com_x, tree.d_node_com_y, tree.d_node_com_z,
        tree.d_node_half_size,
        tree.d_node_children,
        tree.d_node_body,
        tree.d_node_is_leaf,
        n, eps2, theta2,
        d_pair_a, d_pair_b, d_pair_dist, d_pair_count, max_pairs);

    CUDA_CHECK(cudaGetLastError());
}

// --------------------------------------------------------------------------
// Phase 14-15: compute_forces_with_collisions
// --------------------------------------------------------------------------

void GpuTreeSolver::compute_forces_with_collisions(
    memory::GpuPool& pool, i32 n, double softening,
    std::vector<physics::CollisionPair>& out_pairs,
    cudaStream_t stream)
{
    out_pairs.clear();
    if (n <= 0) return;

    // Expand sort buffers if needed
    if (n > sort_capacity_) {
        if (d_morton_alt_) cudaFree(d_morton_alt_);
        if (d_indices_alt_) cudaFree(d_indices_alt_);
        sort_capacity_ = n + n / 4;
        CUDA_CHECK(cudaMalloc(&d_morton_alt_, sizeof(u64) * static_cast<usize>(sort_capacity_)));
        CUDA_CHECK(cudaMalloc(&d_indices_alt_, sizeof(i32) * static_cast<usize>(sort_capacity_)));
        ensure_sort_workspace(sort_capacity_);
    }

    // Expand tree buffers if needed
    i32 required_nodes = n * GPU_TREE_MAX_NODES_FACTOR;
    if (required_nodes > tree_.max_nodes) {
        tree_.free();
        tree_.allocate(required_nodes);
    }

    // Expand collision buffers if needed
    i32 needed_pairs = n / 4;
    if (needed_pairs < 256) needed_pairs = 256;
    if (needed_pairs > max_pairs_) {
        if (d_pair_a_) cudaFree(d_pair_a_);
        if (d_pair_b_) cudaFree(d_pair_b_);
        if (d_pair_dist_) cudaFree(d_pair_dist_);
        max_pairs_ = needed_pairs;
        CUDA_CHECK(cudaMalloc(&d_pair_a_, sizeof(i32) * static_cast<usize>(max_pairs_)));
        CUDA_CHECK(cudaMalloc(&d_pair_b_, sizeof(i32) * static_cast<usize>(max_pairs_)));
        CUDA_CHECK(cudaMalloc(&d_pair_dist_, sizeof(double) * static_cast<usize>(max_pairs_)));
    }

    auto t0 = std::chrono::high_resolution_clock::now();

    // Step 1: Compute bounding box
    double min_x, min_y, min_z, max_x, max_y, max_z;
    launch_bbox_reduction(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_bbox_min, pool.d_bbox_max,
        min_x, min_y, min_z, max_x, max_y, max_z,
        stream);

    // Step 2: Generate Morton codes
    launch_morton_codes(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_morton_codes, pool.d_sorted_indices,
        min_x, min_y, min_z, max_x, max_y, max_z,
        stream);

    // Step 3: Radix sort
    launch_radix_sort(
        pool.d_morton_codes, pool.d_sorted_indices, n,
        d_morton_alt_, d_indices_alt_,
        d_sort_histograms_, d_sort_offsets_,
        stream);

    auto t1 = std::chrono::high_resolution_clock::now();
    last_sort_time_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    // Step 4: Build octree
    double cx = (min_x + max_x) * 0.5;
    double cy = (min_y + max_y) * 0.5;
    double cz = (min_z + max_z) * 0.5;
    double half_size = std::max({max_x - min_x, max_y - min_y, max_z - min_z}) * 0.5;
    half_size = std::max(half_size, 1e-10) * 1.001;

    launch_tree_build(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_mass, pool.d_is_active,
        pool.d_sorted_indices, n,
        tree_, cx, cy, cz, half_size,
        stream);

    auto t2 = std::chrono::high_resolution_clock::now();
    last_build_time_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();

    // Step 5: Zero accelerations
    pool.zero_accelerations(n, stream);

    // Step 6: Reset collision pair counter
    CUDA_CHECK(cudaMemsetAsync(d_pair_count_, 0, sizeof(i32), stream));

    // Step 7: Traverse tree with collision detection
    launch_tree_traverse_collide(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_radius, pool.d_is_active,
        pool.d_acc_x, pool.d_acc_y, pool.d_acc_z,
        tree_, n, softening, theta,
        d_pair_a_, d_pair_b_, d_pair_dist_,
        d_pair_count_, max_pairs_,
        stream);

    auto t3 = std::chrono::high_resolution_clock::now();
    last_traverse_time_ms = std::chrono::duration<double, std::milli>(t3 - t2).count();
    last_total_time_ms = std::chrono::duration<double, std::milli>(t3 - t0).count();

    // Step 8: Download collision pairs to host
    CUDA_CHECK(cudaStreamSynchronize(stream));

    i32 h_pair_count = 0;
    CUDA_CHECK(cudaMemcpy(&h_pair_count, d_pair_count_, sizeof(i32), cudaMemcpyDeviceToHost));

    if (h_pair_count > max_pairs_) h_pair_count = max_pairs_;  // clamp to buffer size

    if (h_pair_count > 0) {
        std::vector<i32> h_pair_a(h_pair_count);
        std::vector<i32> h_pair_b(h_pair_count);
        std::vector<double> h_pair_dist(h_pair_count);

        CUDA_CHECK(cudaMemcpy(h_pair_a.data(), d_pair_a_,
            sizeof(i32) * static_cast<usize>(h_pair_count), cudaMemcpyDeviceToHost));
        CUDA_CHECK(cudaMemcpy(h_pair_b.data(), d_pair_b_,
            sizeof(i32) * static_cast<usize>(h_pair_count), cudaMemcpyDeviceToHost));
        CUDA_CHECK(cudaMemcpy(h_pair_dist.data(), d_pair_dist_,
            sizeof(double) * static_cast<usize>(h_pair_count), cudaMemcpyDeviceToHost));

        out_pairs.reserve(static_cast<usize>(h_pair_count));
        for (i32 p = 0; p < h_pair_count; p++) {
            out_pairs.push_back({h_pair_a[p], h_pair_b[p], h_pair_dist[p]});
        }
    }
}

// Provide the launch wrapper for the existing bounding box kernel in reduction_kernel.cu
void launch_bounding_box_kernel(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const uint8_t* d_is_active, int32_t n,
    double* d_out_min_x, double* d_out_min_y, double* d_out_min_z,
    double* d_out_max_x, double* d_out_max_y, double* d_out_max_z,
    cudaStream_t stream)
{
    if (n <= 0) return;
    int block = 256;
    int grid = (n + block - 1) / block;

    // Forward to the kernel defined in reduction_kernel.cu
    extern __global__ void compute_bounding_box_kernel(
        const double*, const double*, const double*,
        const uint8_t*, int32_t,
        double*, double*, double*, double*, double*, double*);

    compute_bounding_box_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_is_active, n,
        d_out_min_x, d_out_min_y, d_out_min_z,
        d_out_max_x, d_out_max_y, d_out_max_z);

    CUDA_CHECK(cudaGetLastError());
}

// Center-of-mass is computed during tree build, so this is a no-op.
void launch_tree_com(GpuTreeBuffers& /*tree*/, i32 /*n_nodes*/, cudaStream_t /*stream*/) {
    // COM is computed inline during tree_build_kernel insertion.
}

// ═════════════════════════════════════════════════════════════════════════════
// Phase 18+19: GPU-resident force+merge+compact pipeline
// ═════════════════════════════════════════════════════════════════════════════

// Forward declarations for merge and compact kernels
void launch_resolve_merges(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_mass, double* d_radius, double* d_density,
    uint8_t* d_is_active,
    const int32_t* d_pair_a, const int32_t* d_pair_b,
    int32_t num_pairs,
    int32_t* d_locks,
    int32_t* d_merge_count_per_body,
    int32_t* d_total_merge_count,
    int32_t n_bodies,
    int32_t max_merges_per_body,
    int32_t max_merges_per_frame,
    double min_radius,
    bool density_preserving,
    cudaStream_t stream);

int32_t launch_gpu_compact(
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
    double* d_mass, double* d_radius,
    double* d_density,
    uint8_t* d_is_active,
    uint8_t* d_is_collidable,
    int32_t* d_body_type,
    int32_t n,
    int32_t* d_mask,
    int32_t* d_scan,
    int32_t* d_block_totals,
    int32_t* d_block_scan,
    double* d_tmp_doubles,
    uint8_t* d_tmp_u8,
    int32_t* d_tmp_i32,
    cudaStream_t stream);

void sort_collision_pairs_host(
    std::vector<int32_t>& pair_a,
    std::vector<int32_t>& pair_b,
    std::vector<double>& pair_dist);

i32 GpuTreeSolver::compute_forces_merge_compact(
    memory::GpuPool& pool, i32 n, double softening,
    const physics::CollisionResolverConfig& collision_config,
    double min_radius, bool density_preserving,
    cudaStream_t stream)
{
    if (n <= 0) return n;
    if (collision_config.mode != physics::CollisionMode::Merge) {
        // Non-merge modes: just compute forces with collisions (old path)
        // The caller handles non-merge collision types on CPU still
        compute_forces(pool, n, softening, stream);
        return n;
    }

    // Step 1-6: Standard BH pipeline + collision detection
    // (This rebuilds the tree and does force traversal with collision detection)
    compute_forces_with_collisions_internal(pool, n, softening, stream);

    // Step 7: Read pair count from device
    CUDA_CHECK(cudaStreamSynchronize(stream));
    i32 h_pair_count = 0;
    CUDA_CHECK(cudaMemcpy(&h_pair_count, d_pair_count_, sizeof(i32), cudaMemcpyDeviceToHost));

    if (h_pair_count <= 0) return n;
    if (h_pair_count > max_pairs_) h_pair_count = max_pairs_;

    // Step 8: Download pairs, sort for deterministic order, re-upload
    std::vector<i32> h_pair_a(h_pair_count), h_pair_b(h_pair_count);
    std::vector<double> h_pair_dist(h_pair_count);
    CUDA_CHECK(cudaMemcpy(h_pair_a.data(), d_pair_a_, sizeof(i32) * h_pair_count, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_pair_b.data(), d_pair_b_, sizeof(i32) * h_pair_count, cudaMemcpyDeviceToHost));
    CUDA_CHECK(cudaMemcpy(h_pair_dist.data(), d_pair_dist_, sizeof(double) * h_pair_count, cudaMemcpyDeviceToHost));

    sort_collision_pairs_host(h_pair_a, h_pair_b, h_pair_dist);

    // Re-upload sorted pairs
    CUDA_CHECK(cudaMemcpyAsync(d_pair_a_, h_pair_a.data(), sizeof(i32) * h_pair_count, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pair_b_, h_pair_b.data(), sizeof(i32) * h_pair_count, cudaMemcpyHostToDevice, stream));

    // Step 9: Allocate merge scratch from pool
    i32* d_locks = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * n));
    i32* d_merge_count = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * n));
    i32* d_total_merge = static_cast<i32*>(pool.scratch_alloc(sizeof(i32)));

    if (!d_locks || !d_merge_count || !d_total_merge) {
        // Scratch exhausted — skip merges this frame
        return n;
    }

    // Step 10: Launch GPU merge resolution
    launch_resolve_merges(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_vel_x, pool.d_vel_y, pool.d_vel_z,
        pool.d_mass, pool.d_radius, nullptr, // density on device not in pool currently
        pool.d_is_active,
        d_pair_a_, d_pair_b_,
        h_pair_count,
        d_locks, d_merge_count, d_total_merge,
        n,
        collision_config.max_merges_per_body,
        collision_config.max_merges_per_frame,
        min_radius, density_preserving,
        stream);

    // Step 11: GPU compaction
    // Allocate compaction scratch
    int elements_per_block = 512;
    int num_blocks = (n + elements_per_block - 1) / elements_per_block;

    i32* d_mask = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * n));
    i32* d_scan = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * n));
    i32* d_block_totals = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * (num_blocks + 1)));
    i32* d_block_scan = static_cast<i32*>(pool.scratch_alloc(sizeof(i32) * (num_blocks + 1)));

    // Temp SoA buffers for scatter
    usize tmp_doubles_size = sizeof(double) * n * 15;
    usize tmp_u8_size = sizeof(u8) * n * 2;
    usize tmp_i32_size = sizeof(i32) * n;
    double* d_tmp_doubles = static_cast<double*>(pool.scratch_alloc(tmp_doubles_size));
    u8* d_tmp_u8 = static_cast<u8*>(pool.scratch_alloc(tmp_u8_size));
    i32* d_tmp_i32 = static_cast<i32*>(pool.scratch_alloc(tmp_i32_size));

    if (!d_mask || !d_scan || !d_block_totals || !d_block_scan ||
        !d_tmp_doubles || !d_tmp_u8) {
        // Scratch exhausted — no compaction possible, return old count
        return n;
    }

    i32 new_count = launch_gpu_compact(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_vel_x, pool.d_vel_y, pool.d_vel_z,
        pool.d_acc_x, pool.d_acc_y, pool.d_acc_z,
        pool.d_old_acc_x, pool.d_old_acc_y, pool.d_old_acc_z,
        pool.d_mass, pool.d_radius,
        nullptr,            // d_density
        pool.d_is_active,
        pool.d_is_collidable,
        pool.d_body_type,
        n,
        d_mask, d_scan, d_block_totals, d_block_scan,
        d_tmp_doubles, d_tmp_u8, d_tmp_i32,
        stream);

    CUDA_CHECK(cudaStreamSynchronize(stream));
    return new_count;
}

/// Internal: same as compute_forces_with_collisions but doesn't download pairs.
/// Pairs remain on device in d_pair_a_/d_pair_b_/d_pair_dist_/d_pair_count_.
void GpuTreeSolver::compute_forces_with_collisions_internal(
    memory::GpuPool& pool, i32 n, double softening,
    cudaStream_t stream)
{
    // Expand buffers if needed
    if (n > sort_capacity_) {
        if (d_morton_alt_) cudaFree(d_morton_alt_);
        if (d_indices_alt_) cudaFree(d_indices_alt_);
        sort_capacity_ = n + n / 4;
        CUDA_CHECK(cudaMalloc(&d_morton_alt_, sizeof(u64) * static_cast<usize>(sort_capacity_)));
        CUDA_CHECK(cudaMalloc(&d_indices_alt_, sizeof(i32) * static_cast<usize>(sort_capacity_)));
        ensure_sort_workspace(sort_capacity_);
    }

    i32 required_nodes = n * GPU_TREE_MAX_NODES_FACTOR;
    if (required_nodes > tree_.max_nodes) {
        tree_.free();
        tree_.allocate(required_nodes);
    }

    i32 needed_pairs = n / 4;
    if (needed_pairs < 256) needed_pairs = 256;
    if (needed_pairs > max_pairs_) {
        if (d_pair_a_) cudaFree(d_pair_a_);
        if (d_pair_b_) cudaFree(d_pair_b_);
        if (d_pair_dist_) cudaFree(d_pair_dist_);
        max_pairs_ = needed_pairs;
        CUDA_CHECK(cudaMalloc(&d_pair_a_, sizeof(i32) * max_pairs_));
        CUDA_CHECK(cudaMalloc(&d_pair_b_, sizeof(i32) * max_pairs_));
        CUDA_CHECK(cudaMalloc(&d_pair_dist_, sizeof(double) * max_pairs_));
    }

    auto t0 = std::chrono::high_resolution_clock::now();

    double min_x, min_y, min_z, max_x, max_y, max_z;
    launch_bbox_reduction(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_bbox_min, pool.d_bbox_max,
        min_x, min_y, min_z, max_x, max_y, max_z, stream);

    launch_morton_codes(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_is_active, n,
        pool.d_morton_codes, pool.d_sorted_indices,
        min_x, min_y, min_z, max_x, max_y, max_z, stream);

    launch_radix_sort(
        pool.d_morton_codes, pool.d_sorted_indices, n,
        d_morton_alt_, d_indices_alt_,
        d_sort_histograms_, d_sort_offsets_, stream);

    auto t1 = std::chrono::high_resolution_clock::now();
    last_sort_time_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    double cx = (min_x + max_x) * 0.5;
    double cy = (min_y + max_y) * 0.5;
    double cz = (min_z + max_z) * 0.5;
    double hs = std::max({max_x - min_x, max_y - min_y, max_z - min_z}) * 0.5;
    hs = std::max(hs, 1e-10) * 1.001;

    launch_tree_build(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_mass, pool.d_is_active,
        pool.d_sorted_indices, n,
        tree_, cx, cy, cz, hs, stream);

    auto t2 = std::chrono::high_resolution_clock::now();
    last_build_time_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();

    pool.zero_accelerations(n, stream);

    CUDA_CHECK(cudaMemsetAsync(d_pair_count_, 0, sizeof(i32), stream));

    launch_tree_traverse_collide(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_radius, pool.d_is_active,
        pool.d_acc_x, pool.d_acc_y, pool.d_acc_z,
        tree_, n, softening, theta,
        d_pair_a_, d_pair_b_, d_pair_dist_,
        d_pair_count_, max_pairs_, stream);

    auto t3 = std::chrono::high_resolution_clock::now();
    last_traverse_time_ms = std::chrono::duration<double, std::milli>(t3 - t2).count();
    last_total_time_ms = std::chrono::duration<double, std::milli>(t3 - t0).count();
}

// ═════════════════════════════════════════════════════════════════════════════
// Phase 18+19: GPU-resident energy computation
// ═════════════════════════════════════════════════════════════════════════════

// Forward declarations for reduction and PE kernels
namespace {
    // Declared in gpu_reduction.cu
    extern void gpu_reduce_kinetic_energy(
        const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
        const double* d_mass, const uint8_t* d_active, int32_t n,
        double* d_block_out, double& out_ke, cudaStream_t stream);

    extern void gpu_reduce_momentum(
        const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
        const double* d_mass, const uint8_t* d_active, int32_t n,
        double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
        double& out_px, double& out_py, double& out_pz, cudaStream_t stream);

    extern void gpu_reduce_angular_momentum(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_vel_x, const double* d_vel_y, const double* d_vel_z,
        const double* d_mass, const uint8_t* d_active, int32_t n,
        double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
        double& out_lx, double& out_ly, double& out_lz, cudaStream_t stream);

    extern void gpu_reduce_com(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_mass, const uint8_t* d_active, int32_t n,
        double* d_block_out_x, double* d_block_out_y, double* d_block_out_z,
        double* d_block_out_m,
        double& out_cx, double& out_cy, double& out_cz, double& out_total_mass,
        cudaStream_t stream);

    extern void launch_gpu_potential_energy(
        const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
        const double* d_mass, const uint8_t* d_is_active, int32_t n,
        const double* d_node_mass,
        const double* d_node_com_x, const double* d_node_com_y, const double* d_node_com_z,
        const double* d_node_half_size,
        const int32_t* d_node_children,
        const int32_t* d_node_body,
        const uint8_t* d_node_is_leaf,
        double softening, double theta,
        double* d_scratch_mphi,
        double* d_block_out,
        double& out_pe,
        cudaStream_t stream);
}

GpuTreeSolver::GpuEnergySnapshot GpuTreeSolver::compute_gpu_energy(
    memory::GpuPool& pool, i32 n, double softening,
    cudaStream_t stream)
{
    GpuEnergySnapshot snap{};
    if (n <= 0) return snap;

    int grid = (n + 255) / 256;

    // Allocate scratch for reductions
    double* d_scratch1 = static_cast<double*>(pool.scratch_alloc(sizeof(double) * grid));
    double* d_scratch2 = static_cast<double*>(pool.scratch_alloc(sizeof(double) * grid));
    double* d_scratch3 = static_cast<double*>(pool.scratch_alloc(sizeof(double) * grid));
    double* d_scratch4 = static_cast<double*>(pool.scratch_alloc(sizeof(double) * grid));
    double* d_scratch_mphi = static_cast<double*>(pool.scratch_alloc(sizeof(double) * n));

    if (!d_scratch1 || !d_scratch2 || !d_scratch3 || !d_scratch4 || !d_scratch_mphi) {
        return snap; // scratch exhausted
    }

    // KE
    cuda::gpu_reduce_kinetic_energy(
        pool.d_vel_x, pool.d_vel_y, pool.d_vel_z,
        pool.d_mass, pool.d_is_active, n,
        d_scratch1, snap.kinetic_energy, stream);

    // PE via BH tree traversal (tree must already be built this frame)
    cuda::launch_gpu_potential_energy(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_mass, pool.d_is_active, n,
        tree_.d_node_mass,
        tree_.d_node_com_x, tree_.d_node_com_y, tree_.d_node_com_z,
        tree_.d_node_half_size,
        tree_.d_node_children,
        tree_.d_node_body,
        tree_.d_node_is_leaf,
        softening, theta,
        d_scratch_mphi, d_scratch1,
        snap.potential_energy, stream);

    snap.total_energy = snap.kinetic_energy + snap.potential_energy;

    // Linear momentum
    cuda::gpu_reduce_momentum(
        pool.d_vel_x, pool.d_vel_y, pool.d_vel_z,
        pool.d_mass, pool.d_is_active, n,
        d_scratch1, d_scratch2, d_scratch3,
        snap.momentum_x, snap.momentum_y, snap.momentum_z, stream);

    // Angular momentum
    cuda::gpu_reduce_angular_momentum(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_vel_x, pool.d_vel_y, pool.d_vel_z,
        pool.d_mass, pool.d_is_active, n,
        d_scratch1, d_scratch2, d_scratch3,
        snap.angular_momentum_x, snap.angular_momentum_y, snap.angular_momentum_z, stream);

    // COM + total mass
    cuda::gpu_reduce_com(
        pool.d_pos_x, pool.d_pos_y, pool.d_pos_z,
        pool.d_mass, pool.d_is_active, n,
        d_scratch1, d_scratch2, d_scratch3, d_scratch4,
        snap.com_x, snap.com_y, snap.com_z, snap.total_mass, stream);

    return snap;
}

} // namespace celestial::cuda
