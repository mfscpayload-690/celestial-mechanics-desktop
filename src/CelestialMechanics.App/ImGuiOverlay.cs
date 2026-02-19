using ImGuiNET;
using CelestialMechanics.Simulation;
using CelestialMechanics.Renderer;
using CelestialMechanics.Physics.Types;
using System.Numerics;

namespace CelestialMechanics.App;

public class ImGuiOverlay
{
    private readonly SimulationEngine _engine;
    private readonly GLRenderer _renderer;
    private int _selectedIntegrator = 1; // 0=Euler, 1=Verlet, 2=RK4
    private readonly string[] _integratorNames = { "Euler", "Verlet", "RK4" };

    // Energy history for graph
    private readonly float[] _energyHistory = new float[200];
    private int _energyHistoryIndex;
    private double _initialEnergy = double.NaN;

    // Add body state
    private int _selectedTemplate;
    private Vector3 _newBodyPos = Vector3.Zero;
    private Vector3 _newBodyVel = Vector3.Zero;
    private float _newBodyMass = 1.0f;
    private int _nextBodyId = 10;
    private readonly string[] _templateNames = { "Star", "Planet", "Gas Giant", "Moon", "Asteroid", "Comet" };
    private readonly BodyType[] _templateTypes = { BodyType.Star, BodyType.Planet, BodyType.GasGiant, BodyType.Moon, BodyType.Asteroid, BodyType.Comet };
    private readonly float[] _templateMasses = { 1.0f, 0.001f, 0.01f, 0.00001f, 0.0000001f, 0.00000001f };
    private readonly float[] _templateRadii = { 0.05f, 0.02f, 0.035f, 0.008f, 0.003f, 0.002f };

    // Body inspector
    private int _selectedBodyIndex = -1;

    // FPS tracking
    private readonly float[] _fpsHistory = new float[120];
    private int _fpsHistoryIndex;
    private float _fpsAccumulator;
    private int _fpsFrameCount;
    private float _displayedFps;

    public ImGuiOverlay(SimulationEngine engine, GLRenderer renderer)
    {
        _engine = engine;
        _renderer = renderer;
        ImGui.StyleColorsDark();

        // Apply custom styling
        var style = ImGui.GetStyle();
        style.WindowRounding = 4.0f;
        style.FrameRounding = 2.0f;
        style.GrabRounding = 2.0f;
        style.Alpha = 0.95f;
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.10f, 0.12f, 0.18f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.15f, 0.18f, 0.28f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.25f, 0.40f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.28f, 0.35f, 0.55f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.35f, 0.42f, 0.65f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.12f, 0.14f, 0.20f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.40f, 0.70f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.40f, 0.70f, 1.0f, 1.0f);
    }

    public void Render(double physicsMs, double renderMs, int bodyCount)
    {
        RenderSimulationControls();
        RenderEnergyMonitor();
        RenderPerformance(physicsMs, renderMs, bodyCount);
        RenderIntegratorSelector();
        RenderAddBody();
        RenderBodyInspector();
    }

    private void RenderSimulationControls()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(250, 140), ImGuiCond.FirstUseEver);
        ImGui.Begin("Simulation Controls");

        string stateText = _engine.State.ToString();
        Vector4 stateColor = _engine.State switch
        {
            EngineState.Running => new Vector4(0.2f, 0.9f, 0.3f, 1.0f),
            EngineState.Paused => new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
            EngineState.Stopped => new Vector4(0.9f, 0.3f, 0.3f, 1.0f),
            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
        };
        ImGui.Text("State: ");
        ImGui.SameLine();
        ImGui.TextColored(stateColor, stateText);

        if (ImGui.Button(_engine.State == EngineState.Running ? "Pause" : "Play", new Vector2(70, 0)))
        {
            if (_engine.State == EngineState.Running) _engine.Pause();
            else _engine.Start();
        }
        ImGui.SameLine();
        if (ImGui.Button("Step", new Vector2(50, 0))) _engine.StepOnce();
        ImGui.SameLine();
        if (ImGui.Button("Reset", new Vector2(50, 0))) { _engine.Stop(); }

        ImGui.Separator();
        ImGui.Text($"Sim Time: {_engine.CurrentTime:F4}");
        ImGui.Text($"Time Step (dt): {_engine.Config.TimeStep:E2}");

        ImGui.End();
    }

    private void RenderEnergyMonitor()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 160), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 280), ImGuiCond.FirstUseEver);
        ImGui.Begin("Energy Monitor");

        var state = _engine.CurrentState;
        if (state != null)
        {
            double ke = state.KineticEnergy;
            double pe = state.PotentialEnergy;
            double total = state.TotalEnergy;
            double drift = state.EnergyDrift;

            // Track initial energy for drift calculation
            if (double.IsNaN(_initialEnergy) && total != 0.0)
                _initialEnergy = total;

            double driftPercent = !double.IsNaN(_initialEnergy) && _initialEnergy != 0.0
                ? System.Math.Abs((total - _initialEnergy) / _initialEnergy) * 100.0
                : drift * 100.0;

            // Color-coded energy display
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), $"Kinetic Energy:    {ke:E6}");
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.3f, 1.0f), $"Potential Energy:  {pe:E6}");
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.2f, 1.0f), $"Total Energy:      {total:E6}");

            // Drift indicator with color: green (<0.01%), yellow (<1%), red (>1%)
            Vector4 driftColor = driftPercent < 0.01 ? new Vector4(0.2f, 0.9f, 0.3f, 1.0f) :
                                 driftPercent < 1.0  ? new Vector4(1.0f, 0.8f, 0.2f, 1.0f) :
                                                       new Vector4(0.9f, 0.3f, 0.3f, 1.0f);
            ImGui.TextColored(driftColor, $"Energy Drift:      {driftPercent:F6}%%");

            // Momentum display
            var momentum = state.TotalMomentum;
            double momentumMag = System.Math.Sqrt(momentum.X * momentum.X + momentum.Y * momentum.Y + momentum.Z * momentum.Z);
            ImGui.Text($"Total Momentum:    {momentumMag:E6}");

            // Update energy history for scrolling graph
            _energyHistory[_energyHistoryIndex] = (float)total;
            _energyHistoryIndex = (_energyHistoryIndex + 1) % _energyHistory.Length;

            // Scrolling energy graph
            ImGui.Separator();
            ImGui.Text("Total Energy History:");

            // Build the overlay text for the graph
            string overlayText = $"E = {total:E4}";
            ImGui.PlotLines("##energy_graph", ref _energyHistory[0], _energyHistory.Length,
                _energyHistoryIndex, overlayText, float.MaxValue, float.MaxValue, new Vector2(300, 80));
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "No simulation data available.");
            ImGui.Text("Start the simulation to see energy data.");
        }

        ImGui.End();
    }

    private void RenderPerformance(double physicsMs, double renderMs, int bodyCount)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 450), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(250, 180), ImGuiCond.FirstUseEver);
        ImGui.Begin("Performance");

        // Calculate FPS from render time
        float frameDt = ImGui.GetIO().DeltaTime;
        _fpsAccumulator += frameDt;
        _fpsFrameCount++;
        if (_fpsAccumulator >= 0.5f)
        {
            _displayedFps = _fpsFrameCount / _fpsAccumulator;
            _fpsAccumulator = 0;
            _fpsFrameCount = 0;
        }

        // FPS history
        _fpsHistory[_fpsHistoryIndex] = _displayedFps;
        _fpsHistoryIndex = (_fpsHistoryIndex + 1) % _fpsHistory.Length;

        // Color-code FPS
        Vector4 fpsColor = _displayedFps >= 55 ? new Vector4(0.2f, 0.9f, 0.3f, 1.0f) :
                           _displayedFps >= 30 ? new Vector4(1.0f, 0.8f, 0.2f, 1.0f) :
                                                 new Vector4(0.9f, 0.3f, 0.3f, 1.0f);
        ImGui.TextColored(fpsColor, $"FPS: {_displayedFps:F1}");

        ImGui.Text($"Body Count: {bodyCount}");
        ImGui.Separator();
        ImGui.Text($"Physics:  {physicsMs:F3} ms");
        ImGui.Text($"Render:   {renderMs:F3} ms");
        ImGui.Text($"Frame dt: {frameDt * 1000.0f:F3} ms");

        // Progress bar for physics budget (target: 16.67ms for 60fps)
        float physicsBudget = (float)(physicsMs / 16.67);
        ImGui.Separator();
        ImGui.Text("Frame Budget Usage:");
        ImGui.ProgressBar(System.Math.Min(physicsBudget, 1.0f), new Vector2(-1, 0),
            $"{physicsBudget * 100.0f:F1}%%");

        ImGui.End();
    }

    private void RenderIntegratorSelector()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 640), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(250, 100), ImGuiCond.FirstUseEver);
        ImGui.Begin("Integrator");

        // Sync the selected integrator with the engine's current setting
        string currentName = _engine.GetIntegratorName();
        for (int i = 0; i < _integratorNames.Length; i++)
        {
            if (_integratorNames[i] == currentName)
            {
                _selectedIntegrator = i;
                break;
            }
        }

        ImGui.Text("Numerical Integrator:");
        if (ImGui.Combo("##integrator", ref _selectedIntegrator, _integratorNames, _integratorNames.Length))
        {
            _engine.SetIntegrator(_integratorNames[_selectedIntegrator]);
            // Reset initial energy tracking when switching integrators
            _initialEnergy = double.NaN;
        }

        ImGui.TextWrapped(_selectedIntegrator switch
        {
            0 => "Euler: Fast but low accuracy. Energy drift will be significant.",
            1 => "Verlet: Symplectic, excellent energy conservation. Recommended.",
            2 => "RK4: High accuracy per step but not symplectic. Good for short runs.",
            _ => ""
        });

        ImGui.End();
    }

    private void RenderAddBody()
    {
        ImGui.SetNextWindowPos(new Vector2(1330, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(260, 340), ImGuiCond.FirstUseEver);
        ImGui.Begin("Add Body");

        // Template selector
        ImGui.Text("Body Template:");
        if (ImGui.Combo("##template", ref _selectedTemplate, _templateNames, _templateNames.Length))
        {
            // Auto-populate mass when template changes
            _newBodyMass = _templateMasses[_selectedTemplate];
        }

        ImGui.Separator();

        // Mass input
        ImGui.Text("Mass (solar masses):");
        ImGui.InputFloat("##mass", ref _newBodyMass, 0.0001f, 0.01f, "%.6f");

        // Position input
        ImGui.Text("Position (AU):");
        ImGui.InputFloat3("##pos", ref _newBodyPos, "%.3f");

        // Velocity input
        ImGui.Text("Velocity (AU/TU):");
        ImGui.InputFloat3("##vel", ref _newBodyVel, "%.4f");

        ImGui.Separator();

        // Preview info
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"Type: {_templateNames[_selectedTemplate]}");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"Radius: {_templateRadii[_selectedTemplate]:F4}");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"ID: {_nextBodyId}");

        ImGui.Separator();

        // Add button
        if (ImGui.Button("Add Body", new Vector2(-1, 30)))
        {
            var position = new CelestialMechanics.Math.Vec3d(_newBodyPos.X, _newBodyPos.Y, _newBodyPos.Z);
            var velocity = new CelestialMechanics.Math.Vec3d(_newBodyVel.X, _newBodyVel.Y, _newBodyVel.Z);

            var body = new PhysicsBody(
                _nextBodyId,
                _newBodyMass,
                position,
                velocity,
                _templateTypes[_selectedTemplate])
            {
                Radius = _templateRadii[_selectedTemplate],
                GravityStrength = 60,
                GravityRange = 8,
                IsActive = true
            };

            _engine.AddBody(body);
            _nextBodyId++;

            // Reset initial energy tracking since system changed
            _initialEnergy = double.NaN;
        }

        ImGui.End();
    }

    private void RenderBodyInspector()
    {
        ImGui.SetNextWindowPos(new Vector2(1330, 360), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(260, 370), ImGuiCond.FirstUseEver);
        ImGui.Begin("Body Inspector");

        var bodies = _engine.Bodies;
        if (bodies == null || bodies.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "No bodies in simulation.");
            ImGui.End();
            return;
        }

        // Body selector list
        ImGui.Text($"Bodies ({bodies.Length}):");
        string[] bodyLabels = new string[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodyLabels[i] = $"[{bodies[i].Id}] {bodies[i].Type} (m={bodies[i].Mass:G4})";
        }

        ImGui.ListBox("##bodies", ref _selectedBodyIndex, bodyLabels, bodyLabels.Length, 4);

        if (_selectedBodyIndex >= 0 && _selectedBodyIndex < bodies.Length)
        {
            ref var body = ref bodies[_selectedBodyIndex];

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Body #{body.Id} - {body.Type}");

            ImGui.Separator();

            // Properties
            ImGui.Text("Properties:");
            ImGui.BulletText($"Mass:    {body.Mass:E4} M_sun");
            ImGui.BulletText($"Radius:  {body.Radius:F4} AU");
            ImGui.BulletText($"Active:  {body.IsActive}");

            ImGui.Separator();

            // Position
            ImGui.Text("Position (AU):");
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), $"  X: {body.Position.X:F6}");
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"  Y: {body.Position.Y:F6}");
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 1.0f, 1.0f), $"  Z: {body.Position.Z:F6}");

            // Velocity
            double speed = body.Velocity.Length;
            ImGui.Text($"Velocity (|v| = {speed:F6}):");
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), $"  Vx: {body.Velocity.X:F6}");
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"  Vy: {body.Velocity.Y:F6}");
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 1.0f, 1.0f), $"  Vz: {body.Velocity.Z:F6}");

            // Acceleration
            double accelMag = body.Acceleration.Length;
            ImGui.Text($"Acceleration (|a| = {accelMag:E4}):");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                $"  ({body.Acceleration.X:E3}, {body.Acceleration.Y:E3}, {body.Acceleration.Z:E3})");

            ImGui.Separator();

            // Remove body button
            if (ImGui.Button("Remove Body", new Vector2(-1, 25)))
            {
                _engine.RemoveBody(body.Id);
                _selectedBodyIndex = -1;
                _initialEnergy = double.NaN;
            }
        }
        else
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Select a body to inspect.");
        }

        ImGui.End();
    }
}
