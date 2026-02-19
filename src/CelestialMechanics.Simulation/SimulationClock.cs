using System.Diagnostics;

namespace CelestialMechanics.Simulation;

public class SimulationClock
{
    private readonly Stopwatch _stopwatch = new();
    private long _lastTicks;
    private double _timeScale = 1.0;

    /// <summary>
    /// Total elapsed seconds since the clock was started (not affected by time scale).
    /// </summary>
    public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

    /// <summary>
    /// Time in seconds since the last call to Tick(), scaled by the current time scale.
    /// </summary>
    public double DeltaTime { get; private set; }

    /// <summary>
    /// Starts the clock.
    /// </summary>
    public void Start()
    {
        _stopwatch.Start();
        _lastTicks = _stopwatch.ElapsedTicks;
    }

    /// <summary>
    /// Updates DeltaTime to the time elapsed since the previous Tick() call,
    /// multiplied by the current time scale.
    /// </summary>
    public void Tick()
    {
        long currentTicks = _stopwatch.ElapsedTicks;
        long deltaTicks = currentTicks - _lastTicks;
        _lastTicks = currentTicks;
        DeltaTime = (double)deltaTicks / Stopwatch.Frequency * _timeScale;
    }

    /// <summary>
    /// Resets the clock to its initial state.
    /// </summary>
    public void Reset()
    {
        _stopwatch.Reset();
        _lastTicks = 0;
        DeltaTime = 0;
    }

    /// <summary>
    /// Sets the time scale multiplier applied to DeltaTime.
    /// </summary>
    /// <param name="scale">The time scale multiplier (e.g. 2.0 for double speed).</param>
    public void SetTimeScale(double scale)
    {
        _timeScale = scale;
    }
}
