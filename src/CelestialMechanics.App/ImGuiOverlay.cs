using ImGuiNET;
using CelestialMechanics.Simulation;
using CelestialMechanics.Renderer;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Placement;
using CelestialMechanics.Data;
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
    private int _selectedCategory;
    private int _selectedTemplate;
    private Vector3 _newBodyPos = Vector3.Zero;
    private Vector3 _newBodyVel = Vector3.Zero;
    private float _newBodyMass = 1.0f;
    private int _nextBodyId = 10;
    private readonly IReadOnlyList<CelestialObjectCategory> _catalog = CelestialCatalog.Categories;
    private bool _interactivePlacementEnabled;
    private readonly PlacementStateMachine _placement = new();

    // Placement velocity tuning
    private float _directionSpeedScale = 0.8f;
    private float _directionMinSpeed = 0.0f;
    private float _directionMaxSpeed = 4.0f;

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

    public PlacementStateMachine PlacementStateMachine => _placement;
    public bool InteractivePlacementEnabled => _interactivePlacementEnabled;
    public string SelectedCategoryName => _catalog.Count == 0 ? string.Empty : _catalog[_selectedCategory].Name;
    public BodyTemplate? SelectedTemplate
    {
        get
        {
            var templates = GetSelectedTemplates();
            if (templates.Count == 0)
                return null;

            _selectedTemplate = System.Math.Clamp(_selectedTemplate, 0, templates.Count - 1);
            return templates[_selectedTemplate];
        }
    }

    public double DirectionSpeedScale => _directionSpeedScale;
    public double DirectionMinSpeed => _directionMinSpeed;
    public double DirectionMaxSpeed => _directionMaxSpeed;

    public void SetInteractivePlacement(bool enabled)
    {
        _interactivePlacementEnabled = enabled;
    }

    public bool TrySpawnTemplateAt(int id, Vec3d position, Vec3d velocity, out PhysicsBody body)
    {
        body = default;
        var template = SelectedTemplate;
        if (template == null)
            return false;

        if (!Enum.TryParse<BodyType>(template.BodyType, ignoreCase: true, out var parsedType))
            parsedType = BodyType.Custom;

        body = new PhysicsBody(id, template.Mass, position, velocity, parsedType)
        {
            Radius = template.Radius,
            GravityStrength = template.GravityStrength,
            GravityRange = template.GravityRange,
            IsActive = true
        };

        return true;
    }

    public int ReserveBodyId()
    {
        return _nextBodyId++;
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
        ImGui.SetNextWindowSize(new Vector2(340, 700), ImGuiCond.FirstUseEver);
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
        ImGui.Text($"Effective dt: {_engine.CurrentState.CurrentDt:E2}");

        bool trails = _renderer.ShowOrbitalTrails;
        if (ImGui.Checkbox("Orbital Trails", ref trails))
            _renderer.ShowOrbitalTrails = trails;

        bool accretionDisks = _renderer.ShowAccretionDisks;
        if (ImGui.Checkbox("Accretion Disks", ref accretionDisks))
            _renderer.ShowAccretionDisks = accretionDisks;

        bool background = _renderer.ShowBackground;
        if (ImGui.Checkbox("Space Background", ref background))
            _renderer.ShowBackground = background;

        int trailPoints = _renderer.MaxTrailPoints;
        if (ImGui.SliderInt("Trail Length", ref trailPoints, 8, 120))
            _renderer.MaxTrailPoints = trailPoints;

        float trailSpacing = _renderer.TrailMinDistance;
        if (ImGui.SliderFloat("Trail Spacing", ref trailSpacing, 0.0005f, 0.05f, "%.4f"))
            _renderer.TrailMinDistance = trailSpacing;

        ImGui.Separator();
        ImGui.Text("Visual Tuning");

        float glow = _renderer.GlobalGlowScale;
        if (ImGui.SliderFloat("Glow Scale", ref glow, 0.0f, 2.0f, "%.2f"))
            _renderer.GlobalGlowScale = glow;

        float luminosity = _renderer.GlobalLuminosityScale;
        if (ImGui.SliderFloat("Luminosity", ref luminosity, 0.0f, 2.0f, "%.2f"))
            _renderer.GlobalLuminosityScale = luminosity;

        float saturation = _renderer.GlobalSaturation;
        if (ImGui.SliderFloat("Saturation", ref saturation, 0.0f, 2.0f, "%.2f"))
            _renderer.GlobalSaturation = saturation;

        ImGui.Separator();
        ImGui.Text("Black Hole Visual");

        bool autoQuality = _renderer.AutoBlackHoleQuality;
        if (ImGui.Checkbox("Auto BH Quality", ref autoQuality))
            _renderer.AutoBlackHoleQuality = autoQuality;

        var qualityNames = Enum.GetNames<BlackHoleVisualQuality>();
        int qualityIndex = (int)_renderer.BlackHoleQualityTier;
        if (ImGui.Combo("BH Quality Tier", ref qualityIndex, qualityNames, qualityNames.Length))
            _renderer.BlackHoleQualityTier = (BlackHoleVisualQuality)System.Math.Clamp(qualityIndex, 0, qualityNames.Length - 1);

        var presetNames = Enum.GetNames<BlackHoleVisualPreset>();
        int presetIndex = (int)_renderer.BlackHolePreset;
        if (ImGui.Combo("BH Visual Preset", ref presetIndex, presetNames, presetNames.Length))
            _renderer.BlackHolePreset = (BlackHoleVisualPreset)System.Math.Clamp(presetIndex, 0, presetNames.Length - 1);

        float ringThickness = _renderer.BlackHoleRingThickness;
        if (ImGui.SliderFloat("Ring Thickness", ref ringThickness, 0.08f, 1.0f, "%.2f"))
            _renderer.BlackHoleRingThickness = ringThickness;

        float lensStrength = _renderer.BlackHoleLensingStrength;
        if (ImGui.SliderFloat("Lensing Strength", ref lensStrength, 0.0f, 2.5f, "%.2f"))
            _renderer.BlackHoleLensingStrength = lensStrength;

        float dopplerBoost = _renderer.BlackHoleDopplerBoost;
        if (ImGui.SliderFloat("Doppler Boost", ref dopplerBoost, 0.0f, 3.0f, "%.2f"))
            _renderer.BlackHoleDopplerBoost = dopplerBoost;

        float opticalDepth = _renderer.BlackHoleOpticalDepth;
        if (ImGui.SliderFloat("Optical Depth", ref opticalDepth, 0.0f, 4.0f, "%.2f"))
            _renderer.BlackHoleOpticalDepth = opticalDepth;

        float temperatureScale = _renderer.BlackHoleTemperatureScale;
        if (ImGui.SliderFloat("Temperature Scale", ref temperatureScale, 0.25f, 3.0f, "%.2f"))
            _renderer.BlackHoleTemperatureScale = temperatureScale;

        float bloomScale = _renderer.BlackHoleBloomScale;
        if (ImGui.SliderFloat("BH Bloom/Glow", ref bloomScale, 0.0f, 2.5f, "%.2f"))
            _renderer.BlackHoleBloomScale = bloomScale;

        var debugNames = Enum.GetNames<BlackHoleDebugView>();
        int debugIndex = (int)_renderer.BlackHoleDebugMode;
        if (ImGui.Combo("BH Debug View", ref debugIndex, debugNames, debugNames.Length))
            _renderer.BlackHoleDebugMode = (BlackHoleDebugView)System.Math.Clamp(debugIndex, 0, debugNames.Length - 1);

        ImGui.Separator();
        ImGui.Text("Physics Fidelity");

        var config = _engine.Config;
        bool configDirty = false;

        bool adaptive = config.UseAdaptiveTimestep;
        if (ImGui.Checkbox("Adaptive Timestep", ref adaptive))
        {
            config.UseAdaptiveTimestep = adaptive;
            configDirty = true;
        }

        bool collisions = config.EnableCollisions;
        if (ImGui.Checkbox("Enable Collisions", ref collisions))
        {
            config.EnableCollisions = collisions;
            configDirty = true;
        }

        bool accretionPhysics = config.EnableAccretionDisks;
        if (ImGui.Checkbox("Accretion Disk Physics", ref accretionPhysics))
        {
            config.EnableAccretionDisks = accretionPhysics;
            configDirty = true;
        }

        if (config.EnableAccretionDisks)
        {
            bool jetEmission = config.EnableJetEmission;
            if (ImGui.Checkbox("Jet Emission", ref jetEmission))
            {
                config.EnableJetEmission = jetEmission;
                configDirty = true;
            }

            float jetThreshold = (float)config.AccretionJetThreshold;
            if (ImGui.SliderFloat("Jet Threshold", ref jetThreshold, 0.01f, 1.0f, "%.2f"))
            {
                config.AccretionJetThreshold = System.Math.Clamp(jetThreshold, 0.01f, 1.0f);
                configDirty = true;
            }

            ImGui.Text($"Active Accretion Particles: {_engine.ActiveAccretionParticleCount}");
        }

        bool shellTheorem = config.EnableShellTheorem;
        if (ImGui.Checkbox("Shell Theorem Gravity", ref shellTheorem))
        {
            config.EnableShellTheorem = shellTheorem;
            configDirty = true;
        }

        var collisionModeNames = Enum.GetNames<CollisionMode>();
        int collisionModeIndex = Array.IndexOf(collisionModeNames, config.CollisionMode.ToString());
        collisionModeIndex = System.Math.Max(0, collisionModeIndex);
        if (ImGui.Combo("Collision Mode", ref collisionModeIndex, collisionModeNames, collisionModeNames.Length))
        {
            config.CollisionMode = Enum.TryParse<CollisionMode>(collisionModeNames[collisionModeIndex], out var parsed)
                ? parsed
                : CollisionMode.MergeOnly;
            configDirty = true;
        }

        float restitution = (float)config.CollisionRestitution;
        if (ImGui.SliderFloat("Restitution", ref restitution, 0.0f, 1.0f, "%.2f"))
        {
            config.CollisionRestitution = System.Math.Clamp(restitution, 0.0f, 1.0f);
            configDirty = true;
        }

        float fragmentationThreshold = (float)config.FragmentationSpecificEnergyThreshold;
        if (ImGui.SliderFloat("Fragmentation Q*", ref fragmentationThreshold, 0.05f, 2.0f, "%.2f"))
        {
            config.FragmentationSpecificEnergyThreshold = System.Math.Max(0.01f, fragmentationThreshold);
            configDirty = true;
        }

        int maxSubsteps = config.MaxSubstepsPerFrame;
        if (ImGui.SliderInt("Max Substeps", ref maxSubsteps, 1, 64))
        {
            config.MaxSubstepsPerFrame = maxSubsteps;
            configDirty = true;
        }

        float minDt = (float)config.MinDt;
        if (ImGui.InputFloat("Min dt", ref minDt, 1e-5f, 1e-4f, "%.6f"))
        {
            config.MinDt = System.Math.Clamp(minDt, 1e-7f, (float)config.MaxDt);
            configDirty = true;
        }

        float maxDt = (float)config.MaxDt;
        if (ImGui.InputFloat("Max dt", ref maxDt, 1e-4f, 1e-3f, "%.5f"))
        {
            config.MaxDt = System.Math.Clamp(maxDt, (float)config.MinDt, 0.1f);
            configDirty = true;
        }

        if (configDirty)
            _engine.Reconfigure();

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
            ImGui.Text($"Collisions (step): {state.CollisionCount}");
            ImGui.Text($"Explosion Bursts:  {state.CollisionBursts.Count}");

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
        ImGui.SetNextWindowSize(new Vector2(320, 460), ImGuiCond.FirstUseEver);
        ImGui.Begin("Add Body");

        var categoryNames = _catalog.Select(c => c.Name).ToArray();
        if (categoryNames.Length == 0)
        {
            ImGui.Text("No catalog entries available.");
            ImGui.End();
            return;
        }

        _selectedCategory = System.Math.Clamp(_selectedCategory, 0, categoryNames.Length - 1);

        ImGui.Text("Category:");
        ImGui.Combo("##catalog-category", ref _selectedCategory, categoryNames, categoryNames.Length);

        var templates = GetSelectedTemplates();
        var templateNames = templates.Select(t => t.Name).ToArray();

        if (templateNames.Length == 0)
        {
            ImGui.TextDisabled("No templates in this category.");
            ImGui.End();
            return;
        }

        _selectedTemplate = System.Math.Clamp(_selectedTemplate, 0, templateNames.Length - 1);

        ImGui.Text("Celestial Object:");
        if (ImGui.Combo("##catalog-template", ref _selectedTemplate, templateNames, templateNames.Length))
            _newBodyMass = (float)templates[_selectedTemplate].Mass;

        var selectedTemplate = templates[_selectedTemplate];

        ImGui.Separator();

        ImGui.TextWrapped(_catalog[_selectedCategory].Description);

        ImGui.Separator();

        // Placement mode
        ImGui.Text("Placement Mode:");
        int placementMode = _interactivePlacementEnabled ? 1 : 0;
        ImGui.RadioButton("Manual", ref placementMode, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Interactive", ref placementMode, 1);
        _interactivePlacementEnabled = placementMode == 1;

        // Manual mode controls
        if (!_interactivePlacementEnabled)
        {
            ImGui.Text("Mass (solar masses):");
            ImGui.InputFloat("##mass", ref _newBodyMass, 0.0001f, 0.01f, "%.6f");

            ImGui.Text("Position (AU):");
            ImGui.InputFloat3("##pos", ref _newBodyPos, "%.3f");

            ImGui.Text("Velocity (AU/TU):");
            ImGui.InputFloat3("##vel", ref _newBodyVel, "%.4f");

            if (ImGui.Button("Place Body", new Vector2(-1, 30)))
            {
                var manualTemplate = selectedTemplate with { Mass = _newBodyMass };
                if (Enum.TryParse<BodyType>(manualTemplate.BodyType, ignoreCase: true, out var parsedType))
                {
                    var body = new PhysicsBody(
                        _nextBodyId,
                        manualTemplate.Mass,
                        new CelestialMechanics.Math.Vec3d(_newBodyPos.X, _newBodyPos.Y, _newBodyPos.Z),
                        new CelestialMechanics.Math.Vec3d(_newBodyVel.X, _newBodyVel.Y, _newBodyVel.Z),
                        parsedType)
                    {
                        Radius = manualTemplate.Radius,
                        GravityStrength = manualTemplate.GravityStrength,
                        GravityRange = manualTemplate.GravityRange,
                        IsActive = true
                    };

                    _engine.AddBody(body);
                    _nextBodyId++;
                    _initialEnergy = double.NaN;
                }
            }
        }
        else
        {
            ImGui.TextWrapped("Interactive placement: ghost follows cursor in simulation panel. Right click to anchor, drag to set direction, left click to confirm.");
            ImGui.InputFloat("Speed Scale", ref _directionSpeedScale, 0.05f, 0.1f, "%.3f");
            ImGui.InputFloat("Min Speed", ref _directionMinSpeed, 0.01f, 0.1f, "%.3f");
            ImGui.InputFloat("Max Speed", ref _directionMaxSpeed, 0.1f, 1.0f, "%.3f");

            _directionSpeedScale = System.Math.Clamp(_directionSpeedScale, 0.01f, 50f);
            _directionMinSpeed = System.Math.Clamp(_directionMinSpeed, 0.0f, 100f);
            _directionMaxSpeed = System.Math.Clamp(_directionMaxSpeed, _directionMinSpeed, 500f);

            ImGui.Separator();
            ImGui.Text($"Placement State: {_placement.State}");
            ImGui.Text($"Preview speed: {_placement.Draft.DirectionMagnitude:F3}");
            if (ImGui.Button("Cancel Placement", new Vector2(-1, 28)))
                _placement.Cancel();
        }

        // Preview info
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"Type: {selectedTemplate.Name}");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"Radius: {selectedTemplate.Radius:F6} AU");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"ID: {_nextBodyId}");

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
            $"Mass: {selectedTemplate.Mass:E4} M_sun");

        ImGui.End();
    }

    private IReadOnlyList<BodyTemplate> GetSelectedTemplates()
    {
        if (_catalog.Count == 0)
            return Array.Empty<BodyTemplate>();

        _selectedCategory = System.Math.Clamp(_selectedCategory, 0, _catalog.Count - 1);
        return _catalog[_selectedCategory].Templates;
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
