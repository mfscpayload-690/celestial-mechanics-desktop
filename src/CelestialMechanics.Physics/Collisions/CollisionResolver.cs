using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Resolves detected collisions by applying the merge policy.
///
/// Resolution order:
///   1. Receive sorted collision events (deepest overlap first)
///   2. For each event, check both bodies are still active (may have
///      been merged by a prior event in the same step)
///   3. Apply MergePolicy.Merge on SoA arrays
///   4. Sync changes to AoS PhysicsBody array
///
/// Thread safety: collision resolution is always single-threaded.
/// Structural mutations (marking bodies inactive) never happen during
/// the parallel force computation.
/// </summary>
public sealed class CollisionResolver
{
    private int _collisionsThisStep;

    /// <summary>Number of merges that occurred in the last resolution call.</summary>
    public int LastCollisionCount => _collisionsThisStep;

    /// <summary>
    /// Resolve all collisions by merging bodies according to MergePolicy.
    /// </summary>
    /// <param name="events">Collision events (sorted deepest-first).</param>
    /// <param name="soa">SoA body buffer.</param>
    /// <param name="bodies">AoS body array to keep in sync.</param>
    public void Resolve(List<CollisionEvent> events, BodySoA soa, PhysicsBody[] bodies)
    {
        _collisionsThisStep = 0;

        foreach (var evt in events)
        {
            int a = evt.BodyIndexA;
            int b = evt.BodyIndexB;

            // Skip if either body was already merged in this step
            if (!soa.IsActive[a] || !soa.IsActive[b])
                continue;

            MergePolicy.Merge(soa, a, b);
            MergePolicy.SyncToAoS(bodies, soa, a, b);
            _collisionsThisStep++;
        }
    }
}
