using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.AppCore.Snapshot;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Modes;

/// <summary>
/// Active simulation mode: advances the physics simulation each frame,
/// optionally capturing time-bar snapshots.
///
/// Wraps:
///   <see cref="SimulationManager"/> — ECS + physics step
///   <see cref="SceneGraph"/>       — organisational hierarchy
///   <see cref="SnapshotManager"/>  — time-bar capture
///   <see cref="SelectionManager"/> — selection state
/// </summary>
public sealed class SimulationMode : IAppMode
{
    public string ModeName => "Simulation";

    // ── Dependencies ──────────────────────────────────────────────────────────

    public SimulationManager   Simulation      { get; }
    public SceneGraph          SceneGraph      { get; }
    public SnapshotManager     Snapshots       { get; }
    public SelectionManager    Selection       { get; }

    // ── Snapshot policy ───────────────────────────────────────────────────────

    /// <summary>
    /// Automatically capture a snapshot every <c>N</c> simulation steps.
    /// Set to 0 to disable automatic snapshots.
    /// Default: 60 (≈ 1 second at 60 Hz).
    /// </summary>
    public int SnapshotIntervalSteps { get; set; } = 60;

    private int _stepsSinceLastSnapshot;

    // ── Construction ──────────────────────────────────────────────────────────

    public SimulationMode(
        SimulationManager simulation,
        SceneGraph?       sceneGraph  = null,
        SnapshotManager?  snapshots   = null,
        SelectionManager? selection   = null)
    {
        Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        SceneGraph = sceneGraph  ?? new SceneGraph();
        Snapshots  = snapshots   ?? new SnapshotManager();
        Selection  = selection   ?? new SelectionManager();
    }

    // ── IAppMode ──────────────────────────────────────────────────────────────

    public void Initialize()
    {
        _stepsSinceLastSnapshot = 0;
    }

    /// <summary>
    /// Advances the simulation by <paramref name="deltaTime"/> (wall-clock seconds)
    /// and optionally captures a snapshot every <see cref="SnapshotIntervalSteps"/> steps.
    /// </summary>
    public void Update(double deltaTime)
    {
        if (deltaTime <= 0.0) return;

        Simulation.Step(deltaTime);

        _stepsSinceLastSnapshot++;
        if (SnapshotIntervalSteps > 0 && _stepsSinceLastSnapshot >= SnapshotIntervalSteps)
        {
            Snapshots.CaptureSnapshot(Simulation);
            _stepsSinceLastSnapshot = 0;
        }
    }

    /// <summary>
    /// AppCore render hook — no-op. The renderer layer (Silk.NET / future Vulkan)
    /// reads entity positions directly from the SimulationManager.
    /// </summary>
    public void Render() { }

    public void Dispose() { }
}
