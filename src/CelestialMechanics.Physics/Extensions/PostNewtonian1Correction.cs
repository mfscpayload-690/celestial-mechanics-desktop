using CelestialMechanics.Math;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// First Post-Newtonian (1PN) correction to gravitational acceleration.
///
/// Adds terms of order v²/c² and GM/(rc²) to the Newtonian equations of motion.
/// Valid in the weak-field, slow-motion regime (v ≪ c, r ≫ Rs).
///
/// The 1PN correction for body i due to body j is (Einstein-Infeld-Hoffmann):
///
///   a_1PN = (G·mj / (r²·c²)) × {
///       n̂ · [ -v_i² - 2·v_j² + 4·(v_i·v_j) + 1.5·(n̂·v_j)²
///              + 5·G·mi/r + 4·G·mj/r ]
///     + (v_i - v_j) · [ 4·(n̂·v_i) - 3·(n̂·v_j) ]
///   }
///
/// where n̂ = (r_j - r_i) / |r_j - r_i|, r = |r_j - r_i|.
///
/// Safety guards:
///   • Returns zero if |v_rel| > 0.3·c (outside PN validity regime)
///   • Uses softening: r_eff = sqrt(r² + ε²)
///   • Warns if r < 3·Rs (strong-field regime)
///
/// References:
///   • Blanchet, Living Reviews in Relativity 17 (2014)
///   • Poisson & Will, "Gravity" (Cambridge, 2014), §9.3
/// </summary>
public sealed class PostNewtonian1Correction : IRelativisticCorrection
{
    /// <summary>
    /// Softening parameter ε for compatibility with the gravitational softening
    /// used by the Newtonian force calculation.
    /// </summary>
    public double SofteningEpsilon { get; set; } = 1e-4;

    /// <summary>
    /// Maximum relative velocity (as fraction of c) beyond which the 1PN
    /// correction is disabled. Default 0.3c — beyond this the PN expansion
    /// is unreliable.
    /// </summary>
    public double MaxVelocityFractionC { get; set; } = 0.3;

    /// <summary>
    /// Number of Schwarzschild radii within which a proximity warning is logged.
    /// Default: 3 Rs.
    /// </summary>
    public double SchwarzschildWarningFactor { get; set; } = 3.0;

    /// <summary>
    /// Optional callback for diagnostics/logging when a body pair enters
    /// the strong-field regime.
    /// </summary>
    public Action<int, int, double, double>? OnSchwarzschildProximityWarning { get; set; }

    // Precomputed constants (depend only on physical constants, not on runtime state)
    private static readonly double G = PhysicalConstants.G_Sim;
    private static readonly double InvC2 = 1.0 / PhysicalConstants.C_Sim2;
    private static readonly double MaxVelThreshold = 0.3 * PhysicalConstants.C_Sim;

    /// <inheritdoc/>
    public (double dax, double day, double daz) ComputeCorrection(
        double[] posX, double[] posY, double[] posZ,
        double[] velX, double[] velY, double[] velZ,
        double[] mass, int bodyIndex, int count)
    {
        double axi = 0.0, ayi = 0.0, azi = 0.0;

        double xi = posX[bodyIndex];
        double yi = posY[bodyIndex];
        double zi = posZ[bodyIndex];
        double vxi = velX[bodyIndex];
        double vyi = velY[bodyIndex];
        double vzi = velZ[bodyIndex];
        double mi = mass[bodyIndex];

        double vi2 = vxi * vxi + vyi * vyi + vzi * vzi;

        double eps2 = SofteningEpsilon * SofteningEpsilon;
        double maxVel2 = MaxVelThreshold * MaxVelThreshold;

        for (int j = 0; j < count; j++)
        {
            if (j == bodyIndex) continue;

            double mj = mass[j];
            if (mj <= 0.0) continue;

            // Relative position: r = pos_j - pos_i (vector from i to j)
            double dx = posX[j] - xi;
            double dy = posY[j] - yi;
            double dz = posZ[j] - zi;

            double dist2 = dx * dx + dy * dy + dz * dz + eps2;
            double dist = System.Math.Sqrt(dist2);
            double invDist = 1.0 / dist;

            // Relative velocity
            double dvx = vxi - velX[j];
            double dvy = vyi - velY[j];
            double dvz = vzi - velZ[j];
            double vRel2 = dvx * dvx + dvy * dvy + dvz * dvz;

            // Safety: skip if relative velocity exceeds 0.3c
            if (vRel2 > maxVel2)
                continue;

            // Schwarzschild radius proximity check
            double totalMass = mi + mj;
            double rs = PhysicalConstants.SchwarzschildFactorSim * totalMass;
            if (dist < SchwarzschildWarningFactor * rs)
            {
                OnSchwarzschildProximityWarning?.Invoke(bodyIndex, j, dist, rs);
                continue; // Skip correction in strong-field regime
            }

            // Unit vector from i to j
            double nx = dx * invDist;
            double ny = dy * invDist;
            double nz = dz * invDist;

            // Velocities of body j
            double vxj = velX[j];
            double vyj = velY[j];
            double vzj = velZ[j];
            double vj2 = vxj * vxj + vyj * vyj + vzj * vzj;

            // Dot products
            double viDotVj = vxi * vxj + vyi * vyj + vzi * vzj;
            double nDotVi = nx * vxi + ny * vyi + nz * vzi;
            double nDotVj = nx * vxj + ny * vyj + nz * vzj;

            // Gravitational potential terms: G·m / r
            double GmiOverR = G * mi * invDist;
            double GmjOverR = G * mj * invDist;

            // 1PN coefficient: G·mj / (r² · c²)
            double prefactor = GmjOverR * invDist * InvC2;

            // Term 1 (along n̂):
            //   -vi² - 2·vj² + 4·(vi·vj) + 1.5·(n̂·vj)² + 5·G·mi/r + 4·G·mj/r
            double term1Scalar =
                -vi2
                - 2.0 * vj2
                + 4.0 * viDotVj
                + 1.5 * nDotVj * nDotVj
                + 5.0 * GmiOverR
                + 4.0 * GmjOverR;

            // Term 2 (along v_i - v_j):
            //   4·(n̂·v_i) - 3·(n̂·v_j)
            double term2Scalar = 4.0 * nDotVi - 3.0 * nDotVj;

            // v_i - v_j (note: this is dvx, dvy, dvz already computed above)
            // But dvx = v_i - v_j which is what we need

            axi += prefactor * (term1Scalar * nx + term2Scalar * dvx);
            ayi += prefactor * (term1Scalar * ny + term2Scalar * dvy);
            azi += prefactor * (term1Scalar * nz + term2Scalar * dvz);
        }

        return (axi, ayi, azi);
    }

    /// <summary>
    /// Apply 1PN corrections to all active bodies in a SoA buffer.
    /// This operates on the AccX/AccY/AccZ arrays, adding the correction
    /// accelerations to the existing Newtonian values.
    /// </summary>
    public void ApplyCorrections(BodySoA bodies)
    {
        int n = bodies.Count;
        double[] px = bodies.PosX, py = bodies.PosY, pz = bodies.PosZ;
        double[] vx = bodies.VelX, vy = bodies.VelY, vz = bodies.VelZ;
        double[] ax = bodies.AccX, ay = bodies.AccY, az = bodies.AccZ;
        double[] m = bodies.Mass;
        bool[] act = bodies.IsActive;

        for (int i = 0; i < n; i++)
        {
            if (!act[i]) continue;

            var (dax, day, daz) = ComputeCorrection(px, py, pz, vx, vy, vz, m, i, n);
            ax[i] += dax;
            ay[i] += day;
            az[i] += daz;
        }
    }
}
