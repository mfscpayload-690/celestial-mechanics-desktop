#include <celestial/cuda/morton.hpp>
#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>

namespace celestial::cuda {

// --------------------------------------------------------------------------
// Morton code generation kernel
// --------------------------------------------------------------------------

__global__ void morton_code_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    uint64_t* __restrict__ morton_codes,
    int32_t* __restrict__ indices,
    double inv_range_x, double inv_range_y, double inv_range_z,
    double min_x, double min_y, double min_z)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;

    // Initialize index mapping
    indices[i] = i;

    if (!is_active[i]) {
        // Inactive bodies get maximum Morton code (sorted to end)
        morton_codes[i] = 0xFFFFFFFFFFFFFFFFULL;
        return;
    }

    // Normalize position to [0, 1]
    double ux = (pos_x[i] - min_x) * inv_range_x;
    double uy = (pos_y[i] - min_y) * inv_range_y;
    double uz = (pos_z[i] - min_z) * inv_range_z;

    // Clamp to [0, 1]
    ux = fmax(0.0, fmin(1.0, ux));
    uy = fmax(0.0, fmin(1.0, uy));
    uz = fmax(0.0, fmin(1.0, uz));

    morton_codes[i] = morton_from_unit(ux, uy, uz);
}

void launch_morton_codes(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active, i32 n,
    u64* d_morton_codes, i32* d_indices,
    double bbox_min_x, double bbox_min_y, double bbox_min_z,
    double bbox_max_x, double bbox_max_y, double bbox_max_z,
    cudaStream_t stream)
{
    if (n <= 0) return;

    double range_x = bbox_max_x - bbox_min_x;
    double range_y = bbox_max_y - bbox_min_y;
    double range_z = bbox_max_z - bbox_min_z;

    // Avoid division by zero for degenerate bounding boxes
    double inv_range_x = (range_x > 1e-30) ? 1.0 / range_x : 0.0;
    double inv_range_y = (range_y > 1e-30) ? 1.0 / range_y : 0.0;
    double inv_range_z = (range_z > 1e-30) ? 1.0 / range_z : 0.0;

    int block = 256;
    int grid = (n + block - 1) / block;

    morton_code_kernel<<<grid, block, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_is_active, n,
        d_morton_codes, d_indices,
        inv_range_x, inv_range_y, inv_range_z,
        bbox_min_x, bbox_min_y, bbox_min_z);

    CUDA_CHECK(cudaGetLastError());
}

// --------------------------------------------------------------------------
// Radix sort (per-block counting sort, multi-pass)
// 64-bit keys sorted 4 bits at a time = 16 passes
// --------------------------------------------------------------------------

// Simple device-wide radix sort using counting sort per digit
// For production, one would use cub::DeviceRadixSort. This is a
// self-contained implementation that avoids the CUB/Thrust dependency.

static constexpr int RADIX_BITS = 4;
static constexpr int RADIX_BUCKETS = 1 << RADIX_BITS; // 16
static constexpr int RADIX_SORT_BLOCK = 256;

__global__ void histogram_kernel(
    const uint64_t* __restrict__ keys,
    int32_t n,
    int shift,
    int32_t* __restrict__ histograms) // [gridDim.x * RADIX_BUCKETS]
{
    __shared__ int32_t s_hist[RADIX_BUCKETS];

    // Initialize shared histogram
    if (threadIdx.x < RADIX_BUCKETS) {
        s_hist[threadIdx.x] = 0;
    }
    __syncthreads();

    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        int digit = (keys[i] >> shift) & (RADIX_BUCKETS - 1);
        atomicAdd(&s_hist[digit], 1);
    }
    __syncthreads();

    // Write block histogram to global memory
    if (threadIdx.x < RADIX_BUCKETS) {
        histograms[blockIdx.x * RADIX_BUCKETS + threadIdx.x] = s_hist[threadIdx.x];
    }
}

__global__ void prefix_sum_histograms(
    int32_t* __restrict__ histograms,
    int32_t* __restrict__ offsets,
    int num_blocks)
{
    // Single-block kernel: one thread per bucket
    int bucket = threadIdx.x;
    if (bucket >= RADIX_BUCKETS) return;

    int32_t sum = 0;
    for (int b = 0; b < num_blocks; b++) {
        int32_t val = histograms[b * RADIX_BUCKETS + bucket];
        histograms[b * RADIX_BUCKETS + bucket] = sum;
        sum += val;
    }
    offsets[bucket] = sum;

    // Now do exclusive prefix sum across buckets
    __syncthreads();

    // Thread 0 does the cross-bucket prefix sum
    if (threadIdx.x == 0) {
        int32_t total = 0;
        int32_t bucket_offsets[RADIX_BUCKETS];
        for (int b2 = 0; b2 < RADIX_BUCKETS; b2++) {
            bucket_offsets[b2] = total;
            total += offsets[b2];
        }
        for (int b2 = 0; b2 < RADIX_BUCKETS; b2++) {
            offsets[b2] = bucket_offsets[b2];
        }
    }
    __syncthreads();

    // Add bucket offset to all block histograms
    int32_t bucket_offset = offsets[bucket];
    for (int b = 0; b < num_blocks; b++) {
        histograms[b * RADIX_BUCKETS + bucket] += bucket_offset;
    }
}

__global__ void scatter_kernel(
    const uint64_t* __restrict__ keys_in,
    const int32_t* __restrict__ vals_in,
    uint64_t* __restrict__ keys_out,
    int32_t* __restrict__ vals_out,
    const int32_t* __restrict__ histograms,
    int32_t n,
    int shift)
{
    __shared__ int32_t s_offsets[RADIX_BUCKETS];

    // Load this block's histogram offsets
    if (threadIdx.x < RADIX_BUCKETS) {
        s_offsets[threadIdx.x] = histograms[blockIdx.x * RADIX_BUCKETS + threadIdx.x];
    }
    __syncthreads();

    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        int digit = (keys_in[i] >> shift) & (RADIX_BUCKETS - 1);
        int pos = atomicAdd(&s_offsets[digit], 1);
        keys_out[pos] = keys_in[i];
        vals_out[pos] = vals_in[i];
    }
}

void launch_radix_sort(
    u64* d_keys, i32* d_values, i32 n,
    u64* d_keys_alt, i32* d_values_alt,
    i32* d_histograms, i32* d_offsets,
    cudaStream_t stream)
{
    if (n <= 1) return;

    int grid = (n + RADIX_SORT_BLOCK - 1) / RADIX_SORT_BLOCK;

    usize hist_size = sizeof(i32) * static_cast<usize>(grid) * RADIX_BUCKETS;

    u64* src_keys = d_keys;
    i32* src_vals = d_values;
    u64* dst_keys = d_keys_alt;
    i32* dst_vals = d_values_alt;

    // 16 passes for 64-bit keys (4 bits per pass)
    for (int pass = 0; pass < 16; pass++) {
        int shift = pass * RADIX_BITS;

        // Step 1: Compute per-block histograms
        CUDA_CHECK(cudaMemsetAsync(d_histograms, 0, hist_size, stream));
        histogram_kernel<<<grid, RADIX_SORT_BLOCK, 0, stream>>>(
            src_keys, n, shift, d_histograms);
        CUDA_CHECK(cudaGetLastError());

        // Step 2: Prefix-sum histograms
        prefix_sum_histograms<<<1, RADIX_BUCKETS, 0, stream>>>(
            d_histograms, d_offsets, grid);
        CUDA_CHECK(cudaGetLastError());

        // Step 3: Scatter to output
        scatter_kernel<<<grid, RADIX_SORT_BLOCK, 0, stream>>>(
            src_keys, src_vals, dst_keys, dst_vals,
            d_histograms, n, shift);
        CUDA_CHECK(cudaGetLastError());

        // Swap buffers
        auto* tmp_k = src_keys; src_keys = dst_keys; dst_keys = tmp_k;
        auto* tmp_v = src_vals; src_vals = dst_vals; dst_vals = tmp_v;
    }

    // After 16 passes (even number), result is back in original buffers
    // (d_keys, d_values) — no final copy needed.
}

} // namespace celestial::cuda
