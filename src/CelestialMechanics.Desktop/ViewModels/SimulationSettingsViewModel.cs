using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Simulation Settings panel.
/// Exposes all PhysicsConfig parameters for viewing/editing.
/// </summary>
public sealed partial class SimulationSettingsViewModel : ObservableObject
{
    private readonly SimulationService _simService;

    public event Action? CloseRequested;

    // ── Integrator ─────────────────────────────────────────────────
    [ObservableProperty]
    private string _integratorName = "Verlet";

    public IReadOnlyList<string> IntegratorNames { get; } =
        new[] { "Euler", "Verlet", "RK4" }.ToList().AsReadOnly();

    // RadioButton-friendly integrator selection
    public bool IsVerlet
    {
        get => IntegratorName == "Verlet";
        set { if (value) IntegratorName = "Verlet"; }
    }
    public bool IsEuler
    {
        get => IntegratorName == "Euler";
        set { if (value) IntegratorName = "Euler"; }
    }
    public bool IsRK4
    {
        get => IntegratorName == "RK4";
        set { if (value) IntegratorName = "RK4"; }
    }

    partial void OnIntegratorNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsVerlet));
        OnPropertyChanged(nameof(IsEuler));
        OnPropertyChanged(nameof(IsRK4));
    }

    // ── Performance ────────────────────────────────────────────────
    [ObservableProperty]
    private bool _deterministicMode = true;

    [ObservableProperty]
    private bool _useParallelComputation;

    [ObservableProperty]
    private bool _useSimd;

    [ObservableProperty]
    private bool _useSoAPath = true;

    // ── Algorithm ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThetaEnabled))]
    private bool _useBarnesHut;

    [ObservableProperty]
    private double _theta = 0.5;

    public bool IsThetaEnabled => UseBarnesHut;

    // ── Physics ────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _enableCollisions = true;

    [ObservableProperty]
    private bool _useAdaptiveTimestep;

    // ── Timestep ───────────────────────────────────────────────────
    [ObservableProperty]
    private double _timeStep = 0.001;

    [ObservableProperty]
    private double _minDt = 1e-6;

    [ObservableProperty]
    private double _maxDt = 0.01;

    // ── Relativistic ───────────────────────────────────────────────
    [ObservableProperty]
    private bool _enablePostNewtonian;

    [ObservableProperty]
    private bool _enableGravitationalLensing;

    [ObservableProperty]
    private bool _enableAccretionDisks;

    [ObservableProperty]
    private bool _enableGravitationalWaves;

    [ObservableProperty]
    private bool _enableJetEmission;

    // ── Softening ──────────────────────────────────────────────────
    [ObservableProperty]
    private double _softeningEpsilon = 1e-4;

    [ObservableProperty]
    private SofteningMode _softeningMode = SofteningMode.Constant;

    public IReadOnlyList<SofteningMode> SofteningModes { get; } =
        Enum.GetValues<SofteningMode>().ToList().AsReadOnly();

    public SimulationSettingsViewModel(SimulationService simService)
    {
        _simService = simService;
    }

    /// <summary>
    /// Reads the current engine configuration and populates all properties.
    /// </summary>
    public void LoadFromEngine()
    {
        _simService.WithEngineLock(engine =>
        {
            var c = engine.Config;
            IntegratorName = c.IntegratorName;
            DeterministicMode = c.DeterministicMode;
            UseParallelComputation = c.UseParallelComputation;
            UseSimd = c.UseSimd;
            UseSoAPath = c.UseSoAPath;
            UseBarnesHut = c.UseBarnesHut;
            Theta = c.Theta;
            EnableCollisions = c.EnableCollisions;
            UseAdaptiveTimestep = c.UseAdaptiveTimestep;
            TimeStep = c.TimeStep;
            MinDt = c.MinDt;
            MaxDt = c.MaxDt;
            EnablePostNewtonian = c.EnablePostNewtonian;
            EnableGravitationalLensing = c.EnableGravitationalLensing;
            EnableAccretionDisks = c.EnableAccretionDisks;
            EnableGravitationalWaves = c.EnableGravitationalWaves;
            EnableJetEmission = c.EnableJetEmission;
            SofteningEpsilon = c.SofteningEpsilon;
            SofteningMode = c.SofteningMode;
        });
    }

    [RelayCommand]
    private void Apply()
    {
        _simService.ApplyConfig(config =>
        {
            config.IntegratorName = IntegratorName;
            config.DeterministicMode = DeterministicMode;
            config.UseParallelComputation = UseParallelComputation;
            config.UseSimd = UseSimd;
            config.UseSoAPath = UseSoAPath;
            config.UseBarnesHut = UseBarnesHut;
            config.Theta = Theta;
            config.EnableCollisions = EnableCollisions;
            config.UseAdaptiveTimestep = UseAdaptiveTimestep;
            config.TimeStep = TimeStep;
            config.MinDt = MinDt;
            config.MaxDt = MaxDt;
            config.EnablePostNewtonian = EnablePostNewtonian;
            config.EnableGravitationalLensing = EnableGravitationalLensing;
            config.EnableAccretionDisks = EnableAccretionDisks;
            config.EnableGravitationalWaves = EnableGravitationalWaves;
            config.EnableJetEmission = EnableJetEmission;
            config.SofteningEpsilon = SofteningEpsilon;
            config.SofteningMode = SofteningMode;
        });
        _simService.SetIntegrator(IntegratorName);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }
}
