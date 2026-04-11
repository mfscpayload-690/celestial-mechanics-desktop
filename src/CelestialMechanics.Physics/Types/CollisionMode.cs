namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Collision response strategy used when <see cref="PhysicsConfig.EnableCollisions"/> is enabled.
/// </summary>
public enum CollisionMode
{
    /// <summary>
    /// Legacy behavior: always merge overlapping bodies.
    /// </summary>
    MergeOnly,

    /// <summary>
    /// Resolve contacts using impulse dynamics without merging.
    /// </summary>
    BounceOnly,

    /// <summary>
    /// Choose between bounce, merge/accretion, and fragmentation by impact conditions.
    /// </summary>
    Realistic
}
