namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Stub interface for broad-phase collision culling.
/// Future implementations: octree-based spatial partitioning.
/// </summary>
public interface IBroadPhase
{
    /// <summary>
    /// Return candidate collision pairs that need narrow-phase testing.
    /// </summary>
    /// <param name="posX">X positions.</param>
    /// <param name="posY">Y positions.</param>
    /// <param name="posZ">Z positions.</param>
    /// <param name="radius">Body radii.</param>
    /// <param name="isActive">Active flags.</param>
    /// <param name="count">Number of bodies.</param>
    /// <returns>List of (i, j) index pairs to test.</returns>
    List<(int, int)> GetCandidatePairs(
        double[] posX, double[] posY, double[] posZ,
        double[] radius, bool[] isActive, int count);
}
