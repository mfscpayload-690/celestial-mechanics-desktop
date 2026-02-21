using CelestialMechanics.Math;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Gravitational wave strain estimator for compact binary systems.
///
/// For each pair of bodies, estimates the quadrupole-formula strain:
///
///   h ≈ 4·G²·m₁·m₂ / (c⁴·r·d)
///
/// where r = orbital separation, d = observer distance.
///
/// Also computes the gravitational wave energy loss rate:
///
///   dE/dt = -(32/5)·G⁴·(m₁·m₂)²·(m₁+m₂) / (c⁵·r⁵)
///
/// The total strain from all binary pairs is summed and recorded into a
/// <see cref="WaveformBuffer"/> each timestep.
///
/// All quantities are in simulation units (G=1, masses in M☉, distances in AU).
/// </summary>
public sealed class GravitationalWaveAnalyzer : IGravitationalWaveModel
{
    private readonly WaveformBuffer _waveform;

    /// <summary>
    /// Observer distance from the system in simulation units (AU).
    /// </summary>
    public double ObserverDistance { get; set; } = 1000.0;

    /// <summary>Access the internal waveform buffer for visualization.</summary>
    public WaveformBuffer Waveform => _waveform;

    /// <summary>Total energy radiated as gravitational waves since last reset.</summary>
    public double TotalRadiatedEnergy { get; private set; }

    // Precomputed constants in simulation units
    // h = 4·G²·m1·m2 / (c⁴·r·d)  →  G_Sim=1 → 4/(c⁴_sim)
    private static readonly double StrainPrefactor = 4.0 / PhysicalConstants.C_Sim4;

    // dE/dt = -(32/5)·G⁴·(m1·m2)²·(m1+m2) / (c⁵·r⁵)  →  G_Sim=1 → -32/(5·c⁵_sim)
    private static readonly double EnergyLossPrefactor = -32.0 / (5.0 * PhysicalConstants.C_Sim5);

    public GravitationalWaveAnalyzer(int bufferCapacity = 8192)
    {
        _waveform = new WaveformBuffer(bufferCapacity);
    }

    /// <inheritdoc/>
    public double ComputeEnergyLossRate(double mass1, double mass2, double separation)
    {
        if (separation <= 0.0) return 0.0;

        double m1m2 = mass1 * mass2;
        double mTotal = mass1 + mass2;
        double r5 = separation * separation * separation * separation * separation;

        return EnergyLossPrefactor * m1m2 * m1m2 * mTotal / r5;
    }

    /// <summary>
    /// Compute gravitational wave strain for a binary pair.
    /// </summary>
    public double ComputeStrain(double mass1, double mass2, double separation, double observerDist)
    {
        if (separation <= 0.0 || observerDist <= 0.0) return 0.0;

        return StrainPrefactor * mass1 * mass2 / (separation * observerDist);
    }

    /// <summary>
    /// Sample all binary pairs in the SoA buffer and add total strain to the waveform.
    /// Called once per timestep by the solver.
    /// </summary>
    /// <param name="bodies">Current body state.</param>
    /// <param name="time">Current simulation time.</param>
    /// <param name="dt">Current timestep (for energy loss integration).</param>
    public void Sample(BodySoA bodies, double time, double dt)
    {
        int n = bodies.Count;
        double totalStrain = 0.0;
        double totalEnergyLoss = 0.0;

        double[] px = bodies.PosX, py = bodies.PosY, pz = bodies.PosZ;
        double[] m = bodies.Mass;
        bool[] act = bodies.IsActive;

        for (int i = 0; i < n; i++)
        {
            if (!act[i] || m[i] <= 0.0) continue;

            for (int j = i + 1; j < n; j++)
            {
                if (!act[j] || m[j] <= 0.0) continue;

                double dx = px[j] - px[i];
                double dy = py[j] - py[i];
                double dz = pz[j] - pz[i];
                double dist = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist <= 0.0) continue;

                totalStrain += ComputeStrain(m[i], m[j], dist, ObserverDistance);
                totalEnergyLoss += ComputeEnergyLossRate(m[i], m[j], dist);
            }
        }

        _waveform.Add(time, totalStrain);
        TotalRadiatedEnergy += System.Math.Abs(totalEnergyLoss) * dt;
    }

    /// <summary>Reset waveform buffer and energy accumulator.</summary>
    public void Reset()
    {
        _waveform.Clear();
        TotalRadiatedEnergy = 0.0;
    }
}
