namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Extension point for accretion disk modeling.
///
/// Accretion disks form when matter falls toward a compact object
/// (black hole, neutron star) but has too much angular momentum to
/// fall directly in. The disk converts gravitational potential energy
/// into thermal radiation.
///
/// Future implementations model mass accretion rate, disk temperature
/// profile, and luminosity for rendering.
/// </summary>
public interface IAccretionModel
{
    /// <summary>
    /// Compute the accretion rate for a compact object at the given
    /// index, based on surrounding matter density and relative velocities.
    /// </summary>
    /// <param name="compactBodyIndex">Index of the compact object.</param>
    /// <param name="mass">Masses of all bodies.</param>
    /// <param name="posX">X positions of all bodies.</param>
    /// <param name="posY">Y positions of all bodies.</param>
    /// <param name="posZ">Z positions of all bodies.</param>
    /// <param name="count">Total number of bodies.</param>
    /// <returns>Mass accretion rate (dm/dt).</returns>
    double ComputeAccretionRate(int compactBodyIndex,
        double[] mass, double[] posX, double[] posY, double[] posZ, int count);
}
