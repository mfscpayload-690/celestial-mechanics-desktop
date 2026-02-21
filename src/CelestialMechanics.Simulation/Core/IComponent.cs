namespace CelestialMechanics.Simulation.Core;

/// <summary>
/// Base interface for all entity components in Phase 8's ECS architecture.
/// Implementations must be reference types (classes) to avoid boxing.
/// </summary>
public interface IComponent
{
    void Update(double dt);
}
