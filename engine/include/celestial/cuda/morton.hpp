#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::cuda {

/// 64-bit Morton code utilities for spatial sorting.
/// Encodes 3D positions into space-filling Z-curve codes for
/// locality-preserving body ordering (prerequisite for GPU octree).

/// Expand a 21-bit integer to 63 bits by inserting 2 zeros between each bit.
CELESTIAL_HOST_DEVICE inline u64 expand_bits_21(u64 v) {
    v &= 0x1FFFFF;  // 21 bits
    v = (v | (v << 32)) & 0x1F00000000FFFF;
    v = (v | (v << 16)) & 0x1F0000FF0000FF;
    v = (v | (v <<  8)) & 0x100F00F00F00F00F;
    v = (v | (v <<  4)) & 0x10C30C30C30C30C3;
    v = (v | (v <<  2)) & 0x1249249249249249;
    return v;
}

/// Compute 63-bit Morton code from 3 normalized coordinates [0, 2^21).
CELESTIAL_HOST_DEVICE inline u64 morton_encode_3d(u64 x, u64 y, u64 z) {
    return (expand_bits_21(x) << 2) | (expand_bits_21(y) << 1) | expand_bits_21(z);
}

/// Compute Morton code from normalized floating-point coordinates [0,1].
CELESTIAL_HOST_DEVICE inline u64 morton_from_unit(double ux, double uy, double uz) {
    constexpr double SCALE = (1 << 21) - 1;  // 2097151
    u64 ix = static_cast<u64>(ux * SCALE);
    u64 iy = static_cast<u64>(uy * SCALE);
    u64 iz = static_cast<u64>(uz * SCALE);
    return morton_encode_3d(ix, iy, iz);
}

// ── Kernel launch functions ──

/// Generate Morton codes for all particles on GPU.
/// Computes bounding box, normalizes positions, then bit-interleaves.
void launch_morton_codes(
    const double* d_pos_x, const double* d_pos_y, const double* d_pos_z,
    const u8* d_is_active, i32 n,
    u64* d_morton_codes, i32* d_indices,
    double bbox_min_x, double bbox_min_y, double bbox_min_z,
    double bbox_max_x, double bbox_max_y, double bbox_max_z,
    cudaStream_t stream);

/// GPU radix sort: sort Morton codes and associated body indices.
/// Stable, deterministic sort using device-wide radix sort.
/// d_histograms must point to pre-allocated buffer of size radix_sort_histogram_bytes(n).
/// d_offsets must point to pre-allocated buffer of size radix_sort_offset_bytes().
void launch_radix_sort(
    u64* d_keys, i32* d_values, i32 n,
    u64* d_keys_alt, i32* d_values_alt,
    i32* d_histograms, i32* d_offsets,
    cudaStream_t stream);

/// Compute the histogram buffer size required for radix sort of n elements.
inline usize radix_sort_histogram_bytes(i32 n) {
    constexpr int RADIX_SORT_BLOCK = 256;
    constexpr int RADIX_BUCKETS = 16;
    i32 grid = (n + RADIX_SORT_BLOCK - 1) / RADIX_SORT_BLOCK;
    return sizeof(i32) * static_cast<usize>(grid) * RADIX_BUCKETS;
}

/// Compute the offset buffer size required for radix sort.
inline usize radix_sort_offset_bytes() {
    constexpr int RADIX_BUCKETS = 16;
    return sizeof(i32) * RADIX_BUCKETS;
}

} // namespace celestial::cuda
