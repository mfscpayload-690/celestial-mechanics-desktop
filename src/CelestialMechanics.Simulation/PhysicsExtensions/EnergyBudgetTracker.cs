using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.PhysicsExtensions;

/// <summary>
/// Tracks energy budget across the simulation to validate conservation.
/// Records kinetic energy, potential energy, explosion energy injections,
/// and gravitational wave energy losses. Validates total drift stays within tolerance.
/// </summary>
public sealed class EnergyBudgetTracker
{
    /// <summary>Total kinetic energy at last measurement.</summary>
    public double KineticEnergy { get; private set; }

    /// <summary>Total gravitational potential energy at last measurement.</summary>
    public double PotentialEnergy { get; private set; }

    /// <summary>Cumulative energy injected by explosions/supernovae.</summary>
    public double ExplosionEnergyInjected { get; private set; }

    /// <summary>Cumulative energy radiated as gravitational waves.</summary>
    public double GravitationalWaveEnergyLoss { get; private set; }

    /// <summary>Cumulative energy lost to merger mass-energy conversion.</summary>
    public double MergerEnergyLoss { get; private set; }

    /// <summary>Initial total energy (set on first measurement).</summary>
    public double InitialTotalEnergy { get; private set; }

    /// <summary>Whether the initial energy has been captured.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Total mechanical energy: KE + PE.
    /// Does not include injection/loss terms (those are tracked separately).
    /// </summary>
    public double TotalMechanicalEnergy => KineticEnergy + PotentialEnergy;

    /// <summary>
    /// Effective total energy accounting for all sources and sinks:
    /// E_eff = KE + PE - ExplosionInjected + GWLoss + MergerLoss
    /// Should remain approximately constant.
    /// </summary>
    public double EffectiveTotalEnergy =>
        TotalMechanicalEnergy - ExplosionEnergyInjected + GravitationalWaveEnergyLoss + MergerEnergyLoss;

    /// <summary>
    /// Fractional energy drift from initial state.
    /// Returns 0 if not yet initialized.
    /// </summary>
    public double EnergyDrift
    {
        get
        {
            if (!IsInitialized || System.Math.Abs(InitialTotalEnergy) < 1e-30)
                return 0.0;
            return System.Math.Abs(EffectiveTotalEnergy - InitialTotalEnergy) /
                   System.Math.Abs(InitialTotalEnergy);
        }
    }

    /// <summary>
    /// Measure current kinetic and potential energy from entity list.
    /// KE = Σ 0.5 * m * v²
    /// PE = -Σ G * m_i * m_j / r_ij (pairwise)
    /// </summary>
    public void Measure(IReadOnlyList<Entity> entities)
    {
        double ke = 0.0;
        double pe = 0.0;

        // Collect active physics components
        int count = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].IsActive) continue;
            var pc = entities[i].GetComponent<PhysicsComponent>();
            if (pc == null) continue;
            count++;
        }

        // Build temporary arrays for PE calculation (no allocation if count unchanged)
        int idx = 0;
        Span<int> indices = count <= 256 ? stackalloc int[count] : new int[count];
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].IsActive) continue;
            var pc = entities[i].GetComponent<PhysicsComponent>();
            if (pc == null) continue;
            indices[idx++] = i;
        }

        // Kinetic energy
        for (int a = 0; a < count; a++)
        {
            var pc = entities[indices[a]].GetComponent<PhysicsComponent>()!;
            ke += 0.5 * pc.Mass * pc.Velocity.LengthSquared;
        }

        // Potential energy (pairwise)
        for (int a = 0; a < count; a++)
        {
            var pcA = entities[indices[a]].GetComponent<PhysicsComponent>()!;
            for (int b = a + 1; b < count; b++)
            {
                var pcB = entities[indices[b]].GetComponent<PhysicsComponent>()!;
                double dist = pcA.Position.DistanceTo(pcB.Position);
                if (dist > 1e-15)
                {
                    pe -= PhysicalConstants.G_Sim * pcA.Mass * pcB.Mass / dist;
                }
            }
        }

        KineticEnergy = ke;
        PotentialEnergy = pe;

        if (!IsInitialized)
        {
            InitialTotalEnergy = EffectiveTotalEnergy;
            IsInitialized = true;
        }
    }

    /// <summary>Record energy injected by an explosion event.</summary>
    public void RecordExplosionEnergy(double energy)
    {
        ExplosionEnergyInjected += energy;
    }

    /// <summary>Record energy lost to gravitational wave radiation.</summary>
    public void RecordGravitationalWaveLoss(double energy)
    {
        GravitationalWaveEnergyLoss += energy;
    }

    /// <summary>Record energy lost in a merger (binding energy released).</summary>
    public void RecordMergerLoss(double energy)
    {
        MergerEnergyLoss += energy;
    }

    /// <summary>
    /// Check whether energy drift is within tolerance.
    /// </summary>
    /// <param name="tolerance">Maximum allowed fractional drift (default 5%).</param>
    public bool IsWithinTolerance(double tolerance = 0.05)
    {
        return EnergyDrift <= tolerance;
    }

    /// <summary>Reset all tracked values.</summary>
    public void Reset()
    {
        KineticEnergy = 0.0;
        PotentialEnergy = 0.0;
        ExplosionEnergyInjected = 0.0;
        GravitationalWaveEnergyLoss = 0.0;
        MergerEnergyLoss = 0.0;
        InitialTotalEnergy = 0.0;
        IsInitialized = false;
    }
}
