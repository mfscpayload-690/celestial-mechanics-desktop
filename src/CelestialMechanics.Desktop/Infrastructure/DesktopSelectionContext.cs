using CelestialMechanics.Simulation;

namespace CelestialMechanics.Desktop.Infrastructure;

public sealed class DesktopSelectionContext : ISelectionContext
{
    public int SelectedBodyId { get; set; } = -1;
    public bool HasSelection => SelectedBodyId >= 0;
}
