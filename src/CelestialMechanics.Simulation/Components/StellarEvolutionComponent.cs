using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Components;

/// <summary>
/// Tracks stellar evolution state: age, core mass growth, fuel depletion,
/// and luminosity. Signals supernova readiness when core mass exceeds
/// the Chandrasekhar limit (≈ 1.44 M☉).
///
/// This component does NOT perform the explosion — it only evolves state.
/// The SupernovaTrigger checks collapse conditions externally.
/// </summary>
public sealed class StellarEvolutionComponent : IComponent
{
    /// <summary>Age within the simulation in time units.</summary>
    public double Age { get; set; }

    /// <summary>Mass of the stellar core (grows as fuel burns).</summary>
    public double CoreMass { get; set; }

    /// <summary>Remaining hydrogen/helium fuel mass.</summary>
    public double FuelMass { get; set; }

    /// <summary>Luminosity in simulation units (proportional to mass^3.5).</summary>
    public double Luminosity { get; private set; }

    /// <summary>
    /// Chandrasekhar limit: ≈ 1.44 M☉. When CoreMass reaches this,
    /// the star is eligible for core-collapse supernova.
    /// </summary>
    public double CollapseThreshold { get; set; } = 1.44 * PhysicalConstants.SolarMass / PhysicalConstants.SolarMass; // 1.44 in solar mass units

    /// <summary>
    /// Rate at which fuel converts to core mass per unit time.
    /// Higher for more massive stars (faster evolution).
    /// </summary>
    public double BurnRate { get; set; } = 0.001;

    /// <summary>Set to true once the star has undergone collapse.</summary>
    public bool HasCollapsed { get; set; }

    public StellarEvolutionComponent() { }

    public StellarEvolutionComponent(double initialFuelMass, double initialCoreMass, double burnRate)
    {
        FuelMass = initialFuelMass;
        CoreMass = initialCoreMass;
        BurnRate = burnRate;
    }

    public void Update(double dt)
    {
        if (HasCollapsed) return;

        Age += dt;

        // Burn fuel → grow core
        double burned = System.Math.Min(FuelMass, BurnRate * dt);
        FuelMass -= burned;
        CoreMass += burned;

        // Mass-luminosity relation: L ∝ M^3.5 (main sequence approximation)
        double totalMass = CoreMass + FuelMass;
        Luminosity = totalMass > 0.0
            ? System.Math.Pow(totalMass, 3.5)
            : 0.0;
    }
}
