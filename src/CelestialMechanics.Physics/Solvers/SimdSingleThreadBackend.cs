using System.Numerics;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// SIMD-vectorised CPU backend for N-body force calculation.
///
/// Uses <see cref="Vector{T}"/> with <see cref="double"/> for AVX2/SSE2
/// vectorisation of the inner force loop. Processes bodies in chunks of
/// <see cref="Vector{Double}.Count"/> (typically 4 for AVX2, 2 for SSE2).
///
/// VECTORISATION STRATEGY
/// ----------------------
/// The outer loop iterates over body i. The inner loop processes body j in
/// SIMD-width chunks:
///
///   1. Broadcast body i's position (xi, yi, zi) and accumulator into vectors
///   2. Load body j's positions as contiguous Vector&lt;double&gt; chunks
///   3. Compute dx, dy, dz as vector subtractions
///   4. Compute dist² = dx² + dy² + dz² + ε² as vector FMA
///   5. Compute invDist = 1/sqrt(dist²) via vector operations
///   6. Accumulate force contributions into vector accumulators
///   7. Scalar tail loop for remaining bodies (j count not multiple of SIMD width)
///
/// This backend does NOT use Newton's 3rd law symmetry because writing back
/// to j's accumulator during the vectorised inner loop would require scatter
/// operations that negate the SIMD benefit. The full n*(n-1) pairs are computed.
///
/// Expected speedup: 1.5-3× over scalar on large n (≥ 500 bodies).
///
/// FALLBACK
/// --------
/// If <see cref="Vector.IsHardwareAccelerated"/> is false, delegates to
/// <see cref="CpuSingleThreadBackend"/> for identical results.
/// </summary>
public sealed class SimdSingleThreadBackend : IPhysicsComputeBackend, IGravityModelAwareBackend
{
    private readonly CpuSingleThreadBackend _scalarFallback = new();
    public bool EnableShellTheorem { get; set; }

    /// <inheritdoc/>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        if (EnableShellTheorem)
        {
            _scalarFallback.EnableShellTheorem = true;
            _scalarFallback.ComputeForces(bodies, softening);
            return;
        }

        if (!Vector.IsHardwareAccelerated)
        {
            _scalarFallback.ComputeForces(bodies, softening);
            return;
        }

        int n = bodies.Count;
        int vecSize = Vector<double>.Count;
        double eps2 = softening * softening;

        double[] px  = bodies.PosX;
        double[] py  = bodies.PosY;
        double[] pz  = bodies.PosZ;
        double[] ax  = bodies.AccX;
        double[] ay  = bodies.AccY;
        double[] az  = bodies.AccZ;
        double[] m   = bodies.Mass;
        bool[]   act = bodies.IsActive;

        // ── Zero accelerations ─────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            ax[i] = 0.0;
            ay[i] = 0.0;
            az[i] = 0.0;
        }

        var eps2Vec = new Vector<double>(eps2);

        // ── O(n²) force loop with SIMD inner loop ──────────────────────────
        for (int i = 0; i < n; i++)
        {
            if (!act[i]) continue;

            double xi = px[i], yi = py[i], zi = pz[i];
            var xiVec = new Vector<double>(xi);
            var yiVec = new Vector<double>(yi);
            var ziVec = new Vector<double>(zi);

            double axi = 0.0, ayi = 0.0, azi = 0.0;

            // ── Vectorised inner loop ──────────────────────────────────────
            int j = 0;
            int vecLimit = n - (n % vecSize);

            for (; j < vecLimit; j += vecSize)
            {
                // Load j-th chunk of positions and masses
                var pxj = new Vector<double>(px, j);
                var pyj = new Vector<double>(py, j);
                var pzj = new Vector<double>(pz, j);
                var mj  = new Vector<double>(m, j);

                // Displacement
                var dx = xiVec - pxj;
                var dy = yiVec - pyj;
                var dz = ziVec - pzj;

                // Softened distance squared
                var dist2 = dx * dx + dy * dy + dz * dz + eps2Vec;

                // invDist = 1 / sqrt(dist2)
                // Use reciprocal sqrt: not available as single intrinsic for
                // Vector<double>, so compute via division.
                var dist = Vector.SquareRoot(dist2);
                var invDist = Vector<double>.One / dist;
                var invDist3 = invDist * invDist * invDist;

                // Force factor: -G·m_j / r³ (G=1 in sim units)
                // Sign: dx = pos_i - pos_j, force on i is toward j → -dx
                var factor = mj * invDist3;

                // Mask out self-interaction (j == i) and inactive bodies
                // For self-interaction: when j == i, dx=dy=dz=0, dist2 = eps2,
                // which gives a small but nonzero force. We must zero it out.
                // Also mask inactive bodies.
                for (int k = 0; k < vecSize; k++)
                {
                    int jk = j + k;
                    if (jk == i || !act[jk])
                    {
                        // Zero out this lane by not accumulating
                        continue;
                    }
                    axi -= factor[k] * dx[k];
                    ayi -= factor[k] * dy[k];
                    azi -= factor[k] * dz[k];
                }
            }

            // ── Scalar tail loop ───────────────────────────────────────────
            for (; j < n; j++)
            {
                if (j == i || !act[j]) continue;

                double dx = xi - px[j];
                double dy = yi - py[j];
                double dz = zi - pz[j];

                double dist2 = dx * dx + dy * dy + dz * dz + eps2;
                double invDist = 1.0 / System.Math.Sqrt(dist2);
                double invDist3 = invDist * invDist * invDist;

                double factor = m[j] * invDist3;
                axi -= factor * dx;
                ayi -= factor * dy;
                azi -= factor * dz;
            }

            ax[i] = axi;
            ay[i] = ayi;
            az[i] = azi;
        }
    }
}
