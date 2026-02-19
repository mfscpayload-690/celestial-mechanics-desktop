using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using CelestialMechanics.Renderer;
using CelestialMechanics.Simulation;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Data;
using System.Numerics;

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
    private int _selectedBodyId = -1;

    public Application(IWindow window) { _window = window; }

    public void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();

        // Initialize ImGui
        _imguiController = new ImGuiController(_gl, _window, _input);

        // Create simulation with default config
        _simulationEngine = new SimulationEngine();
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
        _inputHandler.Update((float)deltaTime);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _simulationEngine.Update(deltaTime);
        sw.Stop();
        _lastPhysicsTime = sw.Elapsed.TotalMilliseconds;
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
}
