namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Extension point for gravitational wave energy loss.
///
/// Compact binary systems radiate gravitational waves, causing the
/// orbit to shrink (inspiral). The leading quadrupole formula gives:
///
///   dE/dt = -(32/5) · G⁴/(c⁵) · (m₁·m₂)² · (m₁+m₂) / r⁵
///
/// Future implementations should remove this energy from the system
/// and optionally adjust the orbit accordingly.
/// </summary>
public interface IGravitationalWaveModel
{
    /// <summary>
    /// Compute gravitational wave energy loss rate for a pair of bodies.
    /// </summary>
    /// <param name="mass1">Mass of body 1.</param>
    /// <param name="mass2">Mass of body 2.</param>
    /// <param name="separation">Orbital separation distance.</param>
    /// <returns>Energy loss rate (dE/dt), always ≤ 0.</returns>
    double ComputeEnergyLossRate(double mass1, double mass2, double separation);
}
