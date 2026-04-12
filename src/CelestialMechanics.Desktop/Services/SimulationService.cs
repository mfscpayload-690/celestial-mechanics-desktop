using System.Windows;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// Thread-safe wrapper around SimulationEngine.
/// All access to the engine is serialized through _engineLock.
/// </summary>
public class SimulationService : IDisposable
{
    private readonly SimulationEngine _engine;
    private readonly SimulationClock _clock;
    private readonly object _engineLock = new();
    private Thread? _simThread;
    private volatile bool _running;
    private long _timeScaleBits = BitConverter.DoubleToInt64Bits(1.0);
    private long _stepCount;

    /// <summary>
    /// Time scale multiplier for the simulation (0.1x to 10x).
    /// Updated from the UI thread, read from the sim thread.
    /// </summary>
    public double TimeScale
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _timeScaleBits));
        set => Interlocked.Exchange(ref _timeScaleBits,
            BitConverter.DoubleToInt64Bits(System.Math.Clamp(value, 0.1, 10.0)));
    }

    // Snapshot of state for UI reads (updated from sim thread)
    private volatile EngineState _lastState = EngineState.Stopped;
    private long _lastSimTimeBits;
    private long _lastPhysicsTimeMsBits;
    private SimulationState? _lastSimState;

    public EngineState LastState => _lastState;
    public double LastSimTime => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastSimTimeBits));
    public double LastPhysicsTimeMs => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastPhysicsTimeMsBits));
    public SimulationState? LastSimState => _lastSimState;
    public long StepCount => Interlocked.Read(ref _stepCount);

    /// <summary>
    /// Raised on the simulation thread after each physics update.
    /// Subscribers must marshal to the UI thread.
    /// </summary>
    public event Action<SimulationState>? StateUpdated;

    /// <summary>
    /// Raised on the UI thread (~30Hz) with an immutable snapshot.
    /// Safe to consume directly from WPF controls.
    /// </summary>
    public event EventHandler<SimulationSnapshotEventArgs>? SnapshotUpdated;

    /// <summary>
    /// Raised on the UI thread when a log-worthy event occurs.
    /// </summary>
    public event EventHandler<SimEventLogEntry>? EventLogged;

    public SimulationService()
    {
        _engine = new SimulationEngine();
        _clock = new SimulationClock();
    }

    public void StartSimThread()
    {
        if (_simThread != null) return;
        _running = true;
        _clock.Start();

        _simThread = new Thread(SimLoop)
        {
            IsBackground = true,
            Name = "Simulation Thread",
        };
        _simThread.Start();
    }

    public void StopSimThread()
    {
        _running = false;
        _simThread?.Join(timeout: TimeSpan.FromMilliseconds(500));
        _simThread = null;
    }

    private void SimLoop()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double lastTime = sw.Elapsed.TotalSeconds;
        double uiUpdateAccumulator = 0;

        while (_running)
        {
            double now = sw.Elapsed.TotalSeconds;
            double dt = now - lastTime;
            lastTime = now;

            var physicsSw = System.Diagnostics.Stopwatch.StartNew();
            lock (_engineLock)
            {
                _engine.Update(dt * TimeScale);
                _lastState = _engine.State;
                Interlocked.Exchange(ref _lastSimTimeBits, BitConverter.DoubleToInt64Bits(_engine.CurrentTime));
                Interlocked.Increment(ref _stepCount);
                _lastSimState = _engine.CurrentState;
            }
            physicsSw.Stop();
            double physMs = physicsSw.Elapsed.TotalMilliseconds;
            Interlocked.Exchange(ref _lastPhysicsTimeMsBits, BitConverter.DoubleToInt64Bits(physMs));

            // Throttle UI updates to ~30 Hz
            uiUpdateAccumulator += dt;
            if (uiUpdateAccumulator >= 1.0 / 30.0)
            {
                uiUpdateAccumulator = 0;
                StateUpdated?.Invoke(_engine.CurrentState!);

                // Marshal immutable snapshot to UI thread
                var snapshot = new SimulationSnapshotEventArgs
                {
                    SimTime = LastSimTime,
                    StepCount = StepCount,
                    TotalEnergy = _engine.CurrentState?.TotalEnergy ?? 0,
                    KineticEnergy = _engine.CurrentState?.KineticEnergy ?? 0,
                    PotentialEnergy = _engine.CurrentState?.PotentialEnergy ?? 0,
                    BodyCount = _engine.Bodies?.Length ?? 0,
                    PhysicsTimeMs = physMs,
                };
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    SnapshotUpdated?.Invoke(this, snapshot);
                });
            }

            Thread.Sleep(1);
        }
    }

    // ── Thread-safe commands ────────────────────────────────────────

    public void Play()
    {
        lock (_engineLock) _engine.Start();
    }

    public void Pause()
    {
        lock (_engineLock) _engine.Pause();
    }

    public void Step()
    {
        lock (_engineLock) _engine.StepOnce();
    }

    /// <summary>
    /// Stops the simulation and clears all bodies.
    /// Results in an empty universe — no auto-spawned objects.
    /// </summary>
    public void ResetScene()
    {
        lock (_engineLock)
        {
            _engine.Stop();
            _engine.SetBodies(Array.Empty<PhysicsBody>());
            Interlocked.Exchange(ref _stepCount, 0);
        }
        LogEvent(SimEventType.Info, "Simulation reset — all bodies cleared.");
    }

    /// <summary>
    /// Logs an event and fires EventLogged on the UI thread.
    /// </summary>
    public void LogEvent(SimEventType type, string message)
    {
        var entry = SimEventLogEntry.Create(type, message);
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            EventLogged?.Invoke(this, entry);
        });
    }

    public void SetIntegrator(string name)
    {
        lock (_engineLock) _engine.SetIntegrator(name);
    }

    public string GetIntegratorName()
    {
        lock (_engineLock) return _engine.GetIntegratorName();
    }

    public void AddBody(PhysicsBody body)
    {
        lock (_engineLock) _engine.AddBody(body);
    }

    public void RemoveBody(int id)
    {
        lock (_engineLock) _engine.RemoveBody(id);
    }

    public void LoadBodies(PhysicsBody[] bodies)
    {
        lock (_engineLock) _engine.SetBodies(bodies);
    }

    public PhysicsBody[] GetBodies()
    {
        lock (_engineLock) return _engine.Bodies.ToArray();
    }

    public void ApplyConfig(Action<PhysicsConfig> mutate)
    {
        lock (_engineLock)
        {
            mutate(_engine.Config);
            _engine.ApplyConfig();
        }
    }

    /// <summary>
    /// Executes an action under the engine lock. Used by the render thread
    /// to safely read simulation state.
    /// </summary>
    public bool WithEngineLock(Action<SimulationEngine> action)
    {
        lock (_engineLock)
        {
            action(_engine);
            return true;
        }
    }

    /// <summary>
    /// Offsets a body's position by the given delta (for Edit mode dragging).
    /// Uses body index for efficiency.
    /// </summary>
    public void OffsetBodyPosition(int bodyIndex, float dx, float dy, float dz)
    {
        lock (_engineLock)
        {
            var bodies = _engine.Bodies;
            if (bodies == null || bodyIndex < 0 || bodyIndex >= bodies.Length) return;

            ref var body = ref bodies[bodyIndex];
            body.Position = new Vec3d(body.Position.X + dx, body.Position.Y + dy, body.Position.Z + dz);
        }
    }

    public void Dispose()
    {
        StopSimThread();
    }
}
