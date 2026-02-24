#include <celestial/cuda/cuda_check.hpp>
#include <celestial/cuda/kernel_config.hpp>
#include <celestial/cuda/gpu_reduction.hpp>
#include <celestial/core/types.hpp>
#include <vector>
#include <cmath>

namespace celestial::cuda {

// ═════════════════════════════════════════════════════════════════════════
// GPU POTENTIAL ENERGY — Phase 18+19
//
// Computes gravitational potential energy using Barnes-Hut tree traversal.
// For each body i:   phi_i = -sum_j(m_j / |r_i - r_j|)  (via BH approx)
// Total PE = 0.5 * sum_i(m_i * phi_i)  (factor 0.5 avoids double-counting)
//
// Uses the same BH traversal as the force kernel (same theta criterion)
// but computes potential instead of force.
//
// The per-body potential is reduced using the deterministic reduction
// framework (MODULE 5).
// ═════════════════════════════════════════════════════════════════════════

static constexpr int PE_BLOCK = 256;
static constexpr int PE_TREE_STACK_SIZE = 128;

/// Per-body gravitational potential via Barnes-Hut tree traversal.
/// Writes phi_i to out_potential[i] for each active body i.
__global__ void compute_potential_bh_kernel(
    const double* __restrict__ pos_x,
    const double* __restrict__ pos_y,
    const double* __restrict__ pos_z,
    const double* __restrict__ mass,
    const uint8_t* __restrict__ is_active,
    int32_t n,
    // Tree SoA
    const double* __restrict__ node_mass,
    const double* __restrict__ node_com_x,
    const double* __restrict__ node_com_y,
    const double* __restrict__ node_com_z,
    const double* __restrict__ node_half_size,
    const int32_t* __restrict__ node_children,
    const int32_t* __restrict__ node_body,
    const uint8_t* __restrict__ node_is_leaf,
    double eps2,
    double theta2,
    // Output: weighted potential per body (m_i * phi_i)
    double* __restrict__ out_m_phi)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n || !is_active[i]) {
        if (i < n) out_m_phi[i] = 0.0;
        return;
    }

    double bx = pos_x[i], by = pos_y[i], bz = pos_z[i];
    double mi = mass[i];
    double phi = 0.0;

    // Stack-based iterative traversal (same as force kernel)
    int32_t stack[PE_TREE_STACK_SIZE];
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

            double soft_dist2 = dist2 + eps2;
            double dist = sqrt(soft_dist2);
            phi -= nm / dist;
            continue;
        }

        // Opening angle criterion
        double hs = node_half_size[node];
        double node_size2 = 4.0 * hs * hs;

        if (dist2 > 0.0 && node_size2 < theta2 * dist2) {
            // Monopole approximation
            double soft_dist2 = dist2 + eps2;
            double dist = sqrt(soft_dist2);
            phi -= nm / dist;
            continue;
        }

        // Open node: push children (deterministic order 0-7)
        const int32_t* ch = &node_children[node * 8];
        #pragma unroll
        for (int c = 7; c >= 0; c--) {
            if (ch[c] >= 0 && sp < PE_TREE_STACK_SIZE) {
                stack[sp++] = ch[c];
            }
        }
    }

    // Output m_i * phi_i (so total PE = 0.5 * sum(m_phi))
    out_m_phi[i] = mi * phi;
}

// ═════════════════════════════════════════════════════════════════════════
// HOST LAUNCH: GPU PE computation
// ═════════════════════════════════════════════════════════════════════════

/// Compute total gravitational potential energy on GPU using Barnes-Hut tree.
/// Requires that the tree has already been built for this frame.
///
/// Args:
///   d_pos_{x,y,z}, d_mass, d_is_active: particle SoA on device
///   n: number of particles
///   tree_*: tree SoA arrays (already built)
///   softening, theta: BH parameters
///   d_scratch_mphi: scratch array of n doubles for per-body m*phi
///   d_block_out: scratch array of ceil(n/256) doubles for reduction
///   out_pe: output potential energy value
///   stream: CUDA stream
void launch_gpu_potential_energy(
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
    cudaStream_t stream)
{
    if (n <= 0) { out_pe = 0.0; return; }

    double eps2 = softening * softening;
    double theta2 = theta * theta;

    int grid = (n + PE_BLOCK - 1) / PE_BLOCK;

    // Step 1: Per-body potential via BH traversal
    compute_potential_bh_kernel<<<grid, PE_BLOCK, 0, stream>>>(
        d_pos_x, d_pos_y, d_pos_z, d_mass, d_is_active, n,
        d_node_mass, d_node_com_x, d_node_com_y, d_node_com_z,
        d_node_half_size, d_node_children, d_node_body, d_node_is_leaf,
        eps2, theta2,
        d_scratch_mphi);
    CUDA_CHECK(cudaGetLastError());

    // Step 2: Deterministic reduction of m_i * phi_i
    double sum_mphi = 0.0;
    gpu_reduce_sum(d_scratch_mphi, nullptr, n, d_block_out, sum_mphi, stream);

    // PE = 0.5 * sum(m_i * phi_i) to avoid double-counting
    out_pe = 0.5 * sum_mphi;
}

} // namespace celestial::cuda
