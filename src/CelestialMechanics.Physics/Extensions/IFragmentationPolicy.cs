namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Extension point for fragmentation policy.
///
/// When two bodies collide, the system currently always merges them.
/// A fragmentation policy allows the collision to instead produce
/// debris fragments, modeling tidal disruption, impact fragmentation,
/// or partial mass stripping.
///
/// Future implementations return a list of fragments with their
/// masses, positions, and velocities.
/// </summary>
public interface IFragmentationPolicy
{
    /// <summary>
    /// Determine whether a collision should fragment instead of merge.
    /// </summary>
    /// <param name="massA">Mass of body A.</param>
    /// <param name="massB">Mass of body B.</param>
    /// <param name="relativeVelocity">Relative velocity magnitude at impact.</param>
    /// <param name="overlapDepth">Overlap depth of the collision.</param>
    /// <returns>True if the collision should fragment; false to merge.</returns>
    bool ShouldFragment(double massA, double massB, double relativeVelocity, double overlapDepth);
}
