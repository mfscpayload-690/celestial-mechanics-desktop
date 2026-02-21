namespace CelestialMechanics.AppCore.Modes;

/// <summary>
/// Manages the active application mode and handles clean transitions between modes.
///
/// Transition sequence:
///   1. <c>currentMode.Dispose()</c>
///   2. Set <c>CurrentMode = newMode</c>
///   3. <c>newMode.Initialize()</c>
///   4. Fire <see cref="OnModeChanged"/>
///
/// Update and Render forward to the current mode.
/// Thread-safe via lock on <see cref="_modeLock"/>.
/// </summary>
public sealed class ModeManager : IDisposable
{
    private readonly object _modeLock = new();
    private IAppMode? _currentMode;
    private bool _disposed;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after a mode transition completes.
    /// Arguments: (previousMode, newMode). previousMode may be null for the first activation.
    /// </summary>
    public event Action<IAppMode?, IAppMode>? OnModeChanged;

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>The currently active mode. Null until the first call to <see cref="SetMode"/>.</summary>
    public IAppMode? CurrentMode
    {
        get { lock (_modeLock) { return _currentMode; } }
    }

    public string CurrentModeName
    {
        get { lock (_modeLock) { return _currentMode?.ModeName ?? "None"; } }
    }

    // ── Mode switching ────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions to <paramref name="newMode"/>, disposing the previous one.
    /// </summary>
    public void SetMode(IAppMode newMode)
    {
        ArgumentNullException.ThrowIfNull(newMode);

        IAppMode? previous;
        lock (_modeLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModeManager));

            previous = _currentMode;
            previous?.Dispose();
            _currentMode = newMode;
            newMode.Initialize();
        }

        OnModeChanged?.Invoke(previous, newMode);
    }

    /// <summary>
    /// Convenience: switch to <see cref="SimulationMode"/> wrapping <paramref name="simMode"/>.
    /// </summary>
    public void SwitchToSimulation(SimulationMode simMode)
        => SetMode(simMode);

    /// <summary>
    /// Convenience: switch to <see cref="ObservationMode"/> wrapping <paramref name="obsMode"/>.
    /// </summary>
    public void SwitchToObservation(ObservationMode obsMode)
        => SetMode(obsMode);

    // ── Update / Render ───────────────────────────────────────────────────────

    /// <summary>Forwards to <see cref="IAppMode.Update"/> of the current mode.</summary>
    public void Update(double deltaTime)
    {
        IAppMode? mode;
        lock (_modeLock) { mode = _currentMode; }
        mode?.Update(deltaTime);
    }

    /// <summary>Forwards to <see cref="IAppMode.Render"/> of the current mode.</summary>
    public void Render()
    {
        IAppMode? mode;
        lock (_modeLock) { mode = _currentMode; }
        mode?.Render();
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        IAppMode? mode;
        lock (_modeLock)
        {
            if (_disposed) return;
            mode     = _currentMode;
            _currentMode = null;
            _disposed    = true;
        }
        mode?.Dispose();
    }
}
