using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Modes;

/// <summary>
/// Observation mode: simulation is paused; the user can freely explore/select objects.
///
/// This mode is intentionally a stub for Phase 10. Future phases will add:
///   • Camera orbit / navigation
///   • Entity info panel binding
///   • Time-bar scrubbing via SnapshotManager
/// </summary>
public sealed class ObservationMode : IAppMode
{
    public string ModeName => "Observation";

    public SimulationManager? Simulation  { get; }
    public SelectionManager   Selection   { get; }

    public ObservationMode(SimulationManager? simulation = null,
                           SelectionManager?  selection  = null)
    {
        Simulation = simulation;
        Selection  = selection ?? new SelectionManager();
    }

    public void Initialize()
    {
        // Simulation is kept paused in this mode — no-op here because
        // ModeManager is responsible for the simulation start/stop lifecycle.
    }

    /// <summary>
    /// No physics advance. The manager simply holds its current state while
    /// the user observes. Future: animate camera, update UI time display.
    /// </summary>
    public void Update(double deltaTime) { }

    /// <summary>Render hook — no-op at AppCore level.</summary>
    public void Render() { }

    public void Dispose() { }
}
