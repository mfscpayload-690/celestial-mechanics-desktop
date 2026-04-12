using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Body Inspector panel.
/// Displays and allows editing of the currently selected body's properties.
/// </summary>
public sealed partial class BodyInspectorViewModel : ObservableObject
{
    private readonly SimulationService _simService;
    private readonly SceneService _sceneService;
    private int? _currentBodyId;

    [ObservableProperty]
    private bool _hasSelection;

    // ── Identity ───────────────────────────────────────────────────
    [ObservableProperty]
    private string _bodyName = "";

    [ObservableProperty]
    private BodyType _bodyType;

    [ObservableProperty]
    private int _bodyId;

    // ── Transform ──────────────────────────────────────────────────
    [ObservableProperty]
    private double _positionX;

    [ObservableProperty]
    private double _positionY;

    [ObservableProperty]
    private double _positionZ;

    [ObservableProperty]
    private double _velocityX;

    [ObservableProperty]
    private double _velocityY;

    [ObservableProperty]
    private double _velocityZ;

    // ── Physical ───────────────────────────────────────────────────
    [ObservableProperty]
    private double _mass;

    [ObservableProperty]
    private double _radius;

    [ObservableProperty]
    private double _density;

    // ── Simulation ─────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isCollidable;

    // ── Derived Physical ────────────────────────────────────────────
    /// <summary>True when selected body type is BlackHole.</summary>
    public bool IsBlackHole => BodyType == Physics.Types.BodyType.BlackHole;

    /// <summary>Schwarzschild radius: 2GM/c². Only meaningful for black holes.</summary>
    public double SchwarzschildRadius => Mass > 0
        ? 2.0 * CelestialMechanics.Math.PhysicalConstants.G_Sim * Mass / (3e8 * 3e8)
        : 0;

    // ── Orbital Elements (Analysis Mode) ───────────────────────────
    [ObservableProperty]
    private bool _showOrbitalElements;

    [ObservableProperty]
    private string _orbitType = "";

    [ObservableProperty]
    private double _semiMajorAxis;

    [ObservableProperty]
    private double _eccentricity;

    [ObservableProperty]
    private double _inclination;

    [ObservableProperty]
    private double _longitudeOfAscendingNode;

    [ObservableProperty]
    private double _argumentOfPeriapsis;

    [ObservableProperty]
    private double _trueAnomaly;

    [ObservableProperty]
    private double _orbitalPeriod;

    [ObservableProperty]
    private double _periapsisDistance;

    [ObservableProperty]
    private double _apoapsisDistance;

    [ObservableProperty]
    private double _specificOrbitalEnergy;

    [ObservableProperty]
    private string _centralBodyName = "";

    /// <summary>All BodyType values for the type ComboBox.</summary>
    public IReadOnlyList<BodyType> BodyTypes { get; } =
        Enum.GetValues<BodyType>().ToList().AsReadOnly();

    public BodyInspectorViewModel(SimulationService simService, SceneService sceneService)
    {
        _simService = simService;
        _sceneService = sceneService;
    }

    /// <summary>
    /// Loads a body's properties into the inspector from the simulation engine.
    /// </summary>
    public void LoadBody(Guid nodeId)
    {
        var bodyId = _sceneService.GetBodyIdForNode(nodeId);
        if (bodyId == null)
        {
            ClearSelection();
            return;
        }

        _currentBodyId = bodyId.Value;

        _simService.WithEngineLock(engine =>
        {
            if (engine.Bodies == null) return;
            
            Physics.Types.PhysicsBody? selectedBody = null;
            Physics.Types.PhysicsBody? centralBody = null;
            double maxMass = 0;

            // Find selected body and most massive body (central body for orbital elements)
            foreach (var body in engine.Bodies)
            {
                if (body.Id == bodyId.Value)
                    selectedBody = body;
                if (body.Mass > maxMass && body.Id != bodyId.Value)
                {
                    maxMass = body.Mass;
                    centralBody = body;
                }
            }

            if (selectedBody == null) return;
            var selectedBodyValue = selectedBody.Value;

            BodyId = selectedBodyValue.Id;
            BodyName = $"{selectedBodyValue.Type} {selectedBodyValue.Id}";
            BodyType = selectedBodyValue.Type;
            PositionX = selectedBodyValue.Position.X;
            PositionY = selectedBodyValue.Position.Y;
            PositionZ = selectedBodyValue.Position.Z;
            VelocityX = selectedBodyValue.Velocity.X;
            VelocityY = selectedBodyValue.Velocity.Y;
            VelocityZ = selectedBodyValue.Velocity.Z;
            Mass = selectedBodyValue.Mass;
            Radius = selectedBodyValue.Radius;
            Density = selectedBodyValue.Density;
            IsActive = selectedBodyValue.IsActive;
            IsCollidable = selectedBodyValue.IsCollidable;

            // Compute orbital elements if there's a central body
            if (centralBody.HasValue && centralBody.Value.Mass > selectedBodyValue.Mass)
            {
                var cb = centralBody.Value;
                var relPos = selectedBodyValue.Position - cb.Position;
                var relVel = selectedBodyValue.Velocity - cb.Velocity;
                double mu = CelestialMechanics.Math.PhysicalConstants.G_Sim * cb.Mass;

                var elements = OrbitalElements.FromStateVectors(relPos, relVel, mu);
                if (elements.IsValid)
                {
                    OrbitType = elements.OrbitType;
                    SemiMajorAxis = elements.SemiMajorAxis;
                    Eccentricity = elements.Eccentricity;
                    Inclination = elements.Inclination;
                    LongitudeOfAscendingNode = elements.LongitudeOfAscendingNode;
                    ArgumentOfPeriapsis = elements.ArgumentOfPeriapsis;
                    TrueAnomaly = elements.TrueAnomaly;
                    OrbitalPeriod = elements.Period;
                    PeriapsisDistance = elements.PeriapsisDistance;
                    ApoapsisDistance = elements.ApoapsisDistance;
                    SpecificOrbitalEnergy = elements.SpecificOrbitalEnergy;
                    CentralBodyName = $"{cb.Type} {cb.Id}";
                    ShowOrbitalElements = true;
                }
                else
                {
                    ShowOrbitalElements = false;
                }
            }
            else
            {
                // No valid central body (this might be the most massive object)
                ShowOrbitalElements = false;
                CentralBodyName = "(Primary body)";
            }
        });

        HasSelection = true;
    }

    /// <summary>
    /// Clears the inspector when no body is selected.
    /// </summary>
    public void ClearSelection()
    {
        _currentBodyId = null;
        HasSelection = false;
    }

    /// <summary>
    /// Reloads the selected body values so the inspector stays in sync while the simulation runs.
    /// </summary>
    public void RefreshIfSelected()
    {
        if (_currentBodyId == null)
        {
            return;
        }

        var nodeId = _sceneService.GetNodeIdForBody(_currentBodyId.Value);
        if (nodeId.HasValue)
        {
            LoadBody(nodeId.Value);
        }
    }

    [RelayCommand]
    private void ApplyChanges()
    {
        if (_currentBodyId == null) return;
        int id = _currentBodyId.Value;

        _simService.WithEngineLock(engine =>
        {
            if (engine.Bodies == null) return;
            for (int i = 0; i < engine.Bodies.Length; i++)
            {
                if (engine.Bodies[i].Id != id) continue;

                engine.Bodies[i].Mass = Mass;
                engine.Bodies[i].Radius = Radius;
                engine.Bodies[i].Position = new Math.Vec3d(PositionX, PositionY, PositionZ);
                engine.Bodies[i].Velocity = new Math.Vec3d(VelocityX, VelocityY, VelocityZ);
                engine.Bodies[i].Type = BodyType;
                engine.Bodies[i].IsActive = IsActive;
                engine.Bodies[i].IsCollidable = IsCollidable;
                break;
            }
        });
    }
}
