using System.Text.Json;

namespace CelestialMechanics.App;

internal sealed class RuntimeDiagnosticsLogger : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly object _sync = new();
    private DateTime _nextWriteUtc;

    public bool Enabled { get; }
    public string OutputPath { get; }

    public RuntimeDiagnosticsLogger(bool enabled, string? outputPath = null, TimeSpan? interval = null)
    {
        Enabled = enabled;
        OutputPath = outputPath ?? Path.Combine("test-results", "module1", "runtime-diagnostics.ndjson");
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _nextWriteUtc = DateTime.MinValue;
    }

    public void TryWrite(RuntimeDiagnosticsSnapshot snapshot)
    {
        if (!Enabled)
            return;

        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _nextWriteUtc)
            return;

        _nextWriteUtc = nowUtc + _interval;

        string line = JsonSerializer.Serialize(snapshot);
        lock (_sync)
        {
            string? dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(OutputPath, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
    }
}

internal sealed record RuntimeDiagnosticsSnapshot(
    DateTime TimestampUtc,
    double FrameDeltaTime,
    double ScaledDeltaTime,
    double PhysicsMs,
    double RenderMs,
    string EngineState,
    string SolverBackend,
    string Integrator,
    double SimTime,
    double SolverDt,
    int BodyCount,
    int ActiveBodyCount,
    int CollisionCount,
    int CollisionBursts,
    bool UseSoAPath,
    bool UseBarnesHut,
    bool DeterministicMode,
    bool UseParallel,
    bool UseSimd,
    bool EnableAdaptiveTimestep,
    bool EnableCollisions,
    float TimeFlowSlider,
    double TimeScaleMultiplier,
    bool ShowSimulationControlsPanel,
    bool ShowEnergyMonitorPanel,
    bool ShowPerformancePanel,
    bool ShowIntegratorPanel,
    bool ShowAddBodyPanel,
    bool ShowBodyInspectorPanel,
    bool ShowGrid,
    bool ShowOrbitalTrails,
    bool ShowBackground,
    bool ShowAccretionDisks,
    bool EnableAlbedoTextureMaps,
    bool EnableStarDrivenLighting,
    bool EnableRayTracedShadows,
    float GlobalLuminosityScale,
    float GlobalGlowScale,
    float GlobalSaturation
);
