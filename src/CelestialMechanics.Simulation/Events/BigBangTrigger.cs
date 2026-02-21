using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Evaluates whether a Singularity entity should trigger cosmological expansion.
/// Condition: entity has ExpansionComponent with IsSingularity=true and has not yet expanded.
/// </summary>
public sealed class BigBangTrigger : ITriggerCondition
{
    public bool Evaluate(Entity entity)
    {
        var expansion = entity.GetComponent<ExpansionComponent>();
        if (expansion == null)
            return false;

        return expansion.IsSingularity && !expansion.HasExpanded;
    }
}
