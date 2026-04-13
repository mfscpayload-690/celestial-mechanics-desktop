using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using CelestialMechanics.Renderer;
using CelestialMechanics.Simulation;
using CelestialMechanics.Simulation.Analysis;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Placement;
using CelestialMechanics.Data;
using System.Numerics;
using System.Linq;
using System;

namespace CelestialMechanics.App;

public class Application
{
    [Flags]
    private enum WorkflowEvent
    {
        None = 0,
        SelectionChanged = 1,
        ModeChanged = 2,
    }

    private readonly IWindow _window;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _imguiController = null!;
    private GLRenderer _renderer = null!;
    private SimulationEngine _simulationEngine = null!;
    private SimulationClock _clock = null!;
    private InputHandler _inputHandler = null!;
    private ImGuiOverlay _imGuiOverlay = null!;

    // Performance tracking
    private double _lastPhysicsTime;
    private double _lastRenderTime;
    private RuntimeDiagnosticsLogger? _runtimeDiagnostics;
    private readonly SelectionContext _selectionContext = new();
    private int _autoCentralBodyId = -1;
    private int _lastSelectionBodyId = -1;
    private ApplicationMode _lastMode = ApplicationMode.Simulation;
    private WorkflowEvent _pendingWorkflowEvents = WorkflowEvent.None;
    private bool _followSelectedBody;
    private ApplicationMode _mode = ApplicationMode.Simulation;
    private bool _windowShown;

    public Application(IWindow window) { _window = window; }

    public void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();

        // Initialize ImGui
        _imguiController = new ImGuiController(_gl, _window, _input);

        // Create simulation config tuned for interactive desktop behavior.
        _simulationEngine = new SimulationEngine(new PhysicsConfig
        {
            EnableCollisions = true,
            CollisionMode = CollisionMode.Realistic,
            EnableShellTheorem = true,
            UseAdaptiveTimestep = true,
            MaxSubstepsPerFrame = 12,
            DeterministicMode = false,
            UseParallelComputation = true,
            UseSimd = true,
            UseSoAPath = true,
            EnableAccretionDisks = true,
            MaxAccretionParticles = 12000,
            EnableJetEmission = false
        });
        _clock = new SimulationClock();

        // Set up a default 2-body orbit scenario
        SetupTwoBodyOrbit();

        // Initialize renderer
        _renderer = new GLRenderer(_selectionContext);
        _renderer.Initialize(_gl);

        // Input handling
        _inputHandler = new InputHandler(_input, _renderer.Camera);
        _inputHandler.OnToggleSimulation += () => {
            if (_simulationEngine.State == EngineState.Running)
                _simulationEngine.Pause();
            else
                _simulationEngine.Start();
        };
        _inputHandler.OnStepSimulation += () => _simulationEngine.StepOnce();
        _inputHandler.OnResetSimulation += () => { _simulationEngine.Stop(); SetupTwoBodyOrbit(); };
        _inputHandler.OnCancelPlacement += () => _imGuiOverlay?.PlacementStateMachine.Cancel();

        // ImGui overlay
        _imGuiOverlay = new ImGuiOverlay(_simulationEngine, _renderer, _selectionContext, () =>
        {
            _simulationEngine.Stop();
            SetupTwoBodyOrbit();
        });
        _imGuiOverlay.Mode = _mode;

        string diagnosticsEnv = Environment.GetEnvironmentVariable("CM_MODULE1_DIAGNOSTICS") ?? "1";
        bool enableDiagnostics = !string.Equals(diagnosticsEnv, "0", StringComparison.Ordinal);
        _runtimeDiagnostics = new RuntimeDiagnosticsLogger(enableDiagnostics);

        _clock.Start();
        _simulationEngine.Start();

        // GL state
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.ClearColor(0.04f, 0.04f, 0.06f, 1.0f); // #0a0a0f
    }

    private void SetupTwoBodyOrbit()
    {
        // Two equal-mass bodies in a circular orbit
        // For circular orbit of two equal masses M at separation 2r:
        // v = sqrt(G*M / (4*r)) for each body
        double mass = 1.0;  // solar mass
        double separation = 2.0; // AU
        double r = separation / 2.0;
        double v = System.Math.Sqrt(CelestialMechanics.Math.PhysicalConstants.G_Sim * mass / (4.0 * r));

        var bodies = new PhysicsBody[]
        {
            new(0, mass, new CelestialMechanics.Math.Vec3d(r, 0, 0), new CelestialMechanics.Math.Vec3d(0, 0, v), BodyType.Star)
            { Radius = 0.05, GravityStrength = 60, GravityRange = 8, IsActive = true },
            new(1, mass, new CelestialMechanics.Math.Vec3d(-r, 0, 0), new CelestialMechanics.Math.Vec3d(0, 0, -v), BodyType.Star)
            { Radius = 0.05, GravityStrength = 60, GravityRange = 8, IsActive = true },
        };
        _simulationEngine.SetBodies(bodies);
    }

    public void OnUpdate(double deltaTime)
    {
        _clock.Tick();
        _inputHandler.BlockCameraMouseControls = _imGuiOverlay.InteractivePlacementEnabled;
        _inputHandler.Update((float)deltaTime);

        _mode = _imGuiOverlay.Mode;

        if (_mode != _lastMode)
            RaiseWorkflowEvent(WorkflowEvent.ModeChanged);

        _followSelectedBody = _imGuiOverlay.FollowSelectedBody;

        HandleInteractivePlacement();
        HandleBodySelection();

        if (_selectionContext.SelectedBodyId != _lastSelectionBodyId)
            RaiseWorkflowEvent(WorkflowEvent.SelectionChanged);

        if (_pendingWorkflowEvents != WorkflowEvent.None)
            SyncSelectionWorkflow();

        UpdateSelectedOrbitAnalysis();
        UpdateSelectedEventDetection();
        ApplyCameraFollow();

        _renderer.UseAnalysisPrediction = _mode == ApplicationMode.Analysis;
        _renderer.HighPrecisionPrediction = _imGuiOverlay.HighPrecisionPrediction;

        double modeDeltaTime = _mode == ApplicationMode.Analysis
            ? deltaTime * 0.1
            : deltaTime;
        double scaledDeltaTime = modeDeltaTime * _imGuiOverlay.TimeScaleMultiplier;
        _simulationEngine.Config.TimeFlowSubstepBoost = System.Math.Clamp(_imGuiOverlay.TimeScaleMultiplier, 1.0, 64.0);
        _imGuiOverlay.ApplyEnvironmentEffects(scaledDeltaTime);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _simulationEngine.Update(scaledDeltaTime);
        sw.Stop();
        _lastPhysicsTime = sw.Elapsed.TotalMilliseconds;

        var state = _simulationEngine.CurrentState;
        var config = _simulationEngine.Config;
        _runtimeDiagnostics?.TryWrite(new RuntimeDiagnosticsSnapshot(
            TimestampUtc: DateTime.UtcNow,
            FrameDeltaTime: deltaTime,
            ScaledDeltaTime: scaledDeltaTime,
            PhysicsMs: _lastPhysicsTime,
            RenderMs: _lastRenderTime,
            EngineState: _simulationEngine.State.ToString(),
            SolverBackend: _simulationEngine.LastSolverBackend,
            Integrator: config.IntegratorName,
            SimTime: _simulationEngine.CurrentTime,
            SolverDt: state.CurrentDt,
            BodyCount: state.BodyCount,
            ActiveBodyCount: state.ActiveBodyCount,
            CollisionCount: state.CollisionCount,
            CollisionBursts: state.CollisionBursts.Count,
            UseSoAPath: config.UseSoAPath,
            UseBarnesHut: config.UseBarnesHut,
            DeterministicMode: config.DeterministicMode,
            UseParallel: config.UseParallelComputation,
            UseSimd: config.UseSimd,
            EnableAdaptiveTimestep: config.UseAdaptiveTimestep,
            EnableCollisions: config.EnableCollisions,
            TimeFlowSlider: _imGuiOverlay.TimeFlowValue,
            TimeScaleMultiplier: _imGuiOverlay.TimeScaleMultiplier,
            ShowSimulationControlsPanel: _imGuiOverlay.ShowSimulationControlsPanel,
            ShowEnergyMonitorPanel: _imGuiOverlay.ShowEnergyMonitorPanel,
            ShowPerformancePanel: _imGuiOverlay.ShowPerformancePanel,
            ShowIntegratorPanel: _imGuiOverlay.ShowIntegratorPanel,
            ShowAddBodyPanel: _imGuiOverlay.ShowAddBodyPanel,
            ShowBodyInspectorPanel: _imGuiOverlay.ShowBodyInspectorPanel,
            ShowGrid: _renderer.ShowGrid,
            ShowOrbitalTrails: _renderer.ShowOrbitalTrails,
            ShowBackground: _renderer.ShowBackground,
            ShowAccretionDisks: _renderer.ShowAccretionDisks,
            EnableAlbedoTextureMaps: _renderer.EnableAlbedoTextureMaps,
            EnableStarDrivenLighting: _renderer.EnableStarDrivenLighting,
            EnableRayTracedShadows: _renderer.EnableRayTracedShadows,
            GlobalLuminosityScale: _renderer.GlobalLuminosityScale,
            GlobalGlowScale: _renderer.GlobalGlowScale,
            GlobalSaturation: _renderer.GlobalSaturation));

        _inputHandler.EndFrame();
    }

    public void OnRender(double deltaTime)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _renderer.UpdateFromSimulation(_simulationEngine);
        _renderer.Render((float)deltaTime, _window.Size.X, _window.Size.Y);

        sw.Stop();
        _lastRenderTime = sw.Elapsed.TotalMilliseconds;

        // ImGui overlay
        _imguiController.Update((float)deltaTime);
        _imGuiOverlay.Render(_lastPhysicsTime, _lastRenderTime, _simulationEngine.Bodies?.Length ?? 0);
        _imguiController.Render();

        if (!_windowShown)
        {
            _window.IsVisible = true;
            _windowShown = true;
        }
    }

    public void OnResize(Silk.NET.Maths.Vector2D<int> size)
    {
        _gl.Viewport(size);
    }

    public void OnClose()
    {
        _runtimeDiagnostics?.Dispose();
        _renderer?.Dispose();
        _imguiController?.Dispose();
        _gl?.Dispose();
    }

    private void HandleInteractivePlacement()
    {
        var placement = _imGuiOverlay.PlacementStateMachine;
        var selectedTemplate = _imGuiOverlay.SelectedTemplate;

        if (!_imGuiOverlay.InteractivePlacementEnabled || selectedTemplate == null)
        {
            if (placement.State != PlacementState.Idle)
                placement.Reset();

            _renderer.ClearPlacementPreview();
            return;
        }

        bool cursorInPanel = !ImGuiNET.ImGui.GetIO().WantCaptureMouse;
        if (!TryGetCursorWorldPosition(out var cursorWorld))
            cursorInPanel = false;

        if (placement.State == PlacementState.Idle ||
            placement.State == PlacementState.PlacementCanceled ||
            placement.State == PlacementState.PlacementCommitted)
        {
            if (cursorInPanel)
            {
                placement.BeginGhostFollow(
                    _imGuiOverlay.SelectedCategoryName,
                    selectedTemplate.Name,
                    cursorWorld,
                    cursorInPanel);
            }
        }

        if (placement.State == PlacementState.GhostFollow)
        {
            placement.UpdateGhostPosition(cursorWorld, cursorInPanel);
            if (_inputHandler.LeftClickThisFrame && cursorInPanel)
                placement.AnchorAt(cursorWorld);
        }

        if (placement.State == PlacementState.GhostAnchoredVectorEditing)
        {
            placement.UpdateDirection(
                cursorWorld,
                _imGuiOverlay.DirectionSpeedScale,
                _imGuiOverlay.DirectionMinSpeed,
                _imGuiOverlay.DirectionMaxSpeed);

            if (Enum.TryParse<BodyType>(selectedTemplate.BodyType, true, out var previewType) == false)
                previewType = BodyType.Custom;

            const int previewBodyId = int.MinValue + 1337;
            var previewBodies = new List<PhysicsBody>(_simulationEngine.Bodies.Length + 1);
            previewBodies.AddRange(_simulationEngine.Bodies);
            previewBodies.Add(new PhysicsBody(
                previewBodyId,
                selectedTemplate.Mass,
                placement.Draft.AnchorPosition,
                placement.Draft.InitialVelocity,
                previewType)
            {
                Radius = selectedTemplate.Radius,
                GravityStrength = selectedTemplate.GravityStrength,
                GravityRange = selectedTemplate.GravityRange,
                IsActive = true,
                IsCollidable = true
            });

            var previewSamples = TrajectoryPredictor.PredictBodyTrajectory(
                previewBodies,
                previewBodyId,
                _simulationEngine.Config,
                steps: 64,
                dt: System.Math.Max(_simulationEngine.Config.TimeStep, 0.001));

            placement.SetPreviewSamples(previewSamples);

            if (_inputHandler.LeftReleasedThisFrame && cursorInPanel)
                placement.Commit();
        }

        if (placement.State == PlacementState.PlacementCommitted)
        {
            int bodyId = _imGuiOverlay.ReserveBodyId();
            if (_imGuiOverlay.TrySpawnTemplateAt(bodyId, placement.Draft.AnchorPosition, placement.Draft.InitialVelocity, out var body))
                _simulationEngine.AddBody(body);

            placement.Reset();
            _renderer.ClearPlacementPreview();
            return;
        }

        if (placement.State == PlacementState.PlacementCanceled)
        {
            placement.Reset();
            _renderer.ClearPlacementPreview();
            return;
        }

        RenderBody? ghost = null;
        Vector3? directionStart = null;
        Vector3? directionEnd = null;
        List<Vector3>? preview = null;

        if (placement.State == PlacementState.GhostFollow || placement.State == PlacementState.GhostAnchoredVectorEditing)
        {
            if (Enum.TryParse<BodyType>(selectedTemplate.BodyType, true, out var parsedType) == false)
                parsedType = BodyType.Custom;

            var baseColor = selectedTemplate.Color;
            ghost = new RenderBody
            {
                Id = -1,
                Position = new Vector3((float)placement.Draft.GhostPosition.X, (float)placement.Draft.GhostPosition.Y, (float)placement.Draft.GhostPosition.Z),
                Radius = (float)System.Math.Max(selectedTemplate.Radius, 0.01),
                Color = new Vector4(baseColor.X, baseColor.Y, baseColor.Z, 0.35f),
                BodyType = (int)parsedType,
            };
        }

        if (placement.State == PlacementState.GhostAnchoredVectorEditing)
        {
            directionStart = new Vector3(
                (float)placement.Draft.AnchorPosition.X,
                (float)placement.Draft.AnchorPosition.Y,
                (float)placement.Draft.AnchorPosition.Z);
            directionEnd = new Vector3(
                (float)placement.Draft.DirectionEnd.X,
                (float)placement.Draft.DirectionEnd.Y,
                (float)placement.Draft.DirectionEnd.Z);

            preview = placement.Draft.PreviewTrajectorySamples
                .Select(p => new Vector3((float)p.X, (float)p.Y, (float)p.Z))
                .ToList();
        }

        _renderer.SetPlacementPreview(ghost, directionStart, directionEnd, preview);
    }

    private bool TryGetCursorWorldPosition(out CelestialMechanics.Math.Vec3d world)
    {
        var mouse = _inputHandler.MousePosition;
        int width = _window.Size.X;
        int height = _window.Size.Y;

        if (width <= 0 || height <= 0)
        {
            world = CelestialMechanics.Math.Vec3d.Zero;
            return false;
        }

        float x = (2.0f * mouse.X / width) - 1.0f;
        float y = 1.0f - (2.0f * mouse.Y / height);

        var view = _renderer.Camera.GetViewMatrix();
        var projection = _renderer.Camera.GetProjectionMatrix(width / (float)height);
        var viewProj = view * projection;

        if (!Matrix4x4.Invert(viewProj, out var invViewProj))
        {
            world = CelestialMechanics.Math.Vec3d.Zero;
            return false;
        }

        var near4 = Vector4.Transform(new Vector4(x, y, 0f, 1f), invViewProj);
        var far4 = Vector4.Transform(new Vector4(x, y, 1f, 1f), invViewProj);

        if (System.Math.Abs(near4.W) < 1e-8 || System.Math.Abs(far4.W) < 1e-8)
        {
            world = CelestialMechanics.Math.Vec3d.Zero;
            return false;
        }

        var nearPoint = new Vector3(near4.X / near4.W, near4.Y / near4.W, near4.Z / near4.W);
        var farPoint = new Vector3(far4.X / far4.W, far4.Y / far4.W, far4.Z / far4.W);
        var rayDir = Vector3.Normalize(farPoint - nearPoint);

        if (System.Math.Abs(rayDir.Y) < 1e-6)
        {
            world = new CelestialMechanics.Math.Vec3d(nearPoint.X, nearPoint.Y, nearPoint.Z);
            return true;
        }

        // Intersect with simulation plane y=0.
        float t = -nearPoint.Y / rayDir.Y;
        var hit = nearPoint + rayDir * t;

        world = new CelestialMechanics.Math.Vec3d(hit.X, hit.Y, hit.Z);
        return true;
    }

    private void HandleBodySelection()
    {
        if (_imGuiOverlay.InteractivePlacementEnabled)
            return;

        if (_inputHandler.LeftClickThisFrame && !ImGuiNET.ImGui.GetIO().WantCaptureMouse)
        {
            if (TryPickBodyId(out int pickedId))
                _selectionContext.SelectedBodyId = pickedId;
        }
    }

    private void RaiseWorkflowEvent(WorkflowEvent workflowEvent)
    {
        _pendingWorkflowEvents |= workflowEvent;
    }

    private void SyncSelectionWorkflow()
    {
        WorkflowEvent events = _pendingWorkflowEvents;
        _pendingWorkflowEvents = WorkflowEvent.None;

        if (events == WorkflowEvent.None)
            return;

        if ((events & WorkflowEvent.SelectionChanged) != 0)
        {
            _autoCentralBodyId = CentralBodyDetector.FindCentralBody(_simulationEngine.Bodies);
            if (_imGuiOverlay.UseAutoCentralBody)
                _imGuiOverlay.SelectedCentralBodyId = _autoCentralBodyId;
        }

        // Orbit analysis recompute and UI binding refresh.
        UpdateSelectedOrbitAnalysis();
        _imGuiOverlay.SelectedBodyId = _selectionContext.SelectedBodyId;

        // Prediction refresh is selection/mode driven.
        _renderer.RequestPredictionRefresh();
        _renderer.UseAnalysisPrediction = _mode == ApplicationMode.Analysis;
        _renderer.HighPrecisionPrediction = _imGuiOverlay.HighPrecisionPrediction;

        // Camera target update when selection changes.
        if ((events & WorkflowEvent.SelectionChanged) != 0 &&
            _selectionContext.HasSelection &&
            TryGetBodyPosition(_selectionContext.SelectedBodyId, out var position))
        {
            _renderer.Camera.Target = position;
        }

        _lastSelectionBodyId = _selectionContext.SelectedBodyId;
        _lastMode = _mode;
    }

    private void UpdateSelectedOrbitAnalysis()
    {
        _imGuiOverlay.SelectedOrbitData = null;
        _imGuiOverlay.OrbitReferenceBodyId = -1;

        if (!_selectionContext.HasSelection)
            return;

        if (!TryGetBodyById(_selectionContext.SelectedBodyId, out var selectedBody))
            return;

        int referenceId = _imGuiOverlay.UseAutoCentralBody
            ? _autoCentralBodyId
            : _imGuiOverlay.SelectedCentralBodyId;

        PhysicsBody referenceBody = default;

        bool hasReference = referenceId >= 0 &&
                            referenceId != _selectionContext.SelectedBodyId &&
                            TryGetBodyById(referenceId, out referenceBody);

        if (!hasReference && !TryGetOrbitReferenceBody(_selectionContext.SelectedBodyId, out referenceBody))
            return;

        if (_imGuiOverlay.UseAutoCentralBody)
            _imGuiOverlay.SelectedCentralBodyId = referenceBody.Id;

        _imGuiOverlay.SelectedOrbitData = OrbitCalculator.ComputeOrbit(selectedBody, referenceBody);
        _imGuiOverlay.OrbitReferenceBodyId = referenceBody.Id;
    }

    private void UpdateSelectedEventDetection()
    {
        _imGuiOverlay.ActiveEventWarning = null;

        if (_mode != ApplicationMode.Analysis || !_selectionContext.HasSelection)
            return;

        if (!TryGetBodyById(_selectionContext.SelectedBodyId, out var selectedBody))
            return;

        int referenceId = _imGuiOverlay.OrbitReferenceBodyId;
        if (referenceId < 0 || !TryGetBodyById(referenceId, out var referenceBody))
            return;

        _imGuiOverlay.ActiveEventWarning = EventDetector.DetectPrimaryEvent(
            selectedBody,
            referenceBody,
            _imGuiOverlay.SelectedOrbitData);
    }

    private void ApplyCameraFollow()
    {
        if (!_followSelectedBody || !_selectionContext.HasSelection)
            return;

        if (!TryGetBodyPosition(_selectionContext.SelectedBodyId, out var position))
            return;

        _renderer.Camera.Target = Vector3.Lerp(_renderer.Camera.Target, position, 0.15f);
    }

    private bool TryPickBodyId(out int bodyId)
    {
        bodyId = -1;

        if (!TryGetCursorRay(out var rayOrigin, out var rayDir))
            return false;

        var bodies = _simulationEngine.Bodies;
        if (bodies == null || bodies.Length == 0)
            return false;

        float bestT = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (!body.IsActive)
                continue;

            var center = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);
            var toCenter = center - rayOrigin;
            float t = Vector3.Dot(toCenter, rayDir);
            if (t < 0.0f)
                continue;

            var closest = rayOrigin + rayDir * t;
            float distSq = Vector3.DistanceSquared(closest, center);
            float pickRadius = System.MathF.Max((float)body.Radius * 1.35f, 0.08f);
            if (distSq > pickRadius * pickRadius)
                continue;

            if (t < bestT)
            {
                bestT = t;
                bodyId = body.Id;
            }
        }

        return bodyId >= 0;
    }

    private bool TryGetCursorRay(out Vector3 rayOrigin, out Vector3 rayDir)
    {
        rayOrigin = Vector3.Zero;
        rayDir = Vector3.UnitZ;

        var mouse = _inputHandler.MousePosition;
        int width = _window.Size.X;
        int height = _window.Size.Y;

        if (width <= 0 || height <= 0)
            return false;

        float x = (2.0f * mouse.X / width) - 1.0f;
        float y = 1.0f - (2.0f * mouse.Y / height);

        var view = _renderer.Camera.GetViewMatrix();
        var projection = _renderer.Camera.GetProjectionMatrix(width / (float)height);
        var viewProj = view * projection;

        if (!Matrix4x4.Invert(viewProj, out var invViewProj))
            return false;

        var near4 = Vector4.Transform(new Vector4(x, y, 0f, 1f), invViewProj);
        var far4 = Vector4.Transform(new Vector4(x, y, 1f, 1f), invViewProj);

        if (System.Math.Abs(near4.W) < 1e-8 || System.Math.Abs(far4.W) < 1e-8)
            return false;

        var nearPoint = new Vector3(near4.X / near4.W, near4.Y / near4.W, near4.Z / near4.W);
        var farPoint = new Vector3(far4.X / far4.W, far4.Y / far4.W, far4.Z / far4.W);

        var dir = farPoint - nearPoint;
        if (dir.LengthSquared() < 1e-12f)
            return false;

        rayOrigin = nearPoint;
        rayDir = Vector3.Normalize(dir);
        return true;
    }

    private bool TryGetBodyPosition(int bodyId, out Vector3 position)
    {
        var bodies = _simulationEngine.Bodies;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive || bodies[i].Id != bodyId)
                continue;

            position = new Vector3((float)bodies[i].Position.X, (float)bodies[i].Position.Y, (float)bodies[i].Position.Z);
            return true;
        }

        position = Vector3.Zero;
        return false;
    }

    private bool TryGetBodyById(int bodyId, out PhysicsBody body)
    {
        var bodies = _simulationEngine.Bodies;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive || bodies[i].Id != bodyId)
                continue;

            body = bodies[i];
            return true;
        }

        body = default;
        return false;
    }

    private bool TryGetOrbitReferenceBody(int selectedBodyId, out PhysicsBody referenceBody)
    {
        var bodies = _simulationEngine.Bodies;
        int bestIndex = -1;
        double bestMass = double.NegativeInfinity;

        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (!body.IsActive || body.Id == selectedBodyId)
                continue;

            if (body.Mass <= bestMass)
                continue;

            bestMass = body.Mass;
            bestIndex = i;
        }

        if (bestIndex >= 0)
        {
            referenceBody = bodies[bestIndex];
            return true;
        }

        referenceBody = default;
        return false;
    }
}
