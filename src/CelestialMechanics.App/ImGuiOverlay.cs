using ImGuiNET;
using CelestialMechanics.Simulation;
using CelestialMechanics.Renderer;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Placement;
using CelestialMechanics.Data;
using System.Numerics;
using System.Globalization;

namespace CelestialMechanics.App;

public class ImGuiOverlay
{
    private enum ControlMenuMode
    {
        None,
        Add,
        Simulation
    }

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
    private readonly IReadOnlyList<AddMenuEntry> _addEntries = CelestialAddMenuCatalog.Entries;
    private bool _interactivePlacementEnabled;
    private readonly PlacementStateMachine _placement = new();

    // Bottom control panel state
    private ControlMenuMode _menuMode = ControlMenuMode.None;
    private float _timeFlowValue = 100f; // 100 => real time baseline
    private float _manualTimeFlowInput = 100f;
    private bool _showSimulationControls = true;
    private bool _showEnergyMonitor = true;
    private bool _showPerformance = true;
    private bool _showIntegrator = true;
    private bool _showAddBody = true;
    private bool _showBodyInspector = true;

    // Add menu hierarchy state
    private int _selectedAddTopCategory;
    private int _selectedAddCategory;
    private int _selectedAddSubCategory;
    private int _selectedAddObject;

    // Simulation menu environment controls
    private float _environmentGasDensity;
    private float _environmentTurbulence;
    private float _environmentLocalGravityMultiplier = 1.0f;
    private float _environmentLocalGravityRadius = 2.0f;
    private Vector3 _environmentLocalGravityCenter = Vector3.Zero;
    private float _supernovaCollapseThreshold = 1.44f;

    // CLI state
    private string _cliInput = string.Empty;
    private readonly List<string> _cliHistory = new();

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
        style.WindowRounding = 8.0f;
        style.FrameRounding = 5.0f;
        style.GrabRounding = 5.0f;
        style.ChildRounding = 6.0f;
        style.Alpha = 0.98f;
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.07f, 0.08f, 0.10f, 0.88f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.09f, 0.11f, 0.14f, 0.58f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.45f, 0.50f, 0.58f, 0.35f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.12f, 0.14f, 0.18f, 0.94f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.16f, 0.20f, 0.27f, 0.96f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.31f, 0.44f, 0.86f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.28f, 0.42f, 0.58f, 0.94f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.33f, 0.49f, 0.67f, 0.98f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.11f, 0.14f, 0.18f, 0.82f);
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
    public double TimeScaleMultiplier => System.Math.Clamp(_timeFlowValue / 100.0f, 0.01f, 100.0f);

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

    public void ApplyEnvironmentEffects(double dt)
    {
        if (dt <= 0.0)
            return;

        var bodies = _engine.Bodies;
        if (bodies.Length == 0)
            return;

        double gas = System.Math.Clamp(_environmentGasDensity, 0.0f, 1.0f);
        double turbulence = System.Math.Clamp(_environmentTurbulence, 0.0f, 1.0f);
        double gravityMult = System.Math.Max(0.0f, _environmentLocalGravityMultiplier);
        double gravityRadius = System.Math.Max(0.01f, _environmentLocalGravityRadius);
        var center = new Vec3d(_environmentLocalGravityCenter.X, _environmentLocalGravityCenter.Y, _environmentLocalGravityCenter.Z);
        double radiusSq = gravityRadius * gravityRadius;

        for (int i = 0; i < bodies.Length; i++)
        {
            ref var body = ref bodies[i];
            if (!body.IsActive)
                continue;

            if (gas > 0.0)
            {
                // Lightweight drag model as a stand-in for denser gas regions.
                double damping = System.Math.Exp(-gas * dt * 0.7);
                body.Velocity *= damping;
            }

            if (gravityMult > 1.0)
            {
                var toCenter = center - body.Position;
                double distSq = toCenter.X * toCenter.X + toCenter.Y * toCenter.Y + toCenter.Z * toCenter.Z;
                if (distSq > 1e-10 && distSq <= radiusSq)
                {
                    double invDist = 1.0 / System.Math.Sqrt(distSq);
                    var dir = toCenter * invDist;
                    double accel = (gravityMult - 1.0) * 0.02 / System.Math.Max(distSq, 0.04);
                    body.Velocity += dir * (accel * dt);
                }
            }

            if (turbulence > 0.0)
            {
                // Deterministic pseudo-noise from id/time to avoid random jitter desync.
                double t = _engine.CurrentTime + body.Id * 0.173;
                var jitter = new Vec3d(
                    System.Math.Sin(t * 2.1),
                    System.Math.Cos(t * 1.6),
                    System.Math.Sin(t * 2.8 + 0.4));
                body.Velocity += jitter * (turbulence * dt * 0.005);
            }
        }
    }

    public void Render(double physicsMs, double renderMs, int bodyCount)
    {
        RenderTopMenus();

        if (_showSimulationControls)
            RenderSimulationControls();
        if (_showEnergyMonitor)
            RenderEnergyMonitor();
        if (_showPerformance)
            RenderPerformance(physicsMs, renderMs, bodyCount);
        if (_showIntegrator)
            RenderIntegratorSelector();
        if (_showAddBody)
            RenderAddBody();
        if (_showBodyInspector)
            RenderBodyInspector();

        RenderBottomControlPanel();
    }

    private void RenderTopMenus()
    {
        switch (_menuMode)
        {
            case ControlMenuMode.Add:
                RenderAddMenuBar();
                break;
            case ControlMenuMode.Simulation:
                RenderSimulationMenuBar();
                break;
        }
    }

    private void RenderBottomControlPanel()
    {
        var io = ImGui.GetIO();
        float panelHeight = System.MathF.Max(104f, 112f * io.DisplayFramebufferScale.Y);

        ImGui.SetNextWindowPos(new Vector2(0, io.DisplaySize.Y - panelHeight), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(io.DisplaySize.X, panelHeight), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoCollapse;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.09f, 0.11f, 0.14f, 0.73f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.72f, 0.78f, 0.86f, 0.30f));

        ImGui.Begin("##control-panel", flags);

        float leftWidth = 240f;
        float rightWidth = 440f;
        float spacing = 14f;
        float middleWidth = System.MathF.Max(260f, ImGui.GetContentRegionAvail().X - leftWidth - rightWidth - (spacing * 2));

        ImGui.BeginChild("##control-left", new Vector2(leftWidth, panelHeight - 18f), ImGuiChildFlags.Borders);
        ImGui.Text("Mode");
        bool addActive = _menuMode == ControlMenuMode.Add;
        bool simActive = _menuMode == ControlMenuMode.Simulation;

        if (RenderControlToggleButton("ADD", addActive))
            _menuMode = addActive ? ControlMenuMode.None : ControlMenuMode.Add;

        ImGui.SameLine();
        if (RenderControlToggleButton("SIMULATE", simActive))
            _menuMode = simActive ? ControlMenuMode.None : ControlMenuMode.Simulation;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.75f, 0.82f, 0.94f, 1.0f), "Placement Flow");
        ImGui.TextWrapped("Ghost follows cursor, right click to anchor, left click to commit.");
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);

        ImGui.BeginChild("##control-middle", new Vector2(middleWidth, panelHeight - 18f), ImGuiChildFlags.Borders);
        ImGui.Text("Time Flow");
        if (ImGui.SliderFloat("##time-flow-slider", ref _timeFlowValue, 1f, 10000f, "%.0f"))
        {
            _timeFlowValue = System.Math.Clamp(_timeFlowValue, 1f, 10000f);
            _manualTimeFlowInput = _timeFlowValue;
        }

        ImGui.TextColored(new Vector4(0.75f, 0.82f, 0.94f, 1.0f), GetTimeFlowAnalysis(_timeFlowValue));

        if (ImGui.InputFloat("Manual Time Speed", ref _manualTimeFlowInput, 10f, 100f, "%.0f"))
        {
            _manualTimeFlowInput = System.Math.Clamp(_manualTimeFlowInput, 1f, 10000f);
            _timeFlowValue = _manualTimeFlowInput;
        }

        ImGui.TextWrapped("100 is real-time baseline. Maximum simulation speed is 10000.");
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);

        ImGui.BeginChild("##control-right", new Vector2(rightWidth, panelHeight - 18f), ImGuiChildFlags.Borders);
        ImGui.Text("Floating Panels");

        RenderWindowToggleChip("Simulation Controls", ref _showSimulationControls);
        ImGui.SameLine();
        RenderWindowToggleChip("Energy Monitor", ref _showEnergyMonitor);
        ImGui.SameLine();
        RenderWindowToggleChip("Performance", ref _showPerformance);

        RenderWindowToggleChip("Integrator", ref _showIntegrator);
        ImGui.SameLine();
        RenderWindowToggleChip("Add Body", ref _showAddBody);
        ImGui.SameLine();
        RenderWindowToggleChip("Body Inspector", ref _showBodyInspector);

        ImGui.EndChild();

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private bool RenderControlToggleButton(string label, bool active)
    {
        var baseColor = active ? new Vector4(0.27f, 0.50f, 0.70f, 0.95f) : new Vector4(0.17f, 0.26f, 0.34f, 0.82f);
        var hoverColor = active ? new Vector4(0.33f, 0.57f, 0.78f, 1.0f) : new Vector4(0.22f, 0.33f, 0.44f, 0.95f);

        ImGui.PushStyleColor(ImGuiCol.Button, baseColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hoverColor);
        bool clicked = ImGui.Button(label, new Vector2(106f, 34f));
        ImGui.PopStyleColor(3);

        return clicked;
    }

    private void RenderWindowToggleChip(string label, ref bool visible)
    {
        var color = visible ? new Vector4(0.22f, 0.47f, 0.66f, 0.92f) : new Vector4(0.20f, 0.22f, 0.27f, 0.82f);
        var hover = visible ? new Vector4(0.30f, 0.57f, 0.76f, 1.0f) : new Vector4(0.27f, 0.30f, 0.36f, 0.92f);

        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hover);
        if (ImGui.Button(label))
            visible = !visible;
        ImGui.PopStyleColor(3);
    }

    private string GetTimeFlowAnalysis(float value)
    {
        if (System.Math.Abs(value - 100f) < 0.001f)
            return "Time speed analysis: 1.00x real-time";

        if (value > 100f)
            return $"Time speed analysis: {value / 100f:F2}x faster than real-time";

        return $"Time speed analysis: {100f / value:F2}x slower than real-time";
    }

    private void RenderAddMenuBar()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(io.DisplaySize.X - 20, 122), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoCollapse;

        ImGui.Begin("ADD MENU", flags);

        var topCategories = _addEntries.Select(e => e.TopCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (topCategories.Length == 0)
        {
            ImGui.Text("No add menu catalog entries available.");
            ImGui.End();
            return;
        }

        _selectedAddTopCategory = System.Math.Clamp(_selectedAddTopCategory, 0, topCategories.Length - 1);
        string selectedTop = topCategories[_selectedAddTopCategory];

        var level2 = _addEntries
            .Where(e => string.Equals(e.TopCategory, selectedTop, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _selectedAddCategory = System.Math.Clamp(_selectedAddCategory, 0, System.Math.Max(0, level2.Length - 1));
        string selectedLevel2 = level2.Length > 0 ? level2[_selectedAddCategory] : string.Empty;

        var level3 = _addEntries
            .Where(e => string.Equals(e.TopCategory, selectedTop, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Category, selectedLevel2, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.SubCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _selectedAddSubCategory = System.Math.Clamp(_selectedAddSubCategory, 0, System.Math.Max(0, level3.Length - 1));
        string selectedLevel3 = level3.Length > 0 ? level3[_selectedAddSubCategory] : string.Empty;

        var objects = _addEntries
            .Where(e => string.Equals(e.TopCategory, selectedTop, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Category, selectedLevel2, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.SubCategory, selectedLevel3, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _selectedAddObject = System.Math.Clamp(_selectedAddObject, 0, System.Math.Max(0, objects.Length - 1));

        ImGui.Text("ADD Path");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f);
        ImGui.Combo("##add-top", ref _selectedAddTopCategory, topCategories, topCategories.Length);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        if (level2.Length > 0)
            ImGui.Combo("##add-l2", ref _selectedAddCategory, level2, level2.Length);
        else
            ImGui.TextDisabled("No category");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f);
        if (level3.Length > 0)
            ImGui.Combo("##add-l3", ref _selectedAddSubCategory, level3, level3.Length);
        else
            ImGui.TextDisabled("No subcategory");

        string[] objectNames = objects.Select(o => o.DisplayName).ToArray();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260f);
        if (objectNames.Length > 0 && ImGui.Combo("##add-object", ref _selectedAddObject, objectNames, objectNames.Length))
        {
            SelectTemplateByName(objects[_selectedAddObject].TemplateName);
            _interactivePlacementEnabled = true;
        }
        else if (objectNames.Length == 0)
        {
            ImGui.TextDisabled("No objects");
        }

        if (objectNames.Length > 0)
        {
            var selectedEntry = objects[_selectedAddObject];
            if (ImGui.Button("Use Selected Object", new Vector2(180, 28)))
            {
                SelectTemplateByName(selectedEntry.TemplateName);
                _interactivePlacementEnabled = true;
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.75f, 0.82f, 0.94f, 1.0f), selectedEntry.Description);
        }

        ImGui.End();
    }

    private void SelectTemplateByName(string templateName)
    {
        for (int categoryIndex = 0; categoryIndex < _catalog.Count; categoryIndex++)
        {
            var templates = _catalog[categoryIndex].Templates;
            for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
            {
                if (!string.Equals(templates[templateIndex].Name, templateName, StringComparison.OrdinalIgnoreCase))
                    continue;

                _selectedCategory = categoryIndex;
                _selectedTemplate = templateIndex;
                _newBodyMass = (float)templates[templateIndex].Mass;
                return;
            }
        }
    }

    private void RenderSimulationMenuBar()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(io.DisplaySize.X - 20, 238), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoCollapse;

        ImGui.Begin("SIMULATION MENU", flags);

        if (ImGui.BeginTable("##sim-menu-table", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
        {
            ImGui.TableNextColumn();
            RenderSimulationEnvironmentSection();

            ImGui.TableNextColumn();
            RenderSimulationPresetSection();

            ImGui.TableNextColumn();
            RenderSimulationCliSection();

            ImGui.EndTable();
        }

        ImGui.End();
    }

    private void RenderSimulationEnvironmentSection()
    {
        ImGui.Text("Environment & Physics");

        bool configDirty = false;
        var config = _engine.Config;

        float gravityScale = (float)config.GravityRangeScale;
        if (ImGui.SliderFloat("Global Gravity Scale", ref gravityScale, 100f, 5000f, "%.0f"))
        {
            config.GravityRangeScale = gravityScale;
            configDirty = true;
        }

        ImGui.SliderFloat("Gas Density", ref _environmentGasDensity, 0.0f, 1.0f, "%.2f");
        ImGui.SliderFloat("Turbulence", ref _environmentTurbulence, 0.0f, 1.0f, "%.2f");

        ImGui.InputFloat3("Local Gravity Center", ref _environmentLocalGravityCenter, "%.2f");
        ImGui.SliderFloat("Local Gravity Radius", ref _environmentLocalGravityRadius, 0.2f, 20f, "%.2f");
        ImGui.SliderFloat("Local Gravity Multiplier", ref _environmentLocalGravityMultiplier, 1.0f, 8.0f, "%.2f");

        if (ImGui.SliderFloat("Chandrasekhar Limit", ref _supernovaCollapseThreshold, 0.8f, 3.0f, "%.2f"))
        {
            _supernovaCollapseThreshold = System.Math.Clamp(_supernovaCollapseThreshold, 0.8f, 3.0f);
        }

        bool gw = config.EnableGravitationalWaves;
        if (ImGui.Checkbox("Enable Gravitational Waves", ref gw))
        {
            config.EnableGravitationalWaves = gw;
            configDirty = true;
        }

        bool pn = config.EnablePostNewtonian;
        if (ImGui.Checkbox("Enable Post-Newtonian", ref pn))
        {
            config.EnablePostNewtonian = pn;
            configDirty = true;
        }

        bool disks = config.EnableAccretionDisks;
        if (ImGui.Checkbox("Enable Accretion Disk Physics", ref disks))
        {
            config.EnableAccretionDisks = disks;
            configDirty = true;
        }

        if (configDirty)
            _engine.Reconfigure();
    }

    private void RenderSimulationPresetSection()
    {
        ImGui.Text("Universe Presets");
        if (ImGui.Button("Star Lifecycle", new Vector2(-1, 28)))
            ApplyPreset("star-lifecycle");
        if (ImGui.Button("Big Bang", new Vector2(-1, 28)))
            ApplyPreset("big-bang");
        if (ImGui.Button("Nebula Formation", new Vector2(-1, 28)))
            ApplyPreset("nebula-formation");
        if (ImGui.Button("Planet Formation", new Vector2(-1, 28)))
            ApplyPreset("planet-formation");
    }

    private void RenderSimulationCliSection()
    {
        ImGui.Text("Simulation CLI");
        ImGui.SetNextItemWidth(-80f);
        if (ImGui.InputText("##sim-cli-input", ref _cliInput, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            ExecuteSimulationCommand(_cliInput);

        ImGui.SameLine();
        if (ImGui.Button("Run", new Vector2(64, 0)))
            ExecuteSimulationCommand(_cliInput);

        ImGui.BeginChild("##sim-cli-log", new Vector2(0, 145), ImGuiChildFlags.Borders);
        if (_cliHistory.Count == 0)
            ImGui.TextDisabled("Type 'help' to list commands.");

        foreach (var line in _cliHistory.TakeLast(24))
            ImGui.TextUnformatted(line);
        ImGui.EndChild();
    }

    private void ExecuteSimulationCommand(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
            return;

        _cliInput = string.Empty;
        _cliHistory.Add($"> {rawCommand}");

        var tokens = rawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return;

        string verb = tokens[0].ToLowerInvariant();
        switch (verb)
        {
            case "help":
                _cliHistory.Add("Commands: help, list bodies, set time <value>, set gas <value>, set gravity region <x> <y> <z> <radius> <mult>, trigger preset <name>, pause, play, step");
                break;

            case "list" when tokens.Length >= 2 && tokens[1].Equals("bodies", StringComparison.OrdinalIgnoreCase):
                if (_engine.Bodies.Length == 0)
                {
                    _cliHistory.Add("No bodies present.");
                }
                else
                {
                    foreach (var body in _engine.Bodies.Take(12))
                        _cliHistory.Add($"id={body.Id} type={body.Type} m={body.Mass:G4} pos=({body.Position.X:F2},{body.Position.Y:F2},{body.Position.Z:F2})");
                }
                break;

            case "set":
                HandleSetCommand(tokens);
                break;

            case "trigger" when tokens.Length >= 3 && tokens[1].Equals("preset", StringComparison.OrdinalIgnoreCase):
                ApplyPreset(string.Join('-', tokens.Skip(2)).ToLowerInvariant());
                break;

            case "pause":
                _engine.Pause();
                _cliHistory.Add("Simulation paused.");
                break;

            case "play":
                _engine.Start();
                _cliHistory.Add("Simulation running.");
                break;

            case "step":
                _engine.StepOnce();
                _cliHistory.Add("Single simulation step executed.");
                break;

            default:
                _cliHistory.Add("Unknown command. Type 'help' for supported commands.");
                break;
        }
    }

    private void HandleSetCommand(string[] tokens)
    {
        if (tokens.Length < 3)
        {
            _cliHistory.Add("Usage: set time <value> | set gas <value> | set gravity region <x> <y> <z> <radius> <mult>");
            return;
        }

        if (tokens[1].Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                _timeFlowValue = System.Math.Clamp(value, 1f, 10000f);
                _manualTimeFlowInput = _timeFlowValue;
                _cliHistory.Add($"Time flow set to {_timeFlowValue:F0}.");
                return;
            }

            _cliHistory.Add("Invalid time value.");
            return;
        }

        if (tokens[1].Equals("gas", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                _environmentGasDensity = System.Math.Clamp(value, 0f, 1f);
                _cliHistory.Add($"Gas density set to {_environmentGasDensity:F2}.");
                return;
            }

            _cliHistory.Add("Invalid gas value.");
            return;
        }

        if (tokens.Length >= 8 &&
            tokens[1].Equals("gravity", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("region", StringComparison.OrdinalIgnoreCase))
        {
            float x = 0f;
            float y = 0f;
            float z = 0f;
            float radius = 0f;
            float multiplier = 0f;

            bool parsed =
                float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                float.TryParse(tokens[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                float.TryParse(tokens[5], NumberStyles.Float, CultureInfo.InvariantCulture, out z) &&
                float.TryParse(tokens[6], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) &&
                float.TryParse(tokens[7], NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier);

            if (parsed)
            {
                _environmentLocalGravityCenter = new Vector3(x, y, z);
                _environmentLocalGravityRadius = System.Math.Max(0.1f, radius);
                _environmentLocalGravityMultiplier = System.Math.Max(1.0f, multiplier);
                _cliHistory.Add("Local gravity region updated.");
                return;
            }

            _cliHistory.Add("Invalid gravity region arguments.");
            return;
        }

        _cliHistory.Add("Unsupported set command.");
    }

    private void ApplyPreset(string presetName)
    {
        switch (presetName)
        {
            case "star-lifecycle":
                ApplyStarLifecyclePreset();
                _cliHistory.Add("Preset applied: star lifecycle.");
                break;

            case "big-bang":
                ApplyBigBangPreset();
                _cliHistory.Add("Preset applied: big bang.");
                break;

            case "nebula-formation":
                ApplyNebulaPreset();
                _cliHistory.Add("Preset applied: nebula formation.");
                break;

            case "planet-formation":
                ApplyPlanetFormationPreset();
                _cliHistory.Add("Preset applied: planet formation.");
                break;

            default:
                _cliHistory.Add($"Unknown preset '{presetName}'.");
                break;
        }

        _initialEnergy = double.NaN;
    }

    private void ApplyStarLifecyclePreset()
    {
        var bodies = new PhysicsBody[]
        {
            CreateBodyFromTemplate("Main Sequence Star", 0, new Vec3d(0, 0, 0), new Vec3d(0, 0, 0)),
            CreateBodyFromTemplate("Terrestrial Planet", 1, new Vec3d(1.2, 0, 0), new Vec3d(0, 0, 0.75)),
            CreateBodyFromTemplate("Moon", 2, new Vec3d(1.28, 0, 0), new Vec3d(0, 0, 0.95)),
        };

        _engine.SetBodies(bodies);
    }

    private void ApplyBigBangPreset()
    {
        var rng = new Random(7);
        var bodies = new List<PhysicsBody>();
        for (int i = 0; i < 22; i++)
        {
            double angle = i * (System.Math.PI * 2.0 / 22.0);
            double radius = 0.03 + rng.NextDouble() * 0.06;
            var position = new Vec3d(System.Math.Cos(angle) * radius, 0, System.Math.Sin(angle) * radius);
            var velocity = new Vec3d(position.X * 12.0, 0, position.Z * 12.0);
            bodies.Add(CreateBodyFromTemplate("Meteoroid", i, position, velocity));
        }

        _engine.SetBodies(bodies.ToArray());
    }

    private void ApplyNebulaPreset()
    {
        var bodies = new List<PhysicsBody>
        {
            CreateBodyFromTemplate("Emission Nebula", 0, new Vec3d(0, 0, 0), Vec3d.Zero)
        };

        var rng = new Random(17);
        for (int i = 1; i <= 14; i++)
        {
            double x = (rng.NextDouble() - 0.5) * 1.8;
            double z = (rng.NextDouble() - 0.5) * 1.8;
            var p = new Vec3d(x, 0, z);
            var v = new Vec3d(-z * 0.08, 0, x * 0.08);
            bodies.Add(CreateBodyFromTemplate("Comet", i, p, v));
        }

        _engine.SetBodies(bodies.ToArray());
    }

    private void ApplyPlanetFormationPreset()
    {
        var bodies = new List<PhysicsBody>
        {
            CreateBodyFromTemplate("G-Type Star", 0, Vec3d.Zero, Vec3d.Zero)
        };

        var rng = new Random(31);
        for (int i = 1; i <= 18; i++)
        {
            double r = 0.4 + i * 0.08;
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            var p = new Vec3d(System.Math.Cos(angle) * r, 0, System.Math.Sin(angle) * r);
            double speed = System.Math.Sqrt(1.0 / System.Math.Max(r, 0.08));
            var tangent = new Vec3d(-System.Math.Sin(angle), 0, System.Math.Cos(angle));
            bodies.Add(CreateBodyFromTemplate("Asteroid", i, p, tangent * speed * 0.18));
        }

        _engine.SetBodies(bodies.ToArray());
    }

    private PhysicsBody CreateBodyFromTemplate(string templateName, int id, Vec3d position, Vec3d velocity)
    {
        if (!CelestialCatalog.TryGetTemplate(templateName, out var template))
            template = ObjectTemplates.Asteroid;

        if (!Enum.TryParse<BodyType>(template.BodyType, ignoreCase: true, out var parsedType))
            parsedType = BodyType.Custom;

        return new PhysicsBody(id, template.Mass, position, velocity, parsedType)
        {
            Radius = template.Radius,
            GravityStrength = template.GravityStrength,
            GravityRange = template.GravityRange,
            IsActive = true,
            IsCollidable = true
        };
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
