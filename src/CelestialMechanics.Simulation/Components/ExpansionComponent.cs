using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Components;

/// <summary>
/// Component for entities participating in cosmological expansion.
/// Used by Singularity entities. Tracks Hubble flow velocity:
///   v = H0 * d   (where d = distance from origin)
///
/// Position scaling is handled by SpaceMetricManager globally;
/// this component tracks per-entity expansion state.
/// </summary>
public sealed class ExpansionComponent : IComponent
{
    /// <summary>Whether this entity represents a cosmological singularity.</summary>
    public bool IsSingularity { get; set; }

    /// <summary>Set to true once the Big Bang expansion has begun.</summary>
    public bool HasExpanded { get; set; }

    /// <summary>
    /// Hubble parameter for this entity's local expansion (1/time).
    /// Normally synchronised from SpaceMetricManager.
    /// </summary>
    public double HubbleParameter { get; set; } = 0.001;

    /// <summary>
    /// Apply Hubble flow: v = H0 * d. This adds an expansion velocity
    /// to the entity based on its distance from the origin.
    /// Only active after expansion has started.
    /// </summary>
    public void Update(double dt)
    {
        // Expansion velocity is applied by SpaceMetricManager at the system level.
        // Per-entity update is a no-op; state tracking only.
    }
}
