namespace CelestialMechanics.Simulation;

public interface ISelectionContext
{
    int SelectedBodyId { get; set; }
    bool HasSelection { get; }
}