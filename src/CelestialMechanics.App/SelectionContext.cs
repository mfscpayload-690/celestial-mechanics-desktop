using CelestialMechanics.Simulation;

namespace CelestialMechanics.App;

public class SelectionContext : ISelectionContext
{
    public int SelectedBodyId { get; set; } = -1;

    public bool HasSelection => SelectedBodyId >= 0;
}