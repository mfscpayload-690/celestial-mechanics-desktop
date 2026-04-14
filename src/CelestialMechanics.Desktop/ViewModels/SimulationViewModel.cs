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
/// ViewModel for the real simulation panel.
/// </summary>
public sealed partial class SimulationViewModel : ObservableObject, IDisposable
{
    private readonly SimulationService _simService;
    private readonly SceneService _sceneService;
    private readonly DesktopSelectionContext _selectionContext;
    private readonly GLRenderer _renderer;
    private readonly ProjectService _projectService;
    private readonly NavigationService _navigationService;
    private readonly RenderSettings _renderSettings;
    private readonly DispatcherTimer _uiTimer;
    private readonly Stack<PhysicsBody[]> _undoStack = new();
    private readonly Stack<PhysicsBody[]> _redoStack = new();
    private const int MaxUndoDepth = 32;

    // ── Service Accessors (for code-behind viewport initialization) ──

    public SimulationService SimService => _simService;
    public SceneService SceneService => _sceneService;
    public GLRenderer Renderer => _renderer;
    public RenderSettings RenderSettings => _renderSettings;

    public double SimulationSpeed
    {
        get => TimeScale;
        private set => TimeScale = System.Math.Clamp(value, 0.1, 64.0);
    }

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
    private NavigationState _navState = NavigationState.SimulationIDE;

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

    public SimulationViewModel(
        Dispatcher dispatcher,
        SimulationService simulationService,
        SceneService sceneService,
        ProjectService projectService,
        NavigationService navigationService,
        RenderSettings renderSettings)
    {
        // 1. Wire shared services
        _simService = simulationService;
        _sceneService = sceneService;
        _projectService = projectService;
        _navigationService = navigationService;
        _renderSettings = renderSettings;

        // 2. Create renderer-specific context
        _selectionContext = new DesktopSelectionContext();
        _renderer = new GLRenderer(_renderSettings, _selectionContext);
        _renderer.ShowAccretionDisks = _renderSettings.EnableParticles;

        // 3. Create child ViewModels — Navigation (legacy in-panel menu)
        ModeSelectionVm = new ModeSelectionViewModel();
        SimulationMenuVm = new SimulationMenuViewModel();
        NewProjectVm = new NewProjectViewModel(_projectService);
        ProjectsListVm = new ProjectsListViewModel(_projectService);
        FileMenuVm = new FileMenuViewModel();

        // 4. Create child ViewModels — IDE Panels
        SceneOutlinerVm = new SceneOutlinerViewModel(_sceneService, _simService, dispatcher);
        BodyInspectorVm = new BodyInspectorViewModel(_simService, _sceneService);
        SimulationSettingsVm = new SimulationSettingsViewModel(_simService);

        // 5. Wire navigation events
        WireNavigation();

        // 6. Wire IDE panel events
        WireIdePanels();

        // 7. UI refresh timer (20 Hz)
        _uiTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _uiTimer.Tick += OnUiTimerTick;

        _renderSettings.ShowGrid = _renderer.ShowGrid;
        _renderSettings.ShowTrails = _renderSettings.EnableTrails;
        _renderSettings.ShowOrbits = _renderSettings.EnableTrails;
        _renderSettings.ShowGravitationalWaves = _renderSettings.EnableWaves;
        _renderSettings.ShowVelocityVectors = _renderer.ShowVelocityArrows;
        _renderSettings.ShowInspector = ShowInspector;
        _renderSettings.ShowStatistics = ShowStatusBar;

        ShowGrid = _renderSettings.ShowGrid;
        ShowTrails = _renderSettings.ShowTrails;
        ShowVelocityArrows = _renderSettings.ShowVelocityVectors;
        ShowStarfield = _renderer.ShowBackground;
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
        FileMenuVm.NewSimulationRequested += NewSimulation;
        FileMenuVm.OpenRequested += Open;
        FileMenuVm.SaveRequested += Save;
        FileMenuVm.ExitRequested += Exit;
        FileMenuVm.BackRequested += () => NavState = NavigationState.SimulationMenu;
    }

    /// <summary>
    /// Wires events between IDE panel ViewModels (Phase 4).
    /// </summary>
    private void WireIdePanels()
    {
        SceneOutlinerVm.AddBodyRequested += () =>
        {
            if (!IsAddMode)
            {
                EnterAddModeCommand.Execute(null);
            }
        };

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
        _projectService.SetCurrentProject(project);
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
        _renderer.ClearAllHistory();
        _uiTimer.Start();

        NavState = NavigationState.SimulationIDE;
        CurrentMode = UiMode.Idle;

        // Reset camera to a good default view
        _renderer.Camera.Target = System.Numerics.Vector3.Zero;
        _renderer.Camera.Yaw = -90f;
        _renderer.Camera.Pitch = 20f;
        _renderer.Camera.Distance = 10f;

        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.Refresh();
        _undoStack.Clear();
        _redoStack.Clear();
        MarkProjectSaved();
    }

    /// <summary>
    /// Public entry point to enter the IDE with the given project.
    /// Kept for compatibility with older launch flows.
    /// </summary>
    public void ForceOpenProject(ProjectInfo project) => OnProjectOpened(project);

    /// <summary>
    /// Loads the currently selected project from ProjectService into the real simulation panel.
    /// </summary>
    public void LoadCurrentProjectIfAny()
    {
        var project = _projectService.GetCurrentProject();
        if (project == null)
        {
            return;
        }

        if (CurrentProject != null &&
            string.Equals(CurrentProject.Path, project.Path, StringComparison.OrdinalIgnoreCase) &&
            NavState == NavigationState.SimulationIDE)
        {
            return;
        }

        OnProjectOpened(project);
    }

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

                engine.Reconfigure();
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

        RecordUndoSnapshot();

        // Reset existing simulation to empty scene
        _simService.ResetScene();
        _sceneService.RepopulateFromSimulation(_simService);
        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.Refresh();
        CurrentMode = UiMode.Idle;
        MarkProjectDirty();
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
            _undoStack.Clear();
            _redoStack.Clear();
            MarkProjectSaved();
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
        MarkProjectSaved();
    }

    [RelayCommand]
    private void Exit()
    {
        if (!_projectService.IsSaved && !ShowSaveDialog())
        {
            return;
        }

        _simService.Pause();
        _simService.StopSimThread();
        _uiTimer.Stop();
        _renderer.ClearAllHistory();
        _selectionContext.SelectedBodyId = -1;
        BodyInspectorVm.ClearSelection();
        NavState = NavigationState.ModeSelection;
        CurrentMode = UiMode.Idle;
        _navigationService.NavigateToHome();
    }

    [RelayCommand]
    private void CloseProject() => Exit();

    [RelayCommand]
    private void NewProject()
    {
        NewProjectVm.Reset();
        NavState = NavigationState.NewProject;
    }

    [RelayCommand]
    private void OpenProject()
    {
        ProjectsListVm.RefreshProjects();
        NavState = NavigationState.ProjectsList;
    }

    [RelayCommand]
    private void SaveProject() => Save();

    [RelayCommand]
    private void SaveAs()
    {
        if (CurrentProject == null)
        {
            return;
        }

        Save();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Simulation State|*.json|All Files|*.*",
            FileName = "simulation_state.json",
            InitialDirectory = CurrentProject.Path,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var sourcePath = Path.Combine(CurrentProject.Path, "simulation_state.json");
        if (!File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, dialog.FileName, overwrite: true);
    }

    [RelayCommand]
    private void Reset() => ResetSimulation();

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(CaptureBodiesSnapshot());
        RestoreBodiesFromSnapshot(_undoStack.Pop());
        MarkProjectDirty();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(CaptureBodiesSnapshot());
        RestoreBodiesFromSnapshot(_redoStack.Pop());
        MarkProjectDirty();
    }

    [RelayCommand]
    private void ClearBodies()
    {
        RecordUndoSnapshot();
        _simService.ResetScene();
        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();
        BodyInspectorVm.ClearSelection();
        _selectionContext.SelectedBodyId = -1;
        MarkProjectDirty();
    }

    [RelayCommand]
    private void ToggleBloom()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleParticles()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleWaves()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleHdr()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleReflections()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleGlowScaling()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleExplosions()
    {
        ApplyRenderSettingsToRenderer();
    }

    [RelayCommand]
    private void ToggleGrid()
    {
        ShowGrid = _renderSettings.ShowGrid;
    }

    [RelayCommand]
    private void ToggleAxes()
    {
        _renderSettings.ShowAxes = _renderSettings.ShowAxes;
    }

    [RelayCommand]
    private void ToggleOrbits()
    {
        _renderer.ShowOrbitalTrails = _renderSettings.ShowOrbits;
        _renderer.ShowPersistentOrbitPaths = _renderSettings.ShowOrbits;
    }

    [RelayCommand]
    private void ToggleTrails()
    {
        _renderSettings.EnableTrails = _renderSettings.ShowTrails;
        ShowTrails = _renderSettings.ShowTrails;
        _renderer.ShowOrbitalTrails = ShowTrails;
        _renderer.ShowPersistentOrbitPaths = ShowTrails;
    }

    [RelayCommand]
    private void ToggleVelocityVectors()
    {
        ShowVelocityArrows = _renderSettings.ShowVelocityVectors;
    }

    [RelayCommand]
    private void ToggleForceVectors()
    {
        _renderSettings.ShowForceVectors = _renderSettings.ShowForceVectors;
    }

    [RelayCommand]
    private void ToggleBoundingBoxes()
    {
        _renderSettings.ShowBoundingBoxes = _renderSettings.ShowBoundingBoxes;
    }

    [RelayCommand]
    private void ToggleInspector()
    {
        ShowInspector = _renderSettings.ShowInspector;
    }

    [RelayCommand]
    private void ToggleStats()
    {
        ShowStatusBar = _renderSettings.ShowStatistics;
    }

    [RelayCommand]
    private void Step()
    {
        _simService.Step();
        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();
        BodyInspectorVm.RefreshIfSelected();
        MarkProjectDirty();
    }

    [RelayCommand]
    private void SpeedUp()
    {
        SimulationSpeed *= 2.0;
    }

    [RelayCommand]
    private void SlowDown()
    {
        SimulationSpeed *= 0.5;
    }

    [RelayCommand]
    private void CreatePlanet()
    {
        CreateBodyPreset(BodyType.Planet, mass: 0.001, radius: 0.03, temperature: 288.0, luminosity: 0.0);
    }

    [RelayCommand]
    private void CreateStar()
    {
        CreateBodyPreset(BodyType.Star, mass: 1.2, radius: 0.12, temperature: 6200.0, luminosity: 1.0);
    }

    [RelayCommand]
    private void CreateBlackHole()
    {
        CreateBodyPreset(BodyType.BlackHole, mass: 12.0, radius: 1e-4, temperature: 5.0e4, luminosity: 0.0);
    }

    [RelayCommand]
    private void TriggerSupernova()
    {
        RecordUndoSnapshot();

        int targetBodyId = -1;
        var selectedNodeId = _sceneService.SelectionManager.SelectedEntity;
        if (selectedNodeId.HasValue)
        {
            var selectedBodyId = _sceneService.GetBodyIdForNode(selectedNodeId.Value);
            if (selectedBodyId.HasValue)
            {
                targetBodyId = selectedBodyId.Value;
            }
        }

        bool triggered = false;
        _simService.WithEngineLock(engine =>
        {
            if (targetBodyId < 0)
            {
                var candidates = engine.Bodies.Where(b => b.IsActive && (b.Type == BodyType.Star || b.Type == BodyType.NeutronStar)).ToArray();
                if (candidates.Length > 0)
                {
                    targetBodyId = candidates[0].Id;
                }
            }

            if (targetBodyId >= 0)
            {
                triggered = engine.TriggerSupernova(targetBodyId);
            }
        });

        if (!triggered)
        {
            System.Windows.MessageBox.Show(
                "No eligible star selected. Select an active star or neutron star first.",
                "Trigger Supernova",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();
        MarkProjectDirty();
    }

    [RelayCommand]
    private void TriggerBigBang()
    {
        _renderSettings.EnableBigBangMode = true;
        _simService.LogEvent(SimEventType.Warning, "Big Bang burst requested from top menu.");
    }

    [RelayCommand]
    private void ShowAbout()
    {
        System.Windows.MessageBox.Show(
            "CelestialMechanics Desktop\nHigh-fidelity orbital simulation, OpenGL rendering, and analysis tools.",
            "About CelestialMechanics",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ShowControls()
    {
        System.Windows.MessageBox.Show(
            "Controls Guide\n\nSpace: Play/Pause\nDelete: Delete selected body\nCtrl+S: Save\nCtrl+O: Open\nMouse: Orbit/Pan/Zoom in viewport",
            "Controls Guide",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ShowDiagnostics()
    {
        var diagnostics = $"Runtime Diagnostics\n\nState: {SimulationState}\nSpeed: {SimulationSpeed:F2}x\n{FpsText}\n{PhysicsTimeText}\n{RenderTimeText}\n{BodyCountText}\n{TotalEnergyText}\n{MomentumText}";
        System.Windows.MessageBox.Show(
            diagnostics,
            "Diagnostics",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private bool ShowSaveDialog()
    {

        var result = System.Windows.MessageBox.Show(
            "You have unsaved simulation changes. Save before leaving this panel?",
            "Unsaved Changes",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            Save();
        }

        return true;
    }

    private void MarkProjectDirty()
    {
        _projectService.MarkDirty();
    }

    private void MarkProjectSaved()
    {
        _projectService.MarkSaved();
    }

    private PhysicsBody[] CaptureBodiesSnapshot()
    {
        PhysicsBody[] snapshot = Array.Empty<PhysicsBody>();
        _simService.WithEngineLock(engine =>
        {
            snapshot = engine.Bodies.ToArray();
        });

        return snapshot;
    }

    private void RecordUndoSnapshot()
    {
        if (_undoStack.Count >= MaxUndoDepth)
        {
            _undoStack.Clear();
        }

        _undoStack.Push(CaptureBodiesSnapshot());
        _redoStack.Clear();
    }

    private void RestoreBodiesFromSnapshot(PhysicsBody[] bodies)
    {
        _simService.WithEngineLock(engine =>
        {
            engine.SetBodies(bodies.ToArray());
        });

        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();
        BodyInspectorVm.RefreshIfSelected();
    }

    private void CreateBodyPreset(BodyType type, double mass, double radius, double temperature, double luminosity)
    {
        RecordUndoSnapshot();

        _simService.WithEngineLock(engine =>
        {
            int nextId = engine.Bodies.Length == 0 ? 1 : engine.Bodies.Max(b => b.Id) + 1;
            double laneOffset = ((nextId % 5) - 2) * 0.8;
            var body = new PhysicsBody(
                nextId,
                mass,
                new Vec3d(laneOffset, 0.0, 0.0),
                Vec3d.Zero,
                type)
            {
                Radius = System.Math.Max(radius, 1e-4),
                Temperature = temperature,
                Luminosity = luminosity,
                HeatCapacity = type == BodyType.BlackHole ? 1.0e8 : 1.5e3,
                IsActive = true,
                IsCollidable = true,
            };
            engine.AddBody(body);
        });

        _sceneService.RepopulateFromSimulation(_simService);
        SceneOutlinerVm.Refresh();
        MarkProjectDirty();
    }

    private void ApplyRenderSettingsToRenderer()
    {
        _renderSettings.EnableTrails = _renderSettings.ShowTrails;
        _renderSettings.EnableWaves = _renderSettings.ShowGravitationalWaves;

        ShowGrid = _renderSettings.ShowGrid;
        ShowTrails = _renderSettings.ShowTrails;
        ShowVelocityArrows = _renderSettings.ShowVelocityVectors;
        ShowInspector = _renderSettings.ShowInspector;
        ShowStatusBar = _renderSettings.ShowStatistics;

        _renderer.ShowGrid = _renderSettings.ShowGrid;
        _renderer.ShowVelocityArrows = _renderSettings.ShowVelocityVectors;
        _renderer.ShowOrbitalTrails = _renderSettings.ShowTrails || _renderSettings.ShowOrbits;
        _renderer.ShowPersistentOrbitPaths = _renderSettings.ShowOrbits;
        _renderer.ShowAccretionDisks = _renderSettings.EnableParticles;
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

            engine.Reconfigure();
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

        _renderer.SetPlacementPreview(
            CreatePlacementGhostBody(PlacedX, PlacedY, PlacedZ, 1.0f),
            null,
            null,
            null);
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

        RecordUndoSnapshot();

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
        MarkProjectDirty();

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

        RecordUndoSnapshot();

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
        MarkProjectDirty();

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
        _renderer.SetPlacementPreview(
            CreatePlacementGhostBody(GhostX, GhostY, GhostZ, 0.45f),
            null,
            null,
            null);
    }

    private void UpdateRendererVelocityPreview()
    {
        var placedPos = new Vec3d(PlacedX, PlacedY, PlacedZ);
        var cursorPos = new Vec3d(VelocityEndX, VelocityEndY, VelocityEndZ);
        var velocity = ComputeVelocityFromCursor(placedPos, cursorPos);
        var preview = ComputeTrajectoryPreview(placedPos, velocity, 50)
            .ToList();

        _renderer.SetPlacementPreview(
            CreatePlacementGhostBody(PlacedX, PlacedY, PlacedZ, 1.0f),
            new System.Numerics.Vector3(PlacedX, PlacedY, PlacedZ),
            new System.Numerics.Vector3(VelocityEndX, VelocityEndY, VelocityEndZ),
            preview);
    }

    private void ClearRendererPreview()
    {
        _renderer.ClearPlacementPreview();
    }

    private RenderBody CreatePlacementGhostBody(float x, float y, float z, float alpha)
    {
        var color = SelectedBodyType switch
        {
            BodyType.Star => new System.Numerics.Vector4(1.0f, 0.82f, 0.38f, alpha),
            BodyType.Planet => new System.Numerics.Vector4(0.35f, 0.58f, 1.0f, alpha),
            BodyType.GasGiant => new System.Numerics.Vector4(0.87f, 0.7f, 0.45f, alpha),
            BodyType.RockyPlanet => new System.Numerics.Vector4(0.75f, 0.48f, 0.34f, alpha),
            BodyType.Moon => new System.Numerics.Vector4(0.76f, 0.76f, 0.82f, alpha),
            BodyType.Asteroid => new System.Numerics.Vector4(0.58f, 0.58f, 0.52f, alpha),
            BodyType.NeutronStar => new System.Numerics.Vector4(0.55f, 0.9f, 1.0f, alpha),
            BodyType.BlackHole => new System.Numerics.Vector4(0.18f, 0.2f, 0.24f, alpha),
            BodyType.Comet => new System.Numerics.Vector4(0.46f, 0.85f, 0.86f, alpha),
            _ => new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, alpha),
        };

        return new RenderBody
        {
            Id = -1,
            Position = new System.Numerics.Vector3(x, y, z),
            Radius = (float)GetPlacementRadius(),
            Color = color,
            BodyType = (int)SelectedBodyType,
            VisualParams = new System.Numerics.Vector4(1f, 0.25f, 0.2f, 0.2f),
            TextureLayer = 0,
            StarTemperatureK = SelectedBodyType == BodyType.Star ? 5772f : 0f,
            IsSelected = false,
        };
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
        RecordUndoSnapshot();
        _simService.RemoveBody(bodyId);
        if (_selectionContext.SelectedBodyId == bodyId)
        {
            _selectionContext.SelectedBodyId = -1;
        }
        _sceneService.RepopulateFromSimulation(_simService);
        BodyInspectorVm.ClearSelection();
        MarkProjectDirty();
    }

    /// <summary>
    /// Selects a body by its engine ID. Called from ViewportPanel on raycast hit.
    /// </summary>
    public void SelectBodyById(int bodyId)
    {
        _selectionContext.SelectedBodyId = bodyId;
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
        _selectionContext.SelectedBodyId = -1;
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
                    _renderer.Camera.Target = pos;
                    _renderer.Camera.Distance = dist;
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
                    _renderer.Camera.Target = pos;
                    _renderer.Camera.Distance = dist;
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
        RecordUndoSnapshot();
        _simService.Pause();
        _simService.ResetScene();

        _sceneService.RepopulateFromSimulation(_simService);
        _renderer.ClearAllHistory();
        _selectionContext.SelectedBodyId = -1;
        BodyInspectorVm.ClearSelection();
        SceneOutlinerVm.Refresh();
        CurrentMode = UiMode.Idle;
        MarkProjectDirty();
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
        _renderSettings.ShowGrid = value;
        _renderer.ShowGrid = value;
    }

    partial void OnShowVelocityArrowsChanged(bool value)
    {
        _renderSettings.ShowVelocityVectors = value;
        _renderer.ShowVelocityArrows = value;
    }

    partial void OnShowStarfieldChanged(bool value)
    {
        _renderer.ShowBackground = value;
    }

    partial void OnShowTrailsChanged(bool value)
    {
        _renderSettings.ShowTrails = value;
        _renderSettings.EnableTrails = value;
        _renderer.ShowOrbitalTrails = value;
        _renderer.ShowPersistentOrbitPaths = value;
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
