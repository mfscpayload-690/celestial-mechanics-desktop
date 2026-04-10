using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using CelestialMechanics.Renderer;
using CelestialMechanics.Simulation;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Placement;
using CelestialMechanics.Data;
using System.Numerics;
using System.Linq;

namespace CelestialMechanics.App;

public class Application
{
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
            UseAdaptiveTimestep = true,
            MaxSubstepsPerFrame = 12,
            DeterministicMode = false,
            UseParallelComputation = true,
            UseSimd = true,
            UseSoAPath = true,
            EnableAccretionDisks = true,
            MaxAccretionParticles = 4000,
            EnableJetEmission = false
        });
        _clock = new SimulationClock();

        // Set up a default 2-body orbit scenario
        SetupTwoBodyOrbit();

        // Initialize renderer
        _renderer = new GLRenderer();
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
        _imGuiOverlay = new ImGuiOverlay(_simulationEngine, _renderer);

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

        HandleInteractivePlacement();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _simulationEngine.Update(deltaTime);
        sw.Stop();
        _lastPhysicsTime = sw.Elapsed.TotalMilliseconds;

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
    }

    public void OnResize(Silk.NET.Maths.Vector2D<int> size)
    {
        _gl.Viewport(size);
    }

    public void OnClose()
    {
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
            if (_inputHandler.RightClickThisFrame && cursorInPanel)
                placement.AnchorAt(cursorWorld);
        }

        if (placement.State == PlacementState.GhostAnchoredVectorEditing)
        {
            placement.UpdateDirection(
                cursorWorld,
                _imGuiOverlay.DirectionSpeedScale,
                _imGuiOverlay.DirectionMinSpeed,
                _imGuiOverlay.DirectionMaxSpeed);

            var previewSamples = PlacementMath.BuildGravityAwarePreview(
                placement.Draft.AnchorPosition,
                placement.Draft.InitialVelocity,
                _simulationEngine.Bodies,
                steps: 48,
                dt: System.Math.Max(_simulationEngine.Config.TimeStep * 1.5, 0.005));
            placement.SetPreviewSamples(previewSamples);

            if (_inputHandler.LeftClickThisFrame && cursorInPanel)
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
}
