using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Evaluates whether a star entity should undergo supernova.
/// Condition: StellarEvolutionComponent.CoreMass >= CollapseThreshold (Chandrasekhar limit ≈ 1.44 M☉).
/// This trigger only signals — it does not perform the explosion.
/// </summary>
public sealed class SupernovaTrigger : ITriggerCondition
{
    public bool Evaluate(Entity entity)
    {
        var stellar = entity.GetComponent<StellarEvolutionComponent>();
        if (stellar == null || stellar.HasCollapsed)
            return false;

        return stellar.CoreMass >= stellar.CollapseThreshold;
    }
}
