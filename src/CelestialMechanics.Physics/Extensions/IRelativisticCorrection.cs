namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Extension point for post-Newtonian relativistic corrections.
///
/// Post-Newtonian (PN) corrections add terms proportional to v²/c² and
/// GM/(rc²) to the Newtonian equations of motion. These model:
///   • Perihelion precession (1PN)
///   • Gravitational radiation reaction (2.5PN)
///   • Spin-orbit coupling (1.5PN)
///
/// Future implementations should compute correction accelerations that
/// are added to the Newtonian accelerations after the backend returns.
/// </summary>
public interface IRelativisticCorrection
{
    /// <summary>
    /// Compute relativistic correction to acceleration for body i
    /// given the full system state.
    /// </summary>
    /// <param name="posX">X positions of all bodies.</param>
    /// <param name="posY">Y positions of all bodies.</param>
    /// <param name="posZ">Z positions of all bodies.</param>
    /// <param name="velX">X velocities of all bodies.</param>
    /// <param name="velY">Y velocities of all bodies.</param>
    /// <param name="velZ">Z velocities of all bodies.</param>
    /// <param name="mass">Masses of all bodies.</param>
    /// <param name="bodyIndex">Index of the body to compute correction for.</param>
    /// <param name="count">Total number of bodies.</param>
    /// <returns>Correction acceleration (dax, day, daz).</returns>
    (double dax, double day, double daz) ComputeCorrection(
        double[] posX, double[] posY, double[] posZ,
        double[] velX, double[] velY, double[] velZ,
        double[] mass, int bodyIndex, int count);
}
