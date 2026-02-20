using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Multi-threaded CPU backend for N-body force calculation.
///
/// PARALLELISATION STRATEGY
/// -----------------------
/// The O(n²) force loop is embarrassingly parallel at the outer (i) level:
/// each body i's acceleration depends on the positions of all other bodies,
/// but the result for i is independent of the result for any other k≠i.
///
///   Parallel.For(0, n, i =>
///   {
///       double axi = 0;
///       for (int j = 0; j < n; j++) { axi -= G*m_j / r_ij³ * dx; }
///       AccX[i] = axi;          // ONLY i is written
///   });
///
/// Every iteration writes exclusively to AccX[i] / AccY[i] / AccZ[i].
/// No two iterations can write to the same index, so:
///   • No locks are needed.
///   • No thread-local allocations are required.
///   • No atomic operations are needed.
///
/// WHY WE DO NOT USE NEWTON'S 3RD LAW HERE
/// ----------------------------------------
/// <see cref="CpuSingleThreadBackend"/> uses the Newton pair trick: it iterates
/// j > i and writes both AccX[i] and AccX[j] in the same iteration, halving
/// arithmetic. In a parallel setting, thread i and thread j would both try to
/// write AccX[j] (or AccX[i]) concurrently — a data race. Eliminating the
/// optimisation is the correct, simple solution: each thread computes all n-1
/// contributions for its assigned i independently (O(n) work per thread ×
/// n/T threads = O(n²/T) total ≈ linear scaling with thread count T).
///
/// FALSE SHARING (AWARENESS, NOT A PROBLEM HERE)
/// -----------------------------------------------
/// A CPU cache line is 64 bytes = 8 consecutive doubles. If two threads write
/// to AccX[i] and AccX[i+1] on the same cache line simultaneously, the CPU
/// must invalidate and re-fetch that line on the remote core — false sharing.
///
/// .NET's Parallel.For uses range partitioning: each thread receives a
/// contiguous block of indices (e.g. [0..249], [250..499], ...). Within one
/// block, all writes are sequential; false sharing only occurs at the two
/// boundary doubles between adjacent blocks. With n ≥ 100 bodies (800 B+
/// of acc data) and 4–8 threads, at most 2 × (T-1) boundary doubles are
/// contended — negligible compared to the n writes total.
///
/// REDUCTION IS UNNECESSARY
/// -------------------------
/// Because each Parallel.For iteration writes to a disjoint memory location
/// (AccX[i]), no reduction (sum-into-shared accumulator) is required. This
/// avoids both the overhead and the non-determinism that reduction introduces.
///
/// NON-DETERMINISM NOTE
/// --------------------
/// Floating-point addition is not associative: (a + b) + c ≠ a + (b + c) in
/// general. The order in which threads execute and flush their results can
/// change across runs, producing slightly different bit-level results each
/// time. Use <see cref="CpuSingleThreadBackend"/> when reproducibility is
/// required (<c>PhysicsConfig.DeterministicMode = true</c>).
/// </summary>
public sealed class CpuParallelBackend : IPhysicsComputeBackend
{
    /// <inheritdoc/>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        int n = bodies.Count;
        double eps2 = softening * softening;

        // ── Preload array references into locals ─────────────────────────────
        // These are read-only inside the parallel lambda; capturing the local
        // variable rather than bodies.PosX avoids an extra indirection per
        // iteration inside the hot inner loop.
        double[] px  = bodies.PosX;
        double[] py  = bodies.PosY;
        double[] pz  = bodies.PosZ;
        double[] ax  = bodies.AccX;
        double[] ay  = bodies.AccY;
        double[] az  = bodies.AccZ;
        double[] m   = bodies.Mass;
        bool[]   act = bodies.IsActive;

        // ── Zero accelerations ───────────────────────────────────────────────
        // Array.Clear uses an optimised (possibly SIMD) memset path on .NET 8.
        // Must clear before writing; we do NOT use += inside the parallel loop
        // because each iteration overwrites AccX[i] at the end (not increments).
        Array.Clear(ax, 0, n);
        Array.Clear(ay, 0, n);
        Array.Clear(az, 0, n);

        // ── Parallel outer loop over body index i ────────────────────────────
        // Each iteration handles exactly one i and writes only to ax[i].
        // Parallel.For auto-partitions into contiguous ranges per thread.
        Parallel.For(0, n, i =>
        {
            if (!act[i]) return;

            // Pre-fetch body i into scalars — kept in registers for the inner
            // loop, avoiding n repeated array reads through the same index.
            double xi  = px[i];
            double yi  = py[i];
            double zi  = pz[i];

            // Local accumulators: no writes to shared arrays until the end.
            double axi = 0.0, ayi = 0.0, azi = 0.0;

            for (int j = 0; j < n; j++)
            {
                if (j == i || !act[j]) continue;

                // ── Displacement vector r_ij = position_i − position_j ──────
                double dx = xi - px[j];
                double dy = yi - py[j];
                double dz = zi - pz[j];

                // ── Softened inverse-cube factor ─────────────────────────────
                // dist² + ε² prevents divergence at r → 0 (Plummer softening).
                double dist2    = dx * dx + dy * dy + dz * dz + eps2;
                double invDist  = 1.0 / System.Math.Sqrt(dist2);
                double invDist3 = invDist * invDist * invDist;

                // ── Gravitational acceleration contribution (G = 1) ──────────
                // a_i += G·m_j / |r_ij|³ · (pos_j − pos_i)
                //      = −m_j · invDist3 · (pos_i − pos_j)
                double factor = m[j] * invDist3;
                axi -= factor * dx;
                ayi -= factor * dy;
                azi -= factor * dz;
            }

            // ── Single write to the shared array ─────────────────────────────
            // The entire inner loop accumulated into thread-local scalars.
            // This is the only moment AccX[i] is touched; no race condition.
            ax[i] = axi;
            ay[i] = ayi;
            az[i] = azi;
        });
    }
}
