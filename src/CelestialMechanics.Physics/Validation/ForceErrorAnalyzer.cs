using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Solvers;

namespace CelestialMechanics.Physics.Validation;

/// <summary>
/// Compares Barnes-Hut force approximation against exact brute-force computation.
///
/// PURPOSE
/// -------
/// Measures the accuracy penalty of the Barnes-Hut tree approximation relative
/// to the O(n²) exact computation. This is essential for understanding how the
/// opening angle θ affects force accuracy.
///
/// METRICS
/// -------
/// • Per-body relative acceleration error: |a_BH - a_exact| / |a_exact|
/// • Mean relative error across all active bodies
/// • Maximum relative error across all active bodies
/// • RMS relative error
///
/// USAGE
/// -----
/// var analyzer = new ForceErrorAnalyzer();
/// var result = analyzer.CompareForces(bodies, softening: 1e-4, theta: 0.5);
/// Console.WriteLine($"Mean error: {result.MeanRelativeError:E3}");
///
/// θ TUNING GUIDE
/// ─────────────
/// θ = 0.0 → error = 0 (exact), but O(n²) performance
/// θ = 0.3 → error ~0.01%, excellent accuracy, ~2x slower than θ=0.5
/// θ = 0.5 → error ~0.1%, standard choice for scientific simulations
/// θ = 0.7 → error ~0.5%, acceptable for many applications
/// θ = 1.0 → error ~1-3%, suitable for visualisation
/// θ ≥ 1.5 → error ~10%+, visual-only quality
/// </summary>
public sealed class ForceErrorAnalyzer
{
    /// <summary>
    /// Result of a force accuracy comparison.
    /// </summary>
    public sealed class ComparisonResult
    {
        /// <summary>Mean |a_BH - a_exact| / |a_exact| across all active bodies.</summary>
        public double MeanRelativeError { get; init; }
        /// <summary>Maximum relative error across all active bodies.</summary>
        public double MaxRelativeError { get; init; }
        /// <summary>RMS relative error.</summary>
        public double RmsRelativeError { get; init; }
        /// <summary>Number of active bodies compared.</summary>
        public int BodyCount { get; init; }
        /// <summary>θ value used for Barnes-Hut.</summary>
        public double Theta { get; init; }

        public override string ToString() =>
            $"θ={Theta:F2} | n={BodyCount,5} | mean={MeanRelativeError:E3} | " +
            $"max={MaxRelativeError:E3} | rms={RmsRelativeError:E3}";
    }

    /// <summary>
    /// Compare Barnes-Hut forces against brute-force exact forces.
    /// </summary>
    /// <param name="bodies">SoA body buffer with positions and masses set.</param>
    /// <param name="softening">Softening parameter ε.</param>
    /// <param name="theta">Opening angle θ for Barnes-Hut.</param>
    /// <returns>Comparison metrics.</returns>
    public ComparisonResult CompareForces(BodySoA bodies, double softening, double theta)
    {
        int n = bodies.Count;

        // ── Compute exact forces (brute-force) ────────────────────────────────
        // We use a temporary copy of the acceleration arrays so the original
        // body state is not modified.
        double[] exactAx = new double[n];
        double[] exactAy = new double[n];
        double[] exactAz = new double[n];

        var bruteForce = new CpuSingleThreadBackend();
        bruteForce.ComputeForces(bodies, softening);

        // Save exact results
        Array.Copy(bodies.AccX, exactAx, n);
        Array.Copy(bodies.AccY, exactAy, n);
        Array.Copy(bodies.AccZ, exactAz, n);

        // ── Compute Barnes-Hut forces ─────────────────────────────────────────
        var barnesHut = new BarnesHutBackend
        {
            Theta = theta,
            UseParallel = false // deterministic for comparison
        };
        barnesHut.ComputeForces(bodies, softening);

        // ── Compute per-body relative error ───────────────────────────────────
        double sumError = 0.0;
        double sumError2 = 0.0;
        double maxError = 0.0;
        int activeCount = 0;

        for (int i = 0; i < n; i++)
        {
            if (!bodies.IsActive[i]) continue;

            double ex = exactAx[i];
            double ey = exactAy[i];
            double ez = exactAz[i];
            double exactMag = System.Math.Sqrt(ex * ex + ey * ey + ez * ez);

            if (exactMag < 1e-30) continue; // skip bodies with near-zero force

            double dx = bodies.AccX[i] - ex;
            double dy = bodies.AccY[i] - ey;
            double dz = bodies.AccZ[i] - ez;
            double errorMag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double relError = errorMag / exactMag;

            sumError += relError;
            sumError2 += relError * relError;
            if (relError > maxError) maxError = relError;
            activeCount++;
        }

        double meanError = activeCount > 0 ? sumError / activeCount : 0.0;
        double rmsError = activeCount > 0 ? System.Math.Sqrt(sumError2 / activeCount) : 0.0;

        return new ComparisonResult
        {
            MeanRelativeError = meanError,
            MaxRelativeError = maxError,
            RmsRelativeError = rmsError,
            BodyCount = activeCount,
            Theta = theta
        };
    }
}
