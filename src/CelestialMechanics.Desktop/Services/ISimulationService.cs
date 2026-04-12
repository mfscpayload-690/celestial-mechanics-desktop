using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// Abstraction over SimulationService for DI registration.
/// Implementations manage the simulation engine lifecycle.
/// </summary>
public interface ISimulationService
{
    /// <summary>Current engine state (Running, Paused, Stopped).</summary>
    string State { get; }

    /// <summary>Current simulation time in seconds.</summary>
    double CurrentTime { get; }

    /// <summary>Time scale multiplier (0.1x to 10x).</summary>
    double TimeScale { get; set; }

    /// <summary>Current step count.</summary>
    long StepCount { get; }

    /// <summary>Raised after each physics step with an immutable snapshot — safe to pass across threads.</summary>
    event Action? StateChanged;

    /// <summary>Raised on UI thread with a snapshot after each physics update (~30Hz).</summary>
    event EventHandler<SimulationSnapshotEventArgs>? SnapshotUpdated;

    /// <summary>Raised when a log-worthy event occurs (body collision, warning, etc.).</summary>
    event EventHandler<SimEventLogEntry>? EventLogged;

    void Play();
    void Pause();
    void Step();
    void Reset();
    void StartSimThread();
    void StopSimThread();
}

/// <summary>
/// Immutable snapshot of the simulation state, safe to pass across threads.
/// </summary>
public sealed class SimulationSnapshotEventArgs : EventArgs
{
    public double SimTime { get; init; }
    public long StepCount { get; init; }
    public double TotalEnergy { get; init; }
    public double KineticEnergy { get; init; }
    public double PotentialEnergy { get; init; }
    public int BodyCount { get; init; }
    public double PhysicsTimeMs { get; init; }
}
