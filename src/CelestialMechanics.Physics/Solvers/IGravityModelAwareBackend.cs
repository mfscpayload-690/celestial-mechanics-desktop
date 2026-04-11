namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Optional backend capability for configuring gravity model features.
/// </summary>
public interface IGravityModelAwareBackend
{
    bool EnableShellTheorem { get; set; }
}
