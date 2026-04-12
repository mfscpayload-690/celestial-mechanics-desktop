using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;
using CelestialMechanics.Desktop.Infrastructure;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Renderer;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// Top-level ViewModel for MainWindow.
/// Manages the multi-stage navigation flow and the simulation IDE workspace.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SimulationService _simService;
    private readonly SceneService _sceneService;
    private readonly GLRenderer _renderer;
    private readonly ProjectService _projectService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _uiTimer;

    // ── Service Accessors (for code-behind viewport initialization) ──

    public SimulationService SimService => _simService;
    public SceneService SceneService => _sceneService;
    public GLRenderer Renderer => _renderer;

    /// <summary>Reference to the render loop for reading FPS metrics. Set after viewport init.</summary>
    public RenderLoop? ActiveRenderLoop { get; set; }

    // ── Child ViewModels ─────────────────────────────────────────────

    public ModeSelectionViewModel ModeSelectionVm { get; }
    public SimulationMenuViewModel SimulationMenuVm { get; }
    public NewProjectViewModel NewProjectVm { get; }
    public ProjectsListViewModel ProjectsListVm { get; }
    public FileMenuViewModel FileMenuVm { get; }

    // Phase 4: IDE Panel ViewModels
    public SceneOutlinerViewModel SceneOutlinerVm { get; }
    public BodyInspectorViewModel BodyInspectorVm { get; }
    public SimulationSettingsViewModel SimulationSettingsVm { get; }

    // ── Navigation ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalVisible))]
    [NotifyPropertyChangedFor(nameof(IsIdeActive))]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlBarVisible))]
    private NavigationState _navState = NavigationState.SimulationMenu;

    /// <summary>True when any modal overlay should be visible.</summary>
    public bool IsModalVisible => NavState != NavigationState.SimulationIDE;

    /// <summary>True when the full simulation IDE workspace is active.</summary>
    public bool IsIdeActive => NavState == NavigationState.SimulationIDE;

    // ── Current Project ──────────────────────────────────────────────

    [ObservableProperty]
    private ProjectInfo? _currentProject;

    [ObservableProperty]
    private string _windowTitle = "Celestial Mechanics \u2014 Desktop";

    // ── UI Mode State Machine ────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddMode))]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(IsAnalyseMode))]
    [NotifyPropertyChangedFor(nameof(IsSimulateMode))]
    private UiMode _currentMode = UiMode.Idle;

    public bool IsAddMode => CurrentMode == UiMode.AddPlacement;
    public bool IsEditMode => CurrentMode == UiMode.Edit;
    public bool IsAnalyseMode => CurrentMode == UiMode.Analyse;
    public bool IsSimulateMode => CurrentMode == UiMode.Simulate;

    // ── Object Placement State ───────────────────────────────────────

    /// <summary>When true, the user is in placement mode with a ghost object on cursor.</summary>
    [ObservableProperty]
    private bool _isPlacingObject;

    /// <summary>The type of celestial body being placed.</summary>
    [ObservableProperty]
    private string _placementObjectType = string.Empty;

    /// <summary>The currently selected body type for placement.</summary>
    [ObservableProperty]
    private BodyType _selectedBodyType = BodyType.Star;

    /// <summary>The currently selected subtype for placement (from cascading menu).</summary>
    [ObservableProperty]
    private BodySubtype? _selectedSubtype;

    /// <summary>All body types available for the palette.</summary>
    public IReadOnlyList<BodyType> AllBodyTypes { get; } =
        Enum.GetValues<BodyType>().ToList().AsReadOnly();

    // ── Two-Step Placement State (Module B) ──────────────────────────

    /// <summary>Current phase of the two-step placement workflow.</summary>
    [ObservableProperty]
    private PlacementPhase _placementPhase = PlacementPhase.Inactive;

    /// <summary>Ghost body position (follows cursor during ChoosingPosition).</summary>
    [ObservableProperty]
    private float _ghostX, _ghostY, _ghostZ;

    /// <summary>Confirmed position after first click (during ChoosingVelocity).</summary>
    [ObservableProperty]
    private float _placedX, _placedY, _placedZ;

    /// <summary>Cursor position for velocity vector endpoint (during ChoosingVelocity).</summary>
    [ObservableProperty]
    private float _velocityEndX, _velocityEndY, _velocityEndZ;

    /// <summary>Scale factor for converting cursor distance to velocity magnitude.</summary>
    private const float VelocityScaleFactor = 0.5f;

    /// <summary>Maximum number of celestial bodies allowed in the simulation.</summary>
    private const int MaxBodies = 15;

    // ── Time Scale (Time Flow Slider) ────────────────────────────────

    [ObservableProperty]
    private double _timeScale = 1.0;

    // ── Status Bar Metrics ───────────────────────────────────────────

    [ObservableProperty]
    private string _fpsText = "FPS: --";

    [ObservableProperty]
    private string _bodyCountText = "Bodies: 0";

    [ObservableProperty]
    private string _physicsTimeText = "Physics: -- ms";

    [ObservableProperty]
    private string _renderTimeText = "Render: -- ms";

    [ObservableProperty]
    private string _simTimeText = "T: 0.0000";

    [ObservableProperty]
    private string _totalEnergyText = "E: --";

    [ObservableProperty]
    private string _momentumText = "P: --";

    [ObservableProperty]
    private SimLifecycleState _simulationState = SimLifecycleState.Idle;

    [ObservableProperty]
    private string _simulationStateText = "Idle";

    [ObservableProperty]
    private string _runtimeModeText = "Runtime: WPF Desktop (shared engine)";

    // ── Toolbar Toggles ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _showGrid = true;

    [ObservableProperty]
    private bool _showVelocityArrows;

    [ObservableProperty]
    private bool _showStarfield = true;

    [ObservableProperty]
    private bool _showTrails = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    private bool _showStatusBar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlBarVisible))]
    private bool _showControlBar = true;

    // ── Panel Visibility Toggles (Phase 4) ───────────────────────────

    [ObservableProperty]
    private bool _showSceneOutliner = true;

    [ObservableProperty]
    private bool _showInspector = true;

    /// <summary>
    /// Index of the selected tab in the right-panel TabControl.
    /// 0 = Inspector, 1 = Settings.
    /// </summary>
    [ObservableProperty]
    private int _rightPanelTabIndex;

    /// <summary>True when the compact status bar should be visible (IDE active + toggle on).</summary>
    public bool IsStatusBarVisible => IsIdeActive && ShowStatusBar;

    /// <summary>True when the floating control bar should be visible (IDE active + toggle on).</summary>
    public bool IsControlBarVisible => IsIdeActive && ShowControlBar;

    // ── Constructor ──────────────────────────────────────────────────

    public MainWindowViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // 1. Create services
        _simService = new SimulationService();
        _renderer = new GLRenderer();
        _sceneService = new SceneService();
        _projectService = new ProjectService();

        // 2. Create child ViewModels — Navigation
        ModeSelectionVm = new ModeSelectionViewModel();
        SimulationMenuVm = new SimulationMenuViewModel();
        NewProjectVm = new NewProjectViewModel(_projectService);
        ProjectsListVm = new ProjectsListViewModel(_projectService);
        FileMenuVm = new FileMenuViewModel();

        // 3. Create child ViewModels — IDE Panels (Phase 4)
        SceneOutlinerVm = new SceneOutlinerViewModel(_sceneService, _simService, dispatcher);
        BodyInspectorVm = new BodyInspectorViewModel(_simService, _sceneService);
        SimulationSettingsVm = new SimulationSettingsViewModel(_simService);

        // 4. Wire navigation events
        WireNavigation();

        // 5. Wire IDE panel events
        WireIdePanels();

        // 6. UI refresh timer (20 Hz)
        _uiTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _uiTimer.Tick += OnUiTimerTick;
    }

    // ── Navigation Wiring ────────────────────────────────────────────

    private void WireNavigation()
    {
        // Mode Selection
        ModeSelectionVm.SimulationSelected += () => NavState = NavigationState.SimulationMenu;
        ModeSelectionVm.ExitRequested += () => System.Windows.Application.Current.Shutdown();

        // Simulation Menu
        SimulationMenuVm.NewProjectRequested += () =>
        {
            NewProjectVm.Reset();
            NavState = NavigationState.NewProject;
        };
        SimulationMenuVm.FileRequested += () => NavState = NavigationState.FileMenu;
        SimulationMenuVm.ProjectsRequested += () =>
        {
            ProjectsListVm.RefreshProjects();
            NavState = NavigationState.ProjectsList;
        };
        SimulationMenuVm.BackRequested += () => NavState = NavigationState.ModeSelection;

        // New Project
        NewProjectVm.ProjectCreated += OnProjectOpened;
        NewProjectVm.CancelRequested += () => NavState = NavigationState.SimulationMenu;

        // Projects List
        ProjectsListVm.ProjectOpened += OnProjectOpened;
        ProjectsListVm.CancelRequested += () => NavState = NavigationState.SimulationMenu;

        // File Menu
        FileMenuVm.BackRequested += () => NavState = NavigationState.SimulationMenu;
    }

    /// <summary>
    /// Wires events between IDE panel ViewModels (Phase 4).
    /// </summary>
    private void WireIdePanels()
    {
        // Outliner selection → Inspector load
        SceneOutlinerVm.BodySelected += nodeId => BodyInspectorVm.LoadBody(nodeId);

        // Outliner delete request → delete body
        SceneOutlinerVm.DeleteRequested += DeleteBody;

        // Settings panel close → switch back to Inspector tab
        SimulationSettingsVm.CloseRequested += () => RightPanelTabIndex = 0;
    }

    /// <summary>
    /// Common handler: project created or opened — enter the IDE.
    /// </summary>
    private void OnProjectOpened(ProjectInfo project)
    {
        CurrentProject = project;
        WindowTitle = $"Celestial Mechanics \u2014 {project.Name}";

        if (!TryLoadProjectState(project))
        {
            SeedDefaultScenarioForProject(project);
        }

        _sceneService.RepopulateFromSimulation(_simService);

        // Start simulation engine and UI timer
        _simService.StartSimThread();
        _simService.Play();
        _renderer.ClearTrails();
        _uiTimer.Start();

        NavState = NavigationState.SimulationIDE;
        CurrentMode = UiMode.Idle;

        // Reset camera to a good default view
        _renderer.Camera.ResetToDefault();

        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.Refresh();
    }

    /// <summary>
    /// Public entry point to enter the IDE with the given project.
    /// Used by SimulationWindow to bypass the modal navigation flow.
    /// </summary>
    public void ForceOpenProject(ProjectInfo project) => OnProjectOpened(project);

    private bool TryLoadProjectState(ProjectInfo project)
    {
        var statePath = Path.Combine(project.Path, "simulation_state.json");
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<SimulationSaveState>(json);
            if (state == null)
            {
                return false;
            }

            _simService.WithEngineLock(engine =>
            {
                engine.Stop();

                var bodies = state.Bodies.Select(b => new PhysicsBody(
                    b.Id,
                    mass: b.Mass,
                    position: new Vec3d(b.PositionX, b.PositionY, b.PositionZ),
                    velocity: new Vec3d(b.VelocityX, b.VelocityY, b.VelocityZ),
                    type: b.Type)
                {
                    Radius = b.Radius,
                    Density = b.Density,
                    IsActive = b.IsActive,
                    IsCollidable = b.IsCollidable,
                }).ToArray();
                engine.SetBodies(bodies);

                var c = state.Config;
                engine.Config.IntegratorName = c.IntegratorName;
                engine.Config.TimeStep = c.TimeStep;
                engine.Config.MinDt = c.MinDt;
                engine.Config.MaxDt = c.MaxDt;
                engine.Config.DeterministicMode = c.DeterministicMode;
                engine.Config.UseParallelComputation = c.UseParallelComputation;
                engine.Config.UseSimd = c.UseSimd;
                engine.Config.UseSoAPath = c.UseSoAPath;
                engine.Config.UseBarnesHut = c.UseBarnesHut;
                engine.Config.Theta = c.Theta;
                engine.Config.EnableCollisions = c.EnableCollisions;
                engine.Config.UseAdaptiveTimestep = c.UseAdaptiveTimestep;
                engine.Config.EnablePostNewtonian = c.EnablePostNewtonian;
                engine.Config.EnableGravitationalLensing = c.EnableGravitationalLensing;
                engine.Config.EnableAccretionDisks = c.EnableAccretionDisks;
                engine.Config.EnableGravitationalWaves = c.EnableGravitationalWaves;
                engine.Config.EnableJetEmission = c.EnableJetEmission;
                engine.Config.SofteningEpsilon = c.SofteningEpsilon;
                if (Enum.TryParse<SofteningMode>(c.SofteningMode, out var sm))
                {
                    engine.Config.SofteningMode = sm;
                }

                engine.ApplyConfig();
            });

            _simService.SetIntegrator(state.Config.IntegratorName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SeedDefaultScenarioForProject(ProjectInfo project)
    {
        _simService.WithEngineLock(engine =>
        {
            engine.Stop();
            engine.SetBodies(DefaultSimulationScenario.CreateTwoBodyOrbit());
        });

        SaveProjectState(project);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MENU COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void NewSimulation()
    {
        if (IsModalVisible)
            return;

        // Reset existing simulation to empty scene
        _simService.ResetScene();
        _sceneService.RepopulateFromSimulation(_simService);
        BodyInspectorVm.ClearSelection();
        CurrentMode = UiMode.Idle;
    }

    [RelayCommand]
    private void Open()
    {
        if (CurrentProject == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Simulation State|simulation_state.json|All Files|*.*",
            InitialDirectory = CurrentProject.Path
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var state = JsonSerializer.Deserialize<SimulationSaveState>(json);
            if (state == null) return;

            ApplySimulationState(state);
            _sceneService.RepopulateFromSimulation(_simService);
            SceneOutlinerVm.Refresh();
            BodyInspectorVm.ClearSelection();
        }
        catch
        {
            // Silently ignore deserialization errors for now
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (CurrentProject == null) return;
        SaveProjectState(CurrentProject);
    }

    [RelayCommand]
    private void Exit()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void ApplySimulationState(SimulationSaveState state)
    {
        _simService.WithEngineLock(engine =>
        {
            engine.Stop();

            var bodies = state.Bodies.Select(b => new PhysicsBody(
                b.Id,
                mass: b.Mass,
                position: new Vec3d(b.PositionX, b.PositionY, b.PositionZ),
                velocity: new Vec3d(b.VelocityX, b.VelocityY, b.VelocityZ),
                type: b.Type)
            {
                Radius = b.Radius,
                Density = b.Density,
                IsActive = b.IsActive,
                IsCollidable = b.IsCollidable,
            }).ToArray();
            engine.SetBodies(bodies);

            var c = state.Config;
            engine.Config.IntegratorName = c.IntegratorName;
            engine.Config.TimeStep = c.TimeStep;
            engine.Config.MinDt = c.MinDt;
            engine.Config.MaxDt = c.MaxDt;
            engine.Config.DeterministicMode = c.DeterministicMode;
            engine.Config.UseParallelComputation = c.UseParallelComputation;
            engine.Config.UseSimd = c.UseSimd;
            engine.Config.UseSoAPath = c.UseSoAPath;
            engine.Config.UseBarnesHut = c.UseBarnesHut;
            engine.Config.Theta = c.Theta;
            engine.Config.EnableCollisions = c.EnableCollisions;
            engine.Config.UseAdaptiveTimestep = c.UseAdaptiveTimestep;
            engine.Config.EnablePostNewtonian = c.EnablePostNewtonian;
            engine.Config.EnableGravitationalLensing = c.EnableGravitationalLensing;
            engine.Config.EnableAccretionDisks = c.EnableAccretionDisks;
            engine.Config.EnableGravitationalWaves = c.EnableGravitationalWaves;
            engine.Config.EnableJetEmission = c.EnableJetEmission;
            engine.Config.SofteningEpsilon = c.SofteningEpsilon;
            if (Enum.TryParse<SofteningMode>(c.SofteningMode, out var sm))
            {
                engine.Config.SofteningMode = sm;
            }

            engine.ApplyConfig();
        });

        _simService.SetIntegrator(state.Config.IntegratorName);
    }

    private void SaveProjectState(ProjectInfo project)
    {
        var statePath = Path.Combine(project.Path, "simulation_state.json");

        _simService.WithEngineLock(engine =>
        {
            var state = new SimulationSaveState();

            if (engine.Bodies != null)
            {
                state.Bodies = engine.Bodies.Select(b => new BodySaveData
                {
                    Id = b.Id,
                    Mass = b.Mass,
                    Radius = b.Radius,
                    Density = b.Density,
                    PositionX = b.Position.X,
                    PositionY = b.Position.Y,
                    PositionZ = b.Position.Z,
                    VelocityX = b.Velocity.X,
                    VelocityY = b.Velocity.Y,
                    VelocityZ = b.Velocity.Z,
                    Type = b.Type,
                    IsActive = b.IsActive,
                    IsCollidable = b.IsCollidable,
                }).ToList();
            }

            var c = engine.Config;
            state.Config = new ConfigSaveData
            {
                IntegratorName = c.IntegratorName,
                TimeStep = c.TimeStep,
                MinDt = c.MinDt,
                MaxDt = c.MaxDt,
                DeterministicMode = c.DeterministicMode,
                UseParallelComputation = c.UseParallelComputation,
                UseSimd = c.UseSimd,
                UseSoAPath = c.UseSoAPath,
                UseBarnesHut = c.UseBarnesHut,
                Theta = c.Theta,
                EnableCollisions = c.EnableCollisions,
                UseAdaptiveTimestep = c.UseAdaptiveTimestep,
                EnablePostNewtonian = c.EnablePostNewtonian,
                EnableGravitationalLensing = c.EnableGravitationalLensing,
                EnableAccretionDisks = c.EnableAccretionDisks,
                EnableGravitationalWaves = c.EnableGravitationalWaves,
                EnableJetEmission = c.EnableJetEmission,
                SofteningEpsilon = c.SofteningEpsilon,
                SofteningMode = c.SofteningMode.ToString(),
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(statePath, json);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODE COMMANDS (Bottom Control Bar — Left Group)
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void EnterAddMode()
    {
        if (CurrentMode == UiMode.AddPlacement)
        {
            CurrentMode = UiMode.Idle;
            IsPlacingObject = false;
            PlacementPhase = PlacementPhase.Inactive;
            PlacementObjectType = string.Empty;
        }
        else
        {
            CurrentMode = UiMode.AddPlacement;
            IsPlacingObject = true;
            PlacementPhase = PlacementPhase.ChoosingPosition;
            SelectedBodyType = BodyType.Star;
            PlacementObjectType = "Star";
        }
    }

    [RelayCommand]
    private void EnterSimulateMode()
    {
        if (CurrentMode == UiMode.Simulate)
        {
            CurrentMode = UiMode.Idle;
            _simService.Pause();
        }
        else
        {
            CurrentMode = UiMode.Simulate;
            IsPlacingObject = false;
            _simService.StartSimThread(); // idempotent
            _simService.Play();
        }
    }

    [RelayCommand]
    private void EnterEditMode()
    {
        CurrentMode = CurrentMode == UiMode.Edit ? UiMode.Idle : UiMode.Edit;
        IsPlacingObject = false;
    }

    [RelayCommand]
    private void EnterAnalyseMode()
    {
        CurrentMode = CurrentMode == UiMode.Analyse ? UiMode.Idle : UiMode.Analyse;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (RightPanelTabIndex == 1)
        {
            RightPanelTabIndex = 0; // Switch back to Inspector
        }
        else
        {
            RightPanelTabIndex = 1; // Switch to Settings tab
            ShowInspector = true;   // Ensure right panel is visible
        }
    }

    /// <summary>
    /// Changes the selected body type for placement mode.
    /// Called from the BodyTypePalette.
    /// </summary>
    public void SelectBodyType(BodyType type)
    {
        SelectedBodyType = type;
        PlacementObjectType = type.ToString();
        
        // Auto-select first subtype in category
        var subtypes = BodyCatalog.GetSubtypes(type);
        SelectedSubtype = subtypes.Count > 0 ? subtypes[0] : null;
    }

    /// <summary>
    /// Selects a specific subtype for placement.
    /// Called from the cascading subtype menu.
    /// </summary>
    public void SelectSubtype(BodySubtype subtype)
    {
        SelectedSubtype = subtype;
        SelectedBodyType = subtype.BaseType;
        PlacementObjectType = subtype.Name;
    }

    // ═══════════════════════════════════════════════════════════════
    //  TWO-STEP PLACEMENT (Module B)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the ghost position from cursor world coordinates.
    /// Called by ViewportPanel during ChoosingPosition phase.
    /// </summary>
    public void UpdateGhostPosition(float worldX, float worldY, float worldZ)
    {
        if (PlacementPhase != PlacementPhase.ChoosingPosition) return;
        GhostX = worldX;
        GhostY = worldY;
        GhostZ = worldZ;
        UpdateRendererGhost();
    }

    /// <summary>
    /// Updates the velocity endpoint from cursor world coordinates.
    /// Called by ViewportPanel during ChoosingVelocity phase.
    /// </summary>
    public void UpdateVelocityEndpoint(float worldX, float worldY, float worldZ)
    {
        if (PlacementPhase != PlacementPhase.ChoosingVelocity) return;
        VelocityEndX = worldX;
        VelocityEndY = worldY;
        VelocityEndZ = worldZ;
        UpdateRendererVelocityPreview();
    }

    /// <summary>
    /// Confirms the ghost position and moves to velocity selection phase.
    /// Called when user first-clicks during ChoosingPosition.
    /// </summary>
    public void ConfirmPosition()
    {
        if (PlacementPhase != PlacementPhase.ChoosingPosition) return;

        PlacedX = GhostX;
        PlacedY = GhostY;
        PlacedZ = GhostZ;
        VelocityEndX = PlacedX;
        VelocityEndY = PlacedY;
        VelocityEndZ = PlacedZ;
        PlacementPhase = PlacementPhase.ChoosingVelocity;

        // Update renderer ghost to be at confirmed position (solid)
        _renderer.GhostPosition = new System.Numerics.Vector3(PlacedX, PlacedY, PlacedZ);
        _renderer.GhostAlpha = 1.0f;
        _renderer.GhostRadius = (float)GetPlacementRadius();
        _renderer.GhostBodyType = (int)SelectedBodyType;
        _renderer.ShowGhost = true;
    }

    /// <summary>
    /// Confirms velocity and places the body in the simulation.
    /// Called when user left-clicks during ChoosingVelocity.
    /// </summary>
    public void ConfirmVelocityAndPlace()
    {
        if (PlacementPhase != PlacementPhase.ChoosingVelocity) return;

        // ── Max body limit check ──
        bool limitReached = false;
        _simService.WithEngineLock(engine =>
        {
            if ((engine.Bodies?.Length ?? 0) >= MaxBodies)
                limitReached = true;
        });
        if (limitReached)
        {
            System.Windows.MessageBox.Show(
                "Maximum object limit reached. Delete an existing object.",
                "Object Limit",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var placedPos = new Vec3d(PlacedX, PlacedY, PlacedZ);
        var cursorPos = new Vec3d(VelocityEndX, VelocityEndY, VelocityEndZ);
        var velocity = ComputeVelocityFromCursor(placedPos, cursorPos);

        _simService.WithEngineLock(engine =>
        {
            int nextId = (engine.Bodies?.Length ?? 0) + 1;
            var body = new PhysicsBody(
                nextId,
                mass: GetPlacementMass(),
                position: placedPos,
                velocity: velocity,
                type: SelectedBodyType);
            engine.AddBody(body);
        });

        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();

        // Return to ChoosingPosition for continuous placement
        PlacementPhase = PlacementPhase.ChoosingPosition;
        ClearRendererPreview();
    }

    /// <summary>
    /// Places body with zero velocity (for stationary bodies).
    /// Called on Space key or double-click during ChoosingVelocity.
    /// </summary>
    public void PlaceWithZeroVelocity()
    {
        if (PlacementPhase != PlacementPhase.ChoosingVelocity) return;

        // ── Max body limit check ──
        bool limitReached = false;
        _simService.WithEngineLock(engine =>
        {
            if ((engine.Bodies?.Length ?? 0) >= MaxBodies)
                limitReached = true;
        });
        if (limitReached)
        {
            System.Windows.MessageBox.Show(
                "Maximum object limit reached. Delete an existing object.",
                "Object Limit",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var placedPos = new Vec3d(PlacedX, PlacedY, PlacedZ);

        _simService.WithEngineLock(engine =>
        {
            int nextId = (engine.Bodies?.Length ?? 0) + 1;
            var body = new PhysicsBody(
                nextId,
                mass: GetPlacementMass(),
                position: placedPos,
                velocity: Vec3d.Zero,
                type: SelectedBodyType);
            engine.AddBody(body);
        });

        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();

        // Return to ChoosingPosition for continuous placement
        PlacementPhase = PlacementPhase.ChoosingPosition;
        ClearRendererPreview();
    }

    /// <summary>
    /// Computes the velocity vector from placed position to cursor position.
    /// </summary>
    private Vec3d ComputeVelocityFromCursor(Vec3d placedPos, Vec3d cursorPos)
    {
        var delta = cursorPos - placedPos;
        return delta * VelocityScaleFactor;
    }

    /// <summary>
    /// Computes a gravity-bent trajectory preview using simple Euler integration.
    /// </summary>
    public System.Numerics.Vector3[] ComputeTrajectoryPreview(Vec3d pos, Vec3d vel, int steps = 50)
    {
        var trajectory = new System.Numerics.Vector3[steps];
        var currentPos = pos;
        var currentVel = vel;
        const double dt = 0.02; // Time step for preview
        const double G = 1.0;   // Gravitational constant (simulation units)
        const double softeningEps = 0.01;

        // Get current bodies from engine
        PhysicsBody[]? bodies = null;
        _simService.WithEngineLock(engine =>
        {
            bodies = engine.Bodies?.ToArray();
        });

        for (int i = 0; i < steps; i++)
        {
            trajectory[i] = new System.Numerics.Vector3((float)currentPos.X, (float)currentPos.Y, (float)currentPos.Z);

            // Compute gravitational acceleration from all existing bodies
            var accel = Vec3d.Zero;
            if (bodies != null)
            {
                foreach (var body in bodies)
                {
                    if (!body.IsActive) continue;
                    var r = body.Position - currentPos;
                    double distSq = r.LengthSquared + softeningEps * softeningEps;
                    double dist = System.Math.Sqrt(distSq);
                    if (dist < 0.001) continue;
                    double forceMag = G * body.Mass / distSq;
                    accel = accel + r.Normalized() * forceMag;
                }
            }

            // Euler integration step
            currentVel = currentVel + accel * dt;
            currentPos = currentPos + currentVel * dt;
        }

        return trajectory;
    }

    /// <summary>Gets the mass for the current placement (from subtype or default).</summary>
    private double GetPlacementMass() =>
        SelectedSubtype?.Mass ?? DefaultMassForType(SelectedBodyType);

    /// <summary>Gets the radius for the current placement (from subtype or default).</summary>
    private double GetPlacementRadius() =>
        SelectedSubtype?.Radius ?? DefaultRadiusForType(SelectedBodyType);

    private void UpdateRendererGhost()
    {
        _renderer.GhostPosition = new System.Numerics.Vector3(GhostX, GhostY, GhostZ);
        _renderer.GhostAlpha = 0.4f;
        _renderer.GhostRadius = (float)GetPlacementRadius();
        _renderer.GhostBodyType = (int)SelectedBodyType;
        _renderer.ShowGhost = true;
    }

    private void UpdateRendererVelocityPreview()
    {
        var placedPos = new Vec3d(PlacedX, PlacedY, PlacedZ);
        var cursorPos = new Vec3d(VelocityEndX, VelocityEndY, VelocityEndZ);
        var velocity = ComputeVelocityFromCursor(placedPos, cursorPos);

        // Update velocity vector line
        _renderer.VelocityPreviewStart = new System.Numerics.Vector3(PlacedX, PlacedY, PlacedZ);
        _renderer.VelocityPreviewEnd = new System.Numerics.Vector3(VelocityEndX, VelocityEndY, VelocityEndZ);
        _renderer.ShowVelocityPreview = true;

        // Compute and update trajectory preview
        _renderer.TrajectoryPreview = ComputeTrajectoryPreview(placedPos, velocity, 50);
        _renderer.ShowTrajectoryPreview = true;
    }

    private void ClearRendererPreview()
    {
        _renderer.ShowGhost = false;
        _renderer.ShowVelocityPreview = false;
        _renderer.ShowTrajectoryPreview = false;
        _renderer.TrajectoryPreview = null;
    }

    /// <summary>
    /// Called by ViewportPanel when user presses Escape to cancel placement entirely.
    /// </summary>
    public void CancelPlacement()
    {
        if (CurrentMode == UiMode.AddPlacement)
        {
            IsPlacingObject = false;
            PlacementPhase = PlacementPhase.Inactive;
            PlacementObjectType = string.Empty;
            CurrentMode = UiMode.Idle;
            ClearRendererPreview();
        }
    }

    /// <summary>
    /// Cancels the velocity phase and returns to position choosing.
    /// Called by ViewportPanel when user right-clicks during ChoosingVelocity.
    /// </summary>
    public void CancelVelocityPhase()
    {
        if (PlacementPhase != PlacementPhase.ChoosingVelocity) return;
        PlacementPhase = PlacementPhase.ChoosingPosition;
        ClearRendererPreview();
    }

    /// <summary>Returns a sensible default radius for each body type (in AU).</summary>
    private static double DefaultRadiusForType(BodyType type) => type switch
    {
        BodyType.Star => 0.1,
        BodyType.Planet => 0.03,
        BodyType.GasGiant => 0.05,
        BodyType.RockyPlanet => 0.02,
        BodyType.Moon => 0.01,
        BodyType.Asteroid => 0.005,
        BodyType.NeutronStar => 0.02,
        BodyType.BlackHole => 0.08,
        BodyType.Comet => 0.003,
        BodyType.Custom => 0.04,
        _ => 0.04,
    };

    // ═══════════════════════════════════════════════════════════════
    //  BODY MANAGEMENT (Phase 4)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Deletes a body by its engine ID.</summary>
    private void DeleteBody(int bodyId)
    {
        _simService.RemoveBody(bodyId);
        _sceneService.RepopulateFromSimulation(_simService);
        BodyInspectorVm.ClearSelection();
    }

    /// <summary>
    /// Selects a body by its engine ID. Called from ViewportPanel on raycast hit.
    /// </summary>
    public void SelectBodyById(int bodyId)
    {
        var nodeId = _sceneService.GetNodeIdForBody(bodyId);
        if (nodeId.HasValue)
        {
            _sceneService.SelectionManager.Select(nodeId.Value);
            BodyInspectorVm.LoadBody(nodeId.Value);
            SceneOutlinerVm.SetSelectedNodeId(nodeId.Value);
        }
    }

    /// <summary>
    /// Deselects the current body. Called from ViewportPanel on empty-space click.
    /// </summary>
    public void DeselectBody()
    {
        _sceneService.SelectionManager.Clear();
        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.SetSelectedNodeId(null);
    }

    [RelayCommand]
    private void DeleteSelectedBody()
    {
        var selectedNodeId = _sceneService.SelectionManager.SelectedEntity;
        if (selectedNodeId == null) return;
        var bodyId = _sceneService.GetBodyIdForNode(selectedNodeId.Value);
        if (bodyId.HasValue)
            DeleteBody(bodyId.Value);
    }

    /// <summary>
    /// Sets the selected body as the camera reference frame (tracks this body).
    /// </summary>
    [RelayCommand]
    private void SetReferenceFrame(int bodyId)
    {
        _simService.WithEngineLock(engine =>
        {
            var bodies = engine.Bodies;
            if (bodies == null) return;

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Id == bodyId)
                {
                    var pos = new System.Numerics.Vector3(
                        (float)bodies[i].Position.X,
                        (float)bodies[i].Position.Y,
                        (float)bodies[i].Position.Z);
                    float dist = MathF.Max((float)bodies[i].Radius * 5f, 2f);
                    _renderer.Camera.FlyTo(pos, dist);
                    // Camera now tracks this position; future updates will keep it centered
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Inspects the currently selected body (opens inspector panel).
    /// </summary>
    [RelayCommand]
    private void InspectBody()
    {
        RightPanelTabIndex = 0;
        ShowInspector = true;
    }

    /// <summary>
    /// Focuses the camera on the currently selected body.
    /// </summary>
    [RelayCommand]
    private void FocusCameraOnSelected()
    {
        var selectedNodeId = _sceneService.SelectionManager.SelectedEntity;
        if (selectedNodeId == null) return;
        
        var bodyId = _sceneService.GetBodyIdForNode(selectedNodeId.Value);
        if (!bodyId.HasValue) return;

        _simService.WithEngineLock(engine =>
        {
            var bodies = engine.Bodies;
            if (bodies == null) return;

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Id == bodyId.Value)
                {
                    var pos = new System.Numerics.Vector3(
                        (float)bodies[i].Position.X,
                        (float)bodies[i].Position.Y,
                        (float)bodies[i].Position.Z);
                    float dist = MathF.Max((float)bodies[i].Radius * 4f, 2f);
                    _renderer.Camera.FlyTo(pos, dist);
                    break;
                }
            }
        });
    }

    /// <summary>Returns a sensible default mass for each body type.</summary>
    private static double DefaultMassForType(BodyType type) => type switch
    {
        BodyType.Star => 1.0,
        BodyType.Planet => 0.001,
        BodyType.GasGiant => 0.01,
        BodyType.RockyPlanet => 0.0005,
        BodyType.Moon => 0.0001,
        BodyType.Asteroid => 0.00001,
        BodyType.NeutronStar => 2.0,
        BodyType.BlackHole => 10.0,
        BodyType.Comet => 0.000001,
        BodyType.Custom => 1.0,
        _ => 1.0,
    };

    // ═══════════════════════════════════════════════════════════════
    //  SIMULATION CONTROL COMMANDS (Bottom Control Bar — Right Group)
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void StartSimulation()
    {
        if (_simService.LastState != EngineState.Running)
        {
            _simService.StartSimThread(); // idempotent
            _simService.Play();
        }
    }

    [RelayCommand]
    private void PauseSimulation()
    {
        if (_simService.LastState == EngineState.Running)
        {
            _simService.Pause();
        }
    }

    [RelayCommand]
    private void ResetSimulation()
    {
        _simService.Pause();
        _simService.ResetScene();

        _sceneService.RepopulateFromSimulation(_simService);
        _renderer.ClearTrails();
        _renderer.SelectedInstanceIndex = -1;
        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.Refresh();
        CurrentMode = UiMode.Idle;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PROPERTY CHANGE CALLBACKS
    // ═══════════════════════════════════════════════════════════════

    partial void OnTimeScaleChanged(double value)
    {
        _simService.TimeScale = value;
    }

    partial void OnShowGridChanged(bool value)
    {
        _renderer.ShowGrid = value;
    }

    partial void OnShowVelocityArrowsChanged(bool value)
    {
        _renderer.ShowVelocityArrows = value;
    }

    partial void OnShowStarfieldChanged(bool value)
    {
        _renderer.ShowStarfield = value;
    }

    partial void OnShowTrailsChanged(bool value)
    {
        _renderer.ShowTrails = value;
    }

    partial void OnRightPanelTabIndexChanged(int value)
    {
        if (value == 1)
        {
            SimulationSettingsVm.LoadFromEngine();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  UI TIMER (20 Hz status bar refresh)
    // ═══════════════════════════════════════════════════════════════

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        var state = _simService.LastState;

        SimulationState = state switch
        {
            EngineState.Running => SimLifecycleState.Running,
            EngineState.Paused  => SimLifecycleState.Paused,
            _                   => SimLifecycleState.Idle
        };
        SimulationStateText = SimulationState.ToString();

        PhysicsTimeText = $"Physics: {_simService.LastPhysicsTimeMs:F1} ms";
        SimTimeText = $"T: {_simService.LastSimTime:F4}";

        // Read energy/momentum from last simulation state snapshot
        var simState = _simService.LastSimState;
        if (simState != null)
        {
            TotalEnergyText = $"E: {simState.TotalEnergy:E4}";
            var mom = simState.TotalMomentum;
            double pMag = System.Math.Sqrt(mom.X * mom.X + mom.Y * mom.Y + mom.Z * mom.Z);
            MomentumText = $"P: {pMag:E4}";
        }

        _simService.WithEngineLock(engine =>
        {
            BodyCountText = $"Bodies: {engine.Bodies?.Length ?? 0}";
        });

        // BUG 3 FIX: Read FPS/render metrics from the render loop
        if (ActiveRenderLoop?.IsInitialized == true)
        {
            UpdateRenderMetrics(ActiveRenderLoop.CurrentFps, ActiveRenderLoop.LastRenderTimeMs);
        }
        else if (ActiveRenderLoop?.LastError != null)
        {
            FpsText = "FPS: ERR";
            RenderTimeText = "Render: ERR";
        }

        // Live-update inspector values during simulation
        BodyInspectorVm.RefreshIfSelected();
    }

    /// <summary>
    /// Called from code-behind to feed render-thread metrics into the ViewModel.
    /// </summary>
    public void UpdateRenderMetrics(double fps, double renderTimeMs)
    {
        FpsText = $"FPS: {fps:F0}";
        RenderTimeText = $"Render: {renderTimeMs:F1} ms";
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        _uiTimer.Stop();
        SceneOutlinerVm.Dispose();
        _simService.Dispose();
        _renderer.Dispose();
        _sceneService.Dispose();
    }
}
