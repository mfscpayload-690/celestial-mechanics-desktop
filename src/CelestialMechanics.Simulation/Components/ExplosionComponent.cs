using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Components;

/// <summary>
/// Marker and state component for entities undergoing or resulting from an explosion.
/// Tracks explosion timing and debris state.
/// </summary>
public sealed class ExplosionComponent : IComponent
{
    /// <summary>Whether this entity is currently exploding.</summary>
    public bool IsExploding { get; set; }

    /// <summary>Time since the explosion began.</summary>
    public double TimeSinceExplosion { get; set; }

    /// <summary>Whether this entity is debris from an explosion.</summary>
    public bool IsDebris { get; set; }

    /// <summary>Maximum lifetime for debris particles before deactivation.</summary>
    public double DebrisLifetime { get; set; } = 100.0;

    public void Update(double dt)
    {
        if (IsExploding || IsDebris)
        {
            TimeSinceExplosion += dt;
        }
    }
}
