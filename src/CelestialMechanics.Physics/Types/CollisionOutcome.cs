namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Outcome produced by collision resolution.
/// </summary>
public enum CollisionOutcome
{
    Bounce,
    Merge,
    Fragmentation,
    Accretion
}
