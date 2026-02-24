#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/core/types.hpp>
#include <vector>

namespace celestial::cuda {

// ═════════════════════════════════════════════════════════════════════════
// GPU COMPACTION — Phase 18+19
//
// Removes inactive bodies (mass == 0 or is_active == 0) and compacts
// all SoA arrays so active bodies form a contiguous prefix.
//
// Algorithm:
//   1. Create active mask:  active[i] = (is_active[i] && mass[i] > 0)
//   2. Exclusive prefix scan on active mask → scatter indices
//   3. Scatter all SoA arrays from source to temp buffers using indices
//   4. Copy compacted data back to main arrays
//
// Determinism: Exclusive scan is a deterministic parallel primitive.
//   Order is preserved: body i maps to out[scan[i]] iff active[i].
//   Block size is fixed at 256 for stable scheduling.
//
// CPU reference path is NOT removed — only used in CPU modes.
// ═════════════════════════════════════════════════════════════════════════

static constexpr int COMPACT_BLOCK = 256;

// ─── Step 1: Build active mask ──────────────────────────────────────────

__global__ void build_active_mask_kernel(
    const uint8_t* __restrict__ is_active,
    const double* __restrict__ mass,
    int32_t n,
    int32_t* __restrict__ out_mask)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        out_mask[i] = (is_active[i] && mass[i] > 0.0) ? 1 : 0;
    }
}

// ─── Step 2: Exclusive prefix scan (Blelloch-style, two-level) ──────────

/// Block-level exclusive scan (shared memory, deterministic).
/// Returns the total (inclusive sum) for the block in *block_total.
__global__ void block_exclusive_scan_kernel(
    const int32_t* __restrict__ input,
    int32_t n,
    int32_t* __restrict__ output,
    int32_t* __restrict__ block_totals)
{
    __shared__ int32_t s_data[COMPACT_BLOCK * 2];

    int tid = threadIdx.x;
    int block_offset = blockIdx.x * (COMPACT_BLOCK * 2);
    int i0 = block_offset + tid;
    int i1 = block_offset + tid + COMPACT_BLOCK;

    // Load into shared memory
    s_data[tid] = (i0 < n) ? input[i0] : 0;
    s_data[tid + COMPACT_BLOCK] = (i1 < n) ? input[i1] : 0;
    __syncthreads();

    int elements = COMPACT_BLOCK * 2;

    // Up-sweep (reduce)
    for (int stride = 1; stride < elements; stride <<= 1) {
        int idx = (tid + 1) * (stride << 1) - 1;
        if (idx < elements) {
            s_data[idx] += s_data[idx - stride];
        }
        __syncthreads();
    }

    // Save block total before zeroing
    if (tid == 0) {
        block_totals[blockIdx.x] = s_data[elements - 1];
        s_data[elements - 1] = 0;
    }
    __syncthreads();

    // Down-sweep (distribute)
    for (int stride = elements / 2; stride >= 1; stride >>= 1) {
        int idx = (tid + 1) * (stride << 1) - 1;
        if (idx < elements) {
            int32_t temp = s_data[idx - stride];
            s_data[idx - stride] = s_data[idx];
            s_data[idx] += temp;
        }
        __syncthreads();
    }

    // Write output
    if (i0 < n) output[i0] = s_data[tid];
    if (i1 < n) output[i1] = s_data[tid + COMPACT_BLOCK];
}

/// Add block offsets to scan output (second pass).
__global__ void add_block_offsets_kernel(
    int32_t* __restrict__ scan_output,
    const int32_t* __restrict__ block_offsets,
    int32_t n)
{
    int i = blockIdx.x * (COMPACT_BLOCK * 2) + threadIdx.x;
    if (blockIdx.x == 0) return; // First block has no offset

    int32_t offset = block_offsets[blockIdx.x];
    if (i < n) scan_output[i] += offset;
    int j = i + COMPACT_BLOCK;
    if (j < n) scan_output[j] += offset;
}

// ─── Step 3: Scatter SoA arrays ─────────────────────────────────────────

__global__ void scatter_double_kernel(
    const double* __restrict__ src,
    double* __restrict__ dst,
    const int32_t* __restrict__ mask,
    const int32_t* __restrict__ scan,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n && mask[i]) {
        dst[scan[i]] = src[i];
    }
}

__global__ void scatter_u8_kernel(
    const uint8_t* __restrict__ src,
    uint8_t* __restrict__ dst,
    const int32_t* __restrict__ mask,
    const int32_t* __restrict__ scan,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n && mask[i]) {
        dst[scan[i]] = src[i];
    }
}

__global__ void scatter_i32_kernel(
    const int32_t* __restrict__ src,
    int32_t* __restrict__ dst,
    const int32_t* __restrict__ mask,
    const int32_t* __restrict__ scan,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n && mask[i]) {
        dst[scan[i]] = src[i];
    }
}

// ─── Copy-back kernel ───────────────────────────────────────────────────

__global__ void copy_double_kernel(
    const double* __restrict__ src,
    double* __restrict__ dst,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) dst[i] = src[i];
}

__global__ void copy_u8_kernel(
    const uint8_t* __restrict__ src,
    uint8_t* __restrict__ dst,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) dst[i] = src[i];
}

__global__ void copy_i32_kernel(
    const int32_t* __restrict__ src,
    int32_t* __restrict__ dst,
    int32_t n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) dst[i] = src[i];
}

// ═════════════════════════════════════════════════════════════════════════
// HOST LAUNCH: Full GPU compaction pipeline
// ═════════════════════════════════════════════════════════════════════════

/// Perform exclusive prefix scan on d_mask[0..n-1], writing to d_scan[0..n-1].
/// Returns total active count in out_new_count.
/// Uses d_block_totals and d_block_scan as scratch (each of size num_blocks).
static void gpu_exclusive_scan(
    const int32_t* d_mask, int32_t n,
    int32_t* d_scan,
    int32_t* d_block_totals,
    int32_t* d_block_scan,
    int32_t& out_new_count,
    cudaStream_t stream)
{
    int elements_per_block = COMPACT_BLOCK * 2;
    int num_blocks = (n + elements_per_block - 1) / elements_per_block;

    // Level 1: block-level exclusive scan
    block_exclusive_scan_kernel<<<num_blocks, COMPACT_BLOCK, 0, stream>>>(
        d_mask, n, d_scan, d_block_totals);
    CUDA_CHECK(cudaGetLastError());

    if (num_blocks > 1) {
        // Level 2: scan the block totals (recursive for large num_blocks)
        // For simplicity and determinism, if num_blocks <= 512 use single block.
        // For larger: use host reduction (rare case, N > 512*512 = 262144).
        if (num_blocks <= elements_per_block) {
            block_exclusive_scan_kernel<<<1, COMPACT_BLOCK, 0, stream>>>(
                d_block_totals, num_blocks, d_block_scan, d_block_totals + num_blocks);
            CUDA_CHECK(cudaGetLastError());

            // Add block offsets
            add_block_offsets_kernel<<<num_blocks, COMPACT_BLOCK, 0, stream>>>(
                d_scan, d_block_scan, n);
            CUDA_CHECK(cudaGetLastError());

            // Get total count: last block's scan value + last block's total
            CUDA_CHECK(cudaStreamSynchronize(stream));
            int32_t h_last_scan = 0, h_last_total = 0;
            CUDA_CHECK(cudaMemcpy(&h_last_scan, &d_block_scan[num_blocks - 1],
                sizeof(int32_t), cudaMemcpyDeviceToHost));
            CUDA_CHECK(cudaMemcpy(&h_last_total, &d_block_totals[num_blocks - 1],
                sizeof(int32_t), cudaMemcpyDeviceToHost));
            out_new_count = h_last_scan + h_last_total;
        } else {
            // Three-level: download block totals, scan on host, upload offsets
            std::vector<int32_t> h_totals(num_blocks);
            CUDA_CHECK(cudaMemcpyAsync(h_totals.data(), d_block_totals,
                sizeof(int32_t) * num_blocks, cudaMemcpyDeviceToHost, stream));
            CUDA_CHECK(cudaStreamSynchronize(stream));

            std::vector<int32_t> h_offsets(num_blocks);
            h_offsets[0] = 0;
            for (int i = 1; i < num_blocks; i++) {
                h_offsets[i] = h_offsets[i - 1] + h_totals[i - 1];
            }
            out_new_count = h_offsets[num_blocks - 1] + h_totals[num_blocks - 1];

            CUDA_CHECK(cudaMemcpyAsync(d_block_scan, h_offsets.data(),
                sizeof(int32_t) * num_blocks, cudaMemcpyHostToDevice, stream));

            add_block_offsets_kernel<<<num_blocks, COMPACT_BLOCK, 0, stream>>>(
                d_scan, d_block_scan, n);
            CUDA_CHECK(cudaGetLastError());
        }
    } else {
        // Single block: total is the block total
        CUDA_CHECK(cudaStreamSynchronize(stream));
        CUDA_CHECK(cudaMemcpy(&out_new_count, d_block_totals,
            sizeof(int32_t), cudaMemcpyDeviceToHost));
    }
}

/// Full GPU compaction of all SoA particle arrays.
///
/// Scratch requirements (allocated from gpu_pool scratch):
///   - d_mask:         n * sizeof(int32_t)
///   - d_scan:         n * sizeof(int32_t)
///   - d_block_totals: max_blocks * sizeof(int32_t)
///   - d_block_scan:   (max_blocks + 1) * sizeof(int32_t)
///   - Temp SoA buffers: n * sizeof(double) * 16 + n * sizeof(u8) * 2 + n * sizeof(i32)
///
/// Returns new count (number of active bodies after compaction).
int32_t launch_gpu_compact(
    // SoA arrays to compact (in-place)
    double* d_pos_x, double* d_pos_y, double* d_pos_z,
    double* d_vel_x, double* d_vel_y, double* d_vel_z,
    double* d_acc_x, double* d_acc_y, double* d_acc_z,
    double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
    double* d_mass, double* d_radius,
    double* d_density,      // may be nullptr
    uint8_t* d_is_active,
    uint8_t* d_is_collidable, // may be nullptr
    int32_t* d_body_type,     // may be nullptr
    int32_t n,
    // Scratch memory (caller provides from pool)
    int32_t* d_mask,
    int32_t* d_scan,
    int32_t* d_block_totals,
    int32_t* d_block_scan,
    // Temporary SoA buffers for scatter output
    double* d_tmp_doubles,  // At least n * 15 doubles (or 16 if density exists)
    uint8_t* d_tmp_u8,     // At least n * 2 u8 (or 1 if collidable nullptr)
    int32_t* d_tmp_i32,    // At least n i32 (or 0 if body_type nullptr)
    cudaStream_t stream)
{
    if (n <= 0) return 0;

    // Step 1: Build active mask
    int grid_mask = (n + COMPACT_BLOCK - 1) / COMPACT_BLOCK;
    build_active_mask_kernel<<<grid_mask, COMPACT_BLOCK, 0, stream>>>(
        d_is_active, d_mass, n, d_mask);
    CUDA_CHECK(cudaGetLastError());

    // Step 2: Exclusive prefix scan
    int32_t new_count = 0;
    gpu_exclusive_scan(d_mask, n, d_scan, d_block_totals, d_block_scan,
                       new_count, stream);

    if (new_count <= 0 || new_count == n) return new_count; // Nothing to compact

    // Step 3: Scatter all SoA arrays into temp buffers
    int grid_scatter = (n + COMPACT_BLOCK - 1) / COMPACT_BLOCK;

    // Layout temp doubles: [pos_x|pos_y|pos_z|vel_x|vel_y|vel_z|acc_x|acc_y|acc_z|
    //                       old_acc_x|old_acc_y|old_acc_z|mass|radius|density]
    double* t_pos_x = d_tmp_doubles;
    double* t_pos_y = d_tmp_doubles + n;
    double* t_pos_z = d_tmp_doubles + n * 2;
    double* t_vel_x = d_tmp_doubles + n * 3;
    double* t_vel_y = d_tmp_doubles + n * 4;
    double* t_vel_z = d_tmp_doubles + n * 5;
    double* t_acc_x = d_tmp_doubles + n * 6;
    double* t_acc_y = d_tmp_doubles + n * 7;
    double* t_acc_z = d_tmp_doubles + n * 8;
    double* t_old_x = d_tmp_doubles + n * 9;
    double* t_old_y = d_tmp_doubles + n * 10;
    double* t_old_z = d_tmp_doubles + n * 11;
    double* t_mass  = d_tmp_doubles + n * 12;
    double* t_rad   = d_tmp_doubles + n * 13;

    // Scatter position
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_pos_x, t_pos_x, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_pos_y, t_pos_y, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_pos_z, t_pos_z, d_mask, d_scan, n);
    // Scatter velocity
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_vel_x, t_vel_x, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_vel_y, t_vel_y, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_vel_z, t_vel_z, d_mask, d_scan, n);
    // Scatter acceleration
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_acc_x, t_acc_x, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_acc_y, t_acc_y, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_acc_z, t_acc_z, d_mask, d_scan, n);
    // Scatter old accelerations
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_old_acc_x, t_old_x, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_old_acc_y, t_old_y, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_old_acc_z, t_old_z, d_mask, d_scan, n);
    // Scatter mass and radius
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_mass, t_mass, d_mask, d_scan, n);
    scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_radius, t_rad, d_mask, d_scan, n);

    CUDA_CHECK(cudaGetLastError());

    // Scatter density (optional)
    double* t_density = nullptr;
    if (d_density) {
        t_density = d_tmp_doubles + n * 14;
        scatter_double_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_density, t_density, d_mask, d_scan, n);
    }

    // Scatter is_active (all will be 1 after compaction)
    uint8_t* t_active = d_tmp_u8;
    scatter_u8_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_is_active, t_active, d_mask, d_scan, n);

    // Scatter is_collidable (optional)
    uint8_t* t_collidable = nullptr;
    if (d_is_collidable) {
        t_collidable = d_tmp_u8 + n;
        scatter_u8_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_is_collidable, t_collidable, d_mask, d_scan, n);
    }

    // Scatter body_type_index (optional)
    if (d_body_type && d_tmp_i32) {
        scatter_i32_kernel<<<grid_scatter, COMPACT_BLOCK, 0, stream>>>(d_body_type, d_tmp_i32, d_mask, d_scan, n);
    }

    CUDA_CHECK(cudaGetLastError());

    // Step 4: Copy compacted data back from temp to main arrays
    int grid_copy = (new_count + COMPACT_BLOCK - 1) / COMPACT_BLOCK;

    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_pos_x, d_pos_x, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_pos_y, d_pos_y, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_pos_z, d_pos_z, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_vel_x, d_vel_x, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_vel_y, d_vel_y, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_vel_z, d_vel_z, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_acc_x, d_acc_x, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_acc_y, d_acc_y, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_acc_z, d_acc_z, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_old_x, d_old_acc_x, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_old_y, d_old_acc_y, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_old_z, d_old_acc_z, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_mass, d_mass, new_count);
    copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_rad, d_radius, new_count);

    if (d_density && t_density) {
        copy_double_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_density, d_density, new_count);
    }

    copy_u8_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_active, d_is_active, new_count);

    if (d_is_collidable && t_collidable) {
        copy_u8_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(t_collidable, d_is_collidable, new_count);
    }

    if (d_body_type && d_tmp_i32) {
        copy_i32_kernel<<<grid_copy, COMPACT_BLOCK, 0, stream>>>(d_tmp_i32, d_body_type, new_count);
    }

    CUDA_CHECK(cudaGetLastError());
    return new_count;
}

} // namespace celestial::cuda
