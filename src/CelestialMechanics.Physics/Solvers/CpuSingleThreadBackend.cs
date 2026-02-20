using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Single-threaded CPU backend for N-body force calculation.
///
/// CACHE LOCALITY EXPLANATION
/// --------------------------
/// This solver operates directly on the SoA (Structure-of-Arrays) arrays
/// rather than going through <c>PhysicsBody</c> objects or <c>Vec3d</c> structs.
///
/// In a tight O(n²) loop, the critical path is the inner loop over j. At each
/// iteration we need:
///
///     dx = PosX[i] - PosX[j]
///     dy = PosY[i] - PosY[j]
///     dz = PosZ[i] - PosZ[j]
///     mj = Mass[j]
///
/// Because PosX, PosY, PosZ, and Mass are separate contiguous arrays, the CPU's
/// hardware prefetcher can detect the sequential stride-1 access pattern (j++
/// in index) and load upcoming cache lines before they are needed. A 64-byte
/// L1 cache line holds 8 doubles; after the first cache miss at j=0 for each
/// array, the next 7 iterations hit L1 cache for free.
///
/// In the AoS layout, each body occupies ~120 bytes. With 5 cache lines per body,
/// accessing PosX[j] forces the CPU to load Velocity, Acceleration, and other
/// fields we don't need — wasting 80% of each cache line.
///
/// NEWTON'S 3RD LAW OPTIMISATION (symmetric force accumulation)
/// ------------------------------------------------------------
/// We use the newton pair trick: iterate i from 0..n-1, j from i+1..n-1.
/// This computes exactly n*(n-1)/2 pairs instead of n*(n-1) — halving the
/// arithmetic. The reaction force on j is -F_ij (Newton's 3rd law), so we
/// apply +acc to i and -acc to j in the same iteration:
///
///     AccX[i] += ax;   AccX[j] -= ax;
///     AccY[i] += ay;   AccY[j] -= ay;
///
/// This is safe because single-threaded execution guarantees no race conditions
/// on AccX[j]. The parallel backend does NOT use this trick (see
/// <see cref="CpuParallelBackend"/> for explanation).
/// </summary>
public sealed class CpuSingleThreadBackend : IPhysicsComputeBackend
{
    /// <inheritdoc/>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        int n = bodies.Count;
        double eps2 = softening * softening;   // ε² — computed once, reused every pair

        // ── Preload arrays into locals so the JIT keeps them in registers ──────
        // This eliminates repeated bounds-checked array loads from the object header.
        double[] px  = bodies.PosX;
        double[] py  = bodies.PosY;
        double[] pz  = bodies.PosZ;
        double[] ax  = bodies.AccX;
        double[] ay  = bodies.AccY;
        double[] az  = bodies.AccZ;
        double[] m   = bodies.Mass;
        bool[]   act = bodies.IsActive;

        // ── Zero accelerations ─────────────────────────────────────────────────
        // Must clear before accumulation; buffers are reused across steps.
        for (int i = 0; i < n; i++)
        {
            ax[i] = 0.0;
            ay[i] = 0.0;
            az[i] = 0.0;
        }

        // ── O(n²/2) pairwise force loop ────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            if (!act[i]) continue;

            // Pre-fetch body i's position and mass into scalars.
            // Keeping these in local variables avoids repeated array indexing
            // inside the inner loop, giving the JIT a chance to register-allocate them.
            double xi = px[i];
            double yi = py[i];
            double zi = pz[i];
            double mi = m[i];

            double axi = 0.0, ayi = 0.0, azi = 0.0;   // accumulate into locals

            for (int j = i + 1; j < n; j++)
            {
                if (!act[j]) continue;

                // ── Displacement vector r_ij = position_i - position_j ─────────
                double dx = xi - px[j];
                double dy = yi - py[j];
                double dz = zi - pz[j];

                // ── Softened distance: r² + ε² ────────────────────────────────
                // Softening ε prevents the denominator from going to zero when
                // two bodies pass very close. A small constant ε adds a "plummer
                // softening" that rounds off the potential at short range.
                double dist2    = dx * dx + dy * dy + dz * dz + eps2;
                double invDist  = 1.0 / System.Math.Sqrt(dist2);
                double invDist3 = invDist * invDist * invDist;   // 1/(r²+ε²)^(3/2)

                // ── Gravitational acceleration magnitude ───────────────────────
                // G = 1 in simulation units. Force per unit mass on i from j:
                //   a_ij = G·m_j / |r_ij|³  ·  r_ij  (directed toward j)
                // The sign is negative because dx = pos_i - pos_j, meaning the
                // force on i points in the -dx direction (attracted toward j).
                double mj        = m[j];
                double factor_ij = mj * invDist3;          // m_j / r³
                double factor_ji = mi * invDist3;          // m_i / r³

                // acc_i  += -G·m_j / r³ · (pos_i - pos_j)  →  sign: -dx
                axi -= factor_ij * dx;
                ayi -= factor_ij * dy;
                azi -= factor_ij * dz;

                // acc_j  += +G·m_i / r³ · (pos_i - pos_j)  →  sign: +dx  (Newton's 3rd)
                ax[j] += factor_ji * dx;
                ay[j] += factor_ji * dy;
                az[j] += factor_ji * dz;
            }

            // Flush local accumulator back to the array once per i.
            // This is cheaper than writing ax[i] n-i times inside the loop.
            ax[i] += axi;
            ay[i] += ayi;
            az[i] += azi;
        }
    }
}
