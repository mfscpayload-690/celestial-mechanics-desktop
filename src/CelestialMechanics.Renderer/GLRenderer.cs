using System.Numerics;
using Silk.NET.OpenGL;
using CelestialMechanics.Simulation;
using CelestialMechanics.Simulation.Placement;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Extensions;

namespace CelestialMechanics.Renderer;

public enum BlackHoleVisualQuality
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum BlackHoleVisualPreset
{
    CinematicOrange = 0,
    Eht230 = 1,
    Eht345 = 2,
}

public enum BlackHoleDebugView
{
    None = 0,
    HorizonMask = 1,
    RingMask = 2,
    LensWarpField = 3,
    OpticalDepth = 4,
}

public class GLRenderer : IDisposable
{
    private const int AccretionRendererCapacity = 24000;
    private const int PredictionHardMaxSteps = 1024;
    private const int PredictionMemoryBudgetPoints = 1024;
    private const int SimulationModeMaxPredictionSteps = 192;
    private const int AnalysisModeMinPredictionSteps = 256;

    private struct CollisionFlash
    {
        public Vector3 Position;
        public float Age;
        public float Lifetime;
        public float MaxRadius;
        public float Intensity;
        public float Phase;
    }

    private GL? _gl;
    private InstancedSphereRenderer _sphereRenderer = new();
    private InstancedSphereRenderer _effectSphereRenderer = new();
    private AccretionDiskRenderer? _accretionDiskRenderer;
    private GridRenderer _gridRenderer = new();
    private LineRenderer _lineRenderer = new();
    private ShaderProgram? _sphereShader;
    private ShaderProgram? _gridShader;
    private ShaderProgram? _lineShader;
    private ShaderProgram? _backgroundShader;
    private ProceduralAlbedoAtlas? _albedoAtlas;
    private Camera _camera = new();
    private RenderState _renderState = new();
    private RenderBody[] _compositeBodies = Array.Empty<RenderBody>();
    private RenderBody[] _effectBodies = Array.Empty<RenderBody>();
    private int _effectBodyCount;
    private BackgroundRenderer _backgroundRenderer = new();
    private float _timeSeconds;

    // Placement overlays
    private bool _showGhost;
    private RenderBody _ghostBody;
    private bool _showDirectionVector;
    private Vector3 _directionVectorStart;
    private Vector3 _directionVectorEnd;
    private readonly List<Vector3> _previewTrajectory = new();

    // Orbital trails
    private readonly Dictionary<int, Queue<Vector3>> _trailByBodyId = new();
    private readonly Dictionary<int, Vector3> _lastTrailPointByBodyId = new();
    private readonly Dictionary<int, Vector4> _trailColorByBodyId = new();
    private readonly HashSet<int> _activeTrailIds = new();
    private readonly List<int> _staleTrailIds = new();
    private readonly Dictionary<int, Queue<Vector3>> _orbitPathByBodyId = new();
    private readonly Dictionary<int, Vector3> _lastOrbitPathPointByBodyId = new();
    private readonly HashSet<int> _activeOrbitPathIds = new();
    private readonly List<int> _staleOrbitPathIds = new();
    private readonly List<Vector3> _predictedTrajectory = new();
    private readonly ISelectionContext? _selectionContext;
    private readonly List<CollisionFlash> _collisionFlashes = new(64);
    private double _lastCollisionEventTime = double.NegativeInfinity;
    private float _bhParticleHeat;
    private float _bhParticleDensity;
    private int _qualityBudgetStrikeCount;
    private int _qualityRecoveryCount;
    private const int MaxStarLights = 8;
    private const int MaxRayOccluders = 24;
    private readonly Vector4[] _starLightData = new Vector4[MaxStarLights];
    private readonly Vector3[] _starLightColorData = new Vector3[MaxStarLights];
    private readonly Vector4[] _rayOccluderData = new Vector4[MaxRayOccluders];
    private int _starLightCount;
    private int _rayOccluderCount;
    private Vector3 _currentFrameOrigin;

    public GLRenderer(ISelectionContext? selectionContext = null)
    {
        _selectionContext = selectionContext;
    }

    public Camera Camera => _camera;
    public RenderState RenderState => _renderState;
    public ReferenceFrameManager ReferenceFrame { get; } = new();
    public bool ShowGrid { get; set; } = true;
    public bool ShowVelocityArrows { get; set; } = false;
    public bool ShowBackground { get; set; } = true;
    public bool ShowOrbitalTrails { get; set; } = true;
    public bool ShowPersistentOrbitPaths { get; set; } = true;
    public bool ShowPredictedTrajectory { get; set; } = true;
    public bool ShowAccretionDisks { get; set; } = true;
    public bool UseAnalysisPrediction { get; set; }
    public bool HighPrecisionPrediction { get; set; }
    public int MaxTrailPoints { get; set; } = 24;
    public int OrbitPathMaxPoints { get; set; } = 720;
    public int PredictionSteps { get; set; } = 180;
    public float TrailMinDistance { get; set; } = 0.0035f;
    public float OrbitPathMinDistance { get; set; } = 0.0015f;
    public int SelectedBodyId => _selectionContext?.SelectedBodyId ?? -1;
    public float GlobalLuminosityScale { get; set; } = 1.18f;
    public float GlobalGlowScale { get; set; } = 1.15f;
    public float GlobalSaturation { get; set; } = 1.03f;
    public bool EnableAlbedoTextureMaps { get; set; } = true;
    public float AlbedoTextureBlend { get; set; } = 0.85f;
    public bool EnableStarDrivenLighting { get; set; } = true;
    public bool EnableRayTracedShadows { get; set; } = true;
    public float StarLightIntensityScale { get; set; } = 1.25f;
    public float StarLightFalloff { get; set; } = 0.85f;
    public float AmbientLightFloor { get; set; } = 0.12f;
    public float RayShadowStrength { get; set; } = 0.72f;
    public float RayShadowSoftness { get; set; } = 0.02f;

    public bool AutoBlackHoleQuality { get; set; } = true;
    public BlackHoleVisualQuality BlackHoleQualityTier { get; set; } = BlackHoleVisualQuality.Medium;
    public BlackHoleVisualPreset BlackHolePreset { get; set; } = BlackHoleVisualPreset.CinematicOrange;
    public BlackHoleDebugView BlackHoleDebugMode { get; set; } = BlackHoleDebugView.None;
    public float BlackHoleRingThickness { get; set; } = 0.55f;
    public float BlackHoleLensingStrength { get; set; } = 1.25f;
    public float BlackHoleDopplerBoost { get; set; } = 1.15f;
    public float BlackHoleOpticalDepth { get; set; } = 1.4f;
    public float BlackHoleTemperatureScale { get; set; } = 1.0f;
    public float BlackHoleBloomScale { get; set; } = 1.25f;

    public void SetPlacementPreview(
        RenderBody? ghost,
        Vector3? directionStart,
        Vector3? directionEnd,
        IReadOnlyList<Vector3>? trajectory)
    {
        if (ghost.HasValue)
        {
            _showGhost = true;
            _ghostBody = ghost.Value;
        }
        else
        {
            _showGhost = false;
        }

        if (directionStart.HasValue && directionEnd.HasValue)
        {
            _showDirectionVector = true;
            _directionVectorStart = directionStart.Value;
            _directionVectorEnd = directionEnd.Value;
        }
        else
        {
            _showDirectionVector = false;
        }

        _previewTrajectory.Clear();
        if (trajectory != null)
            _previewTrajectory.AddRange(trajectory);
    }

    public void RequestPredictionRefresh()
    {
        _predictedTrajectory.Clear();
    }

    public void ClearPlacementPreview()
    {
        _showGhost = false;
        _showDirectionVector = false;
        _previewTrajectory.Clear();
    }

    public void ClearAllHistory()
    {
        _trailByBodyId.Clear();
        _lastTrailPointByBodyId.Clear();
        _trailColorByBodyId.Clear();
        _activeTrailIds.Clear();

        _orbitPathByBodyId.Clear();
        _lastOrbitPathPointByBodyId.Clear();
        _activeOrbitPathIds.Clear();
    }

    public void Initialize(GL gl)
    {
        _gl = gl;

        string shaderDir = FindShaderDirectory();

        string sphereVert = File.ReadAllText(Path.Combine(shaderDir, "sphere.vert"));
        string sphereFrag = File.ReadAllText(Path.Combine(shaderDir, "sphere.frag"));
        _sphereShader = new ShaderProgram(gl, sphereVert, sphereFrag);

        string gridVert = File.ReadAllText(Path.Combine(shaderDir, "grid.vert"));
        string gridFrag = File.ReadAllText(Path.Combine(shaderDir, "grid.frag"));
        _gridShader = new ShaderProgram(gl, gridVert, gridFrag);

        string lineVert = File.ReadAllText(Path.Combine(shaderDir, "line.vert"));
        string lineFrag = File.ReadAllText(Path.Combine(shaderDir, "line.frag"));
        _lineShader = new ShaderProgram(gl, lineVert, lineFrag);

        string bgVert = File.ReadAllText(Path.Combine(shaderDir, "background.vert"));
        string bgFrag = File.ReadAllText(Path.Combine(shaderDir, "background.frag"));
        _backgroundShader = new ShaderProgram(gl, bgVert, bgFrag);

        _sphereRenderer.Initialize(gl);
        _effectSphereRenderer.Initialize(gl);
        _albedoAtlas = new ProceduralAlbedoAtlas(gl);
        _albedoAtlas.Initialize();
        _accretionDiskRenderer = new AccretionDiskRenderer(gl);
        _accretionDiskRenderer.Initialize(AccretionRendererCapacity);
        _accretionDiskRenderer.PointScale = 2.2f;
        _gridRenderer.Initialize(gl);
        _lineRenderer.Initialize(gl);
        _backgroundRenderer.Initialize(gl);
    }

    private static string FindShaderDirectory()
    {
        // Look for Shaders directory relative to the executable
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string shaderDir = Path.Combine(baseDir, "Shaders");
        if (Directory.Exists(shaderDir))
            return shaderDir;

        // Try looking relative to the source directory
        string? dir = baseDir;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "src", "CelestialMechanics.Renderer", "Shaders");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Could not find Shaders directory. Searched from: {baseDir}");
    }

    public void UpdateFromSimulation(SimulationEngine engine)
    {
        _currentFrameOrigin = Vector3.Zero;
        if (engine.Bodies != null && engine.Bodies.Length > 0)
        {
            var origin = ReferenceFrame.ComputeOrigin(engine.Bodies);
            _currentFrameOrigin = new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z);
        }

        _renderState.UpdateFrom(engine, ReferenceFrame, SelectedBodyId);

        AppendCollisionBursts(engine.CurrentState);

        if (_renderState.BodyCount == 0 && engine.CurrentState.CollisionBursts.Count == 0)
            _collisionFlashes.Clear();

        BuildCollisionEffectBodies();

        int compositeCount = _renderState.BodyCount + (_showGhost ? 1 : 0);
        if (_compositeBodies.Length < compositeCount)
            _compositeBodies = new RenderBody[compositeCount];

        if (_renderState.BodyCount > 0)
            Array.Copy(_renderState.Bodies, _compositeBodies, _renderState.BodyCount);

        ApplySelectionHighlight();

        if (_showGhost)
            _compositeBodies[_renderState.BodyCount] = _ghostBody;

        BuildStarLightsAndOccluders();

        // Update sphere instances
        _sphereRenderer.UpdateInstances(_compositeBodies, compositeCount);
        _effectSphereRenderer.UpdateInstances(_effectBodies, _effectBodyCount);

        if (_accretionDiskRenderer != null)
        {
            ReadOnlySpan<DiskParticle> particles = ShowAccretionDisks
                ? engine.GetAccretionParticles()
                : ReadOnlySpan<DiskParticle>.Empty;

            _accretionDiskRenderer.UpdateParticles(particles);
            _bhParticleHeat = _accretionDiskRenderer.AverageTemperatureNormalized;
            _bhParticleDensity = _accretionDiskRenderer.ActiveParticleRatio;
        }

        _lineRenderer.Clear();

        if (engine.Bodies != null && ShowPersistentOrbitPaths)
        {
            UpdateOrbitPaths(engine.Bodies);
            AppendOrbitPathLines(engine.Bodies);
        }

        if (ShowOrbitalTrails && engine.Bodies != null)
        {
            UpdateTrails(engine.Bodies);
            AppendTrailLines(engine.Bodies);
        }

        UpdatePredictedTrajectory(engine);
        if (_predictedTrajectory.Count > 1)
            AppendPredictedTrajectoryDashed();

        // Update velocity arrows if enabled
        if (ShowVelocityArrows && engine.Bodies != null)
        {
            foreach (var body in engine.Bodies)
            {
                if (!body.IsActive) continue;
                var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z) - _currentFrameOrigin;
                var vel = new Vector3((float)body.Velocity.X, (float)body.Velocity.Y, (float)body.Velocity.Z);
                float velLen = vel.Length();
                if (velLen > 0.001f)
                {
                    var end = pos + Vector3.Normalize(vel) * MathF.Min(velLen * 0.5f, 2f);
                    _lineRenderer.AddLine(pos, end, new Vector4(0.0f, 1.0f, 0.4f, 0.8f));
                }
            }
        }

        if (_showDirectionVector)
            AppendDirectionVector();

        if (_previewTrajectory.Count > 1)
            AppendPreviewTrajectory();

        _lineRenderer.Upload();
    }

    private void ApplySelectionHighlight()
    {
        if (SelectedBodyId < 0)
            return;

        for (int i = 0; i < _renderState.BodyCount; i++)
        {
            if (_compositeBodies[i].Id != SelectedBodyId)
                continue;

            _compositeBodies[i].Radius *= 1.14f;
            _compositeBodies[i].Color = Vector4.Lerp(_compositeBodies[i].Color, new Vector4(1.0f, 1.0f, 0.78f, 1.0f), 0.42f);
            _compositeBodies[i].VisualParams.Z *= 1.35f;
            _compositeBodies[i].VisualParams.W *= 1.2f;
            break;
        }
    }

    private void UpdatePredictedTrajectory(SimulationEngine engine)
    {
        _predictedTrajectory.Clear();

        if (!ShowPredictedTrajectory || SelectedBodyId < 0 || engine.Bodies == null || engine.Bodies.Length == 0)
            return;

        int requestedSteps = System.Math.Clamp(PredictionSteps, 16, PredictionHardMaxSteps);
        int modeSteps = UseAnalysisPrediction
            ? System.Math.Max(requestedSteps, AnalysisModeMinPredictionSteps)
            : System.Math.Min(requestedSteps, SimulationModeMaxPredictionSteps);
        int steps = System.Math.Min(modeSteps, PredictionMemoryBudgetPoints);

        double baseDt = System.Math.Max(engine.Config.TimeStep, 1e-5);
        double currentDt = System.Math.Max(engine.CurrentState.CurrentDt, 1e-5);
        bool highPrecision = UseAnalysisPrediction || HighPrecisionPrediction;
        double dt = highPrecision
            ? System.Math.Max(System.Math.Min(currentDt, baseDt) * 0.5, 1e-5)
            : System.Math.Max(currentDt, baseDt);

        var predicted = TrajectoryPredictor.PredictBodyTrajectory(
            engine.Bodies,
            SelectedBodyId,
            engine.Config,
            steps,
            dt);

        if (predicted.Count == 0)
            return;

        for (int i = 0; i < predicted.Count; i++)
        {
            _predictedTrajectory.Add(predicted[i].ToVector3() - _currentFrameOrigin);
        }
    }

    private void AppendPredictedTrajectoryDashed()
    {
        // Dashed rendering: emit every other segment to distinguish future path from past trail.
        var color = new Vector4(0.72f, 0.92f, 1.0f, 0.72f);
        for (int i = 1; i < _predictedTrajectory.Count; i++)
        {
            if ((i % 2) == 0)
                continue;

            _lineRenderer.AddLine(_predictedTrajectory[i - 1], _predictedTrajectory[i], color);
        }
    }

    private void BuildCollisionEffectBodies()
    {
        const int layersPerFlash = 2;
        int needed = _collisionFlashes.Count * layersPerFlash;
        if (_effectBodies.Length < needed)
            _effectBodies = new RenderBody[System.Math.Max(needed, 16)];

        _effectBodyCount = 0;

        for (int i = 0; i < _collisionFlashes.Count; i++)
        {
            var fx = _collisionFlashes[i];
            float t = fx.Age / System.Math.Max(fx.Lifetime, 1e-5f);
            float fade = System.Math.Clamp(1.0f - t, 0.0f, 1.0f);
            float wobble = 1.0f + 0.09f * MathF.Sin(fx.Phase + t * 13.0f);

            float coreRadius = fx.MaxRadius * (0.08f + 0.18f * t) * wobble;
            float plumeRadius = fx.MaxRadius * (0.26f + 0.74f * t) * (1.0f + 0.03f * MathF.Sin(fx.Phase + t * 7.0f));
            var framePosition = fx.Position - _currentFrameOrigin;

            AddEffectBody(
                framePosition,
                coreRadius,
                new Vector4(0.98f, 0.95f, 0.88f, 0.32f * fx.Intensity * fade),
                new Vector4(7.0f, 1.55f * fx.Intensity, 1.25f * fx.Intensity, 0.55f));

            AddEffectBody(
                framePosition,
                plumeRadius,
                new Vector4(0.62f, 0.76f, 1.0f, 0.19f * fx.Intensity * fade),
                new Vector4(7.0f, 0.95f * fx.Intensity, 1.05f * fx.Intensity, 0.85f));
        }
    }

    private void BuildStarLightsAndOccluders()
    {
        _starLightCount = 0;
        _rayOccluderCount = 0;

        for (int i = 0; i < _renderState.BodyCount; i++)
        {
            ref readonly var body = ref _renderState.Bodies[i];

            if (_rayOccluderCount < MaxRayOccluders && body.Radius > 0.015f)
            {
                _rayOccluderData[_rayOccluderCount++] = new Vector4(
                    body.Position.X,
                    body.Position.Y,
                    body.Position.Z,
                    body.Radius * 1.02f);
            }

            bool emitsLight = body.BodyType == (int)BodyType.Star || body.BodyType == (int)BodyType.NeutronStar;
            if (!emitsLight || _starLightCount >= MaxStarLights)
                continue;

            float radius = System.MathF.Max(body.Radius, 0.01f);
            float luminosity = System.MathF.Max(body.VisualParams.Y, 0.05f);
            float intensity = System.MathF.Max(
                0.05f,
                StarLightIntensityScale * luminosity * (0.55f + 0.45f * System.MathF.Sqrt(radius)));

            _starLightData[_starLightCount] = new Vector4(body.Position.X, body.Position.Y, body.Position.Z, intensity);
            _starLightColorData[_starLightCount] = Vector3.Clamp(new Vector3(body.Color.X, body.Color.Y, body.Color.Z), Vector3.Zero, Vector3.One);
            _starLightCount++;
        }

        if (_starLightCount == 0)
        {
            Vector3 fallbackDir = Vector3.Normalize(new Vector3(1.8f, 2.4f, 1.1f));
            Vector3 fallbackPos = _camera.Position + fallbackDir * 22.0f;
            _starLightData[0] = new Vector4(fallbackPos, 0.55f * StarLightIntensityScale);
            _starLightColorData[0] = new Vector3(1.0f, 0.96f, 0.9f);
            _starLightCount = 1;
        }
    }

    private void AddEffectBody(Vector3 position, float radius, Vector4 color, Vector4 visual)
    {
        if (_effectBodyCount >= _effectBodies.Length || radius <= 0.0f || color.W <= 0.001f)
            return;

        _effectBodies[_effectBodyCount++] = new RenderBody
        {
            Id = -1000 - _effectBodyCount,
            Position = position,
            Radius = radius,
            Color = color,
            BodyType = (int)BodyType.Star,
            VisualParams = visual
        };
    }

    private void AppendCollisionBursts(SimulationState state)
    {
        if (state.Time <= _lastCollisionEventTime || state.CollisionBursts.Count == 0)
        {
            _lastCollisionEventTime = System.Math.Max(_lastCollisionEventTime, state.Time);
            return;
        }
        while (_collisionFlashes.Count > 12)
            _collisionFlashes.RemoveAt(0);

        for (int i = 0; i < state.CollisionBursts.Count; i++)
        {
            var burst = state.CollisionBursts[i];
            float intensity = (float)System.Math.Clamp(
                0.26 + System.Math.Log10(1.0 + burst.ReleasedEnergy) * 0.07,
                0.18,
                0.75);

            float radius = (float)System.Math.Clamp(
                0.08 + System.Math.Cbrt(System.Math.Max(burst.CombinedMass, 1e-9)) * 0.16,
                0.10,
                0.44);

            _collisionFlashes.Add(new CollisionFlash
            {
                Position = new Vector3((float)burst.Position.X, (float)burst.Position.Y, (float)burst.Position.Z),
                Age = 0f,
                Lifetime = 0.42f,
                MaxRadius = radius,
                Intensity = intensity,
                Phase = Hash01(new Vector3((float)burst.Position.X, (float)burst.Position.Y, (float)burst.Position.Z)) * (MathF.PI * 2.0f)
            });
        }

        _lastCollisionEventTime = state.Time;
    }

    private static float Hash01(Vector3 p)
    {
        float h = MathF.Sin(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.5453f;
        return h - MathF.Floor(h);
    }

    private void UpdateCollisionFlashes(float dt)
    {
        for (int i = _collisionFlashes.Count - 1; i >= 0; i--)
        {
            var fx = _collisionFlashes[i];
            fx.Age += dt;

            if (fx.Age >= fx.Lifetime)
            {
                _collisionFlashes.RemoveAt(i);
                continue;
            }

            _collisionFlashes[i] = fx;
        }
    }

    public void Render(float deltaTime, int width, int height)
    {
        if (_gl == null) return;

        if (AutoBlackHoleQuality)
            UpdateBlackHoleQualityTier(deltaTime);

        _timeSeconds += deltaTime;
        UpdateCollisionFlashes(System.Math.Max(deltaTime, 1e-5f));
        _camera.Update(deltaTime);

        float aspect = width / (float)System.Math.Max(height, 1);
        var view = _camera.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix(aspect);
        var viewPos = _camera.Position;

        if (ShowBackground)
        {
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.DepthTest);

            _backgroundShader!.Use();
            _backgroundShader.SetUniform("uTime", _timeSeconds);
            _backgroundShader.SetUniform("uResolution", new Vector2(width, height));
            _backgroundShader.SetUniform("uCameraPos", viewPos);
            _backgroundRenderer.Render(_gl);

            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(TriangleFace.Back);
        }

        // Render grid
        if (ShowGrid)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gridShader!.Use();
            _gridShader.SetUniform("uView", view);
            _gridShader.SetUniform("uProjection", projection);
            var snappedGridOffset = new Vector3(
                System.MathF.Floor(viewPos.X / 10.0f) * 10.0f,
                0.0f,
                System.MathF.Floor(viewPos.Z / 10.0f) * 10.0f);
            _gridShader.SetUniform("uGridOffset", snappedGridOffset);
            _gridRenderer.Render(_gl, _gridShader);
            _gl.Disable(EnableCap.Blend);
        }

        // Render bodies
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _sphereShader!.Use();
        if (_albedoAtlas != null)
        {
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2DArray, _albedoAtlas.Handle);
        }
        _sphereShader.SetUniform("uView", view);
        _sphereShader.SetUniform("uProjection", projection);
        _sphereShader.SetUniform("uViewPos", viewPos);
        _sphereShader.SetUniform("uTime", _timeSeconds);
        _sphereShader.SetUniform("uGlobalLuminosity", GlobalLuminosityScale);
        _sphereShader.SetUniform("uGlobalGlow", GlobalGlowScale);
        _sphereShader.SetUniform("uGlobalSaturation", GlobalSaturation);
        _sphereShader.SetUniform("uUseAlbedoAtlas", EnableAlbedoTextureMaps && _albedoAtlas != null ? 1 : 0);
        _sphereShader.SetUniform("uBodyAlbedoAtlas", 1);
        _sphereShader.SetUniform("uAlbedoBlend", System.Math.Clamp(AlbedoTextureBlend, 0.0f, 1.0f));
        _sphereShader.SetUniform("uEnableStarLighting", EnableStarDrivenLighting ? 1 : 0);
        _sphereShader.SetUniform("uStarLightCount", _starLightCount);
        _sphereShader.SetUniform("uStarLightFalloff", System.Math.Clamp(StarLightFalloff, 0.05f, 4.0f));
        _sphereShader.SetUniform("uAmbientFloor", System.Math.Clamp(AmbientLightFloor, 0.01f, 0.5f));
        _sphereShader.SetUniform("uRayTraceShadows", EnableRayTracedShadows ? 1 : 0);
        _sphereShader.SetUniform("uRayOccluderCount", _rayOccluderCount);
        _sphereShader.SetUniform("uRayShadowStrength", System.Math.Clamp(RayShadowStrength, 0.0f, 1.0f));
        _sphereShader.SetUniform("uRayShadowSoftness", System.Math.Clamp(RayShadowSoftness, 0.0005f, 0.20f));
        _sphereShader.SetUniform("uBhQualityTier", (int)BlackHoleQualityTier);
        _sphereShader.SetUniform("uBhPreset", (int)BlackHolePreset);
        _sphereShader.SetUniform("uBhRingThickness", System.Math.Clamp(BlackHoleRingThickness, 0.08f, 1.0f));
        _sphereShader.SetUniform("uBhLensStrength", System.Math.Clamp(BlackHoleLensingStrength, 0.0f, 2.5f));
        _sphereShader.SetUniform("uBhDopplerBoost", System.Math.Clamp(BlackHoleDopplerBoost, 0.0f, 3.0f));
        _sphereShader.SetUniform("uBhOpticalDepth", System.Math.Clamp(BlackHoleOpticalDepth, 0.0f, 4.0f));
        _sphereShader.SetUniform("uBhTemperatureScale", System.Math.Clamp(BlackHoleTemperatureScale, 0.25f, 3.0f));
        _sphereShader.SetUniform("uBhBloomScale", System.Math.Clamp(BlackHoleBloomScale, 0.0f, 2.5f));
        _sphereShader.SetUniform("uBhDebugMode", (int)BlackHoleDebugMode);
        _sphereShader.SetUniform("uBhParticleHeat", System.Math.Clamp(_bhParticleHeat, 0.0f, 1.0f));
        _sphereShader.SetUniform("uBhParticleDensity", System.Math.Clamp(_bhParticleDensity, 0.0f, 1.0f));

        for (int i = 0; i < _starLightCount; i++)
        {
            Vector4 light = _starLightData[i];
            _sphereShader.SetUniform($"uStarLights[{i}]", new Vector3(light.X, light.Y, light.Z));
            _sphereShader.SetUniform($"uStarLightIntensity[{i}]", light.W);
            _sphereShader.SetUniform($"uStarLightColor[{i}]", _starLightColorData[i]);
        }

        for (int i = 0; i < _rayOccluderCount; i++)
        {
            Vector4 occ = _rayOccluderData[i];
            _sphereShader.SetUniform($"uRayOccluders[{i}]", new Vector3(occ.X, occ.Y, occ.Z));
            _sphereShader.SetUniform($"uRayOccluderRadius[{i}]", occ.W);
        }

        _sphereRenderer.Render(_gl, _sphereShader);
        _gl.Disable(EnableCap.Blend);

        if (_effectBodyCount > 0)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
            _effectSphereRenderer.Render(_gl, _sphereShader);
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        if (ShowAccretionDisks && _accretionDiskRenderer != null)
        {
            var viewProjection = view * projection;
            _accretionDiskRenderer.ConfigureBlackHoleVisuals(
                qualityTier: (int)BlackHoleQualityTier,
                preset: (int)BlackHolePreset,
                dopplerBoost: System.Math.Clamp(BlackHoleDopplerBoost, 0.0f, 3.0f),
                opticalDepth: System.Math.Clamp(BlackHoleOpticalDepth, 0.0f, 4.0f),
                temperatureScale: System.Math.Clamp(BlackHoleTemperatureScale, 0.25f, 3.0f),
                bloomScale: System.Math.Clamp(BlackHoleBloomScale, 0.0f, 2.5f),
                debugMode: (int)BlackHoleDebugMode);
            _accretionDiskRenderer.Draw(viewProjection);
        }

        // Render line overlays (trails, velocity arrows, placement vectors)
        if (_lineRenderer.HasLines)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.LineWidth(2.0f);
            _lineShader!.Use();
            _lineShader.SetUniform("uView", view);
            _lineShader.SetUniform("uProjection", projection);
            _lineRenderer.Render(_gl, _lineShader);
            _gl.LineWidth(1.0f);
            _gl.Disable(EnableCap.Blend);
        }
    }

    private void UpdateTrails(PhysicsBody[] bodies)
    {
        _activeTrailIds.Clear();
        float minDist2 = TrailMinDistance * TrailMinDistance;

        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (!body.IsActive)
                continue;

            _activeTrailIds.Add(body.Id);
            var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

            if (!_trailByBodyId.TryGetValue(body.Id, out var trail))
            {
                trail = new Queue<Vector3>(MaxTrailPoints + 2);
                _trailByBodyId[body.Id] = trail;
                _lastTrailPointByBodyId[body.Id] = pos;
            }

            if (trail.Count == 0 ||
                !_lastTrailPointByBodyId.TryGetValue(body.Id, out var lastPos) ||
                Vector3.DistanceSquared(lastPos, pos) > minDist2)
            {
                trail.Enqueue(pos);
                _lastTrailPointByBodyId[body.Id] = pos;
            }

            while (trail.Count > MaxTrailPoints)
                trail.Dequeue();
        }

        _staleTrailIds.Clear();
        foreach (int id in _trailByBodyId.Keys)
        {
            if (!_activeTrailIds.Contains(id))
                _staleTrailIds.Add(id);
        }

        for (int i = 0; i < _staleTrailIds.Count; i++)
        {
            int stale = _staleTrailIds[i];
            _trailByBodyId.Remove(stale);
            _lastTrailPointByBodyId.Remove(stale);
        }
    }

    private void UpdateOrbitPaths(PhysicsBody[] bodies)
    {
        _activeOrbitPathIds.Clear();
        float minDist2 = OrbitPathMinDistance * OrbitPathMinDistance;

        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (!body.IsActive)
                continue;

            _activeOrbitPathIds.Add(body.Id);
            var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

            if (!_orbitPathByBodyId.TryGetValue(body.Id, out var path))
            {
                path = new Queue<Vector3>(OrbitPathMaxPoints + 2);
                _orbitPathByBodyId[body.Id] = path;
                _lastOrbitPathPointByBodyId[body.Id] = pos;
            }

            if (path.Count == 0 ||
                !_lastOrbitPathPointByBodyId.TryGetValue(body.Id, out var lastPos) ||
                Vector3.DistanceSquared(lastPos, pos) > minDist2)
            {
                path.Enqueue(pos);
                _lastOrbitPathPointByBodyId[body.Id] = pos;
            }

            while (path.Count > OrbitPathMaxPoints)
                path.Dequeue();
        }

        _staleOrbitPathIds.Clear();
        foreach (int id in _orbitPathByBodyId.Keys)
        {
            if (!_activeOrbitPathIds.Contains(id))
                _staleOrbitPathIds.Add(id);
        }

        for (int i = 0; i < _staleOrbitPathIds.Count; i++)
        {
            int stale = _staleOrbitPathIds[i];
            _orbitPathByBodyId.Remove(stale);
            _lastOrbitPathPointByBodyId.Remove(stale);
        }
    }

    private void AppendOrbitPathLines(PhysicsBody[] bodies)
    {
        _trailColorByBodyId.Clear();
        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (body.IsActive)
                _trailColorByBodyId[body.Id] = GetTrailColor(body.Type);
        }

        foreach (var (bodyId, path) in _orbitPathByBodyId)
        {
            if (path.Count < 2)
                continue;

            if (!_trailColorByBodyId.TryGetValue(bodyId, out var color))
                color = new Vector4(0.8f, 0.8f, 0.8f, 0.6f);

            color.W = System.MathF.Max(color.W, 0.62f);

            if (bodyId == SelectedBodyId)
                color = Vector4.Lerp(color, new Vector4(1.0f, 0.95f, 0.72f, 0.95f), 0.55f);

            Vector3? prev = null;
            int pointIndex = 0;
            int pointCount = path.Count;
            foreach (var point in path)
            {
                if (prev.HasValue)
                {
                    float fade = (float)pointIndex / System.Math.Max(1, pointCount - 1);
                    Vector4 segmentColor = color;
                    segmentColor.W *= (0.1f + 0.9f * fade); // fade from 10% to 100% of target alpha
                    _lineRenderer.AddLine(prev.Value - _currentFrameOrigin, point - _currentFrameOrigin, segmentColor);
                }
                prev = point;
                pointIndex++;
            }
        }
    }

    private void AppendTrailLines(PhysicsBody[] bodies)
    {
        _trailColorByBodyId.Clear();
        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly var body = ref bodies[i];
            if (body.IsActive)
                _trailColorByBodyId[body.Id] = GetTrailColor(body.Type);
        }

        foreach (var (bodyId, trail) in _trailByBodyId)
        {
            if (trail.Count < 2)
                continue;

            if (!_trailColorByBodyId.TryGetValue(bodyId, out var color))
                color = new Vector4(0.8f, 0.8f, 0.8f, 0.3f);

            if (bodyId == SelectedBodyId)
                color = Vector4.Lerp(color, new Vector4(1.0f, 0.95f, 0.72f, 0.9f), 0.65f);

            Vector3? prev = null;
            int pointIndex = 0;
            int pointCount = trail.Count;
            foreach (var point in trail)
            {
                if (prev.HasValue)
                {
                    float fade = (float)pointIndex / System.Math.Max(1, pointCount - 1);
                    // Square the fade for a more dramatic tail drop-off
                    float smoothFade = fade * fade; 
                    Vector4 segmentColor = color;
                    segmentColor.W *= smoothFade;
                    _lineRenderer.AddLine(prev.Value - _currentFrameOrigin, point - _currentFrameOrigin, segmentColor);
                }
                prev = point;
                pointIndex++;
            }
        }
    }

    private void AppendDirectionVector()
    {
        var glow = new Vector4(1.0f, 1.0f, 1.0f, 0.95f);
        var start = _directionVectorStart - _currentFrameOrigin;
        var end = _directionVectorEnd - _currentFrameOrigin;
        _lineRenderer.AddLine(start, end, glow);

        var dir = end - start;
        if (dir.LengthSquared() < 1e-8f)
            return;

        dir = Vector3.Normalize(dir);
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        if (side.LengthSquared() < 1e-8f)
            side = Vector3.UnitX;

        float headLen = MathF.Min((end - start).Length() * 0.2f, 0.2f);
        var headBase = end - dir * headLen;
        _lineRenderer.AddLine(end, headBase + side * (headLen * 0.4f), glow);
        _lineRenderer.AddLine(end, headBase - side * (headLen * 0.4f), glow);
    }

    private void AppendPreviewTrajectory()
    {
        var color = new Vector4(0.75f, 0.9f, 1.0f, 0.8f);
        for (int i = 1; i < _previewTrajectory.Count; i++)
            _lineRenderer.AddLine(_previewTrajectory[i - 1] - _currentFrameOrigin, _previewTrajectory[i] - _currentFrameOrigin, color);
    }

    private static Vector4 GetTrailColor(BodyType type) => type switch
    {
        BodyType.Star => new Vector4(1.0f, 0.9f, 0.3f, 0.35f),
        BodyType.Planet or BodyType.RockyPlanet => new Vector4(0.2f, 0.5f, 1.0f, 0.35f),
        BodyType.GasGiant => new Vector4(0.85f, 0.72f, 0.55f, 0.35f),
        BodyType.Moon => new Vector4(0.8f, 0.8f, 0.8f, 0.3f),
        BodyType.Asteroid or BodyType.Comet => new Vector4(0.6f, 0.6f, 0.55f, 0.3f),
        BodyType.NeutronStar => new Vector4(0.65f, 0.88f, 1.0f, 0.35f),
        BodyType.BlackHole => new Vector4(0.75f, 0.6f, 1.0f, 0.35f),
        _ => new Vector4(0.85f, 0.85f, 0.85f, 0.3f),
    };

    private void UpdateBlackHoleQualityTier(float deltaTime)
    {
        if (deltaTime <= 0.0f)
            return;

        // Target per-tier frame-cost budgets (ms) for black-hole effects.
        float budgetMs = BlackHoleQualityTier switch
        {
            BlackHoleVisualQuality.Low => 0.6f,
            BlackHoleVisualQuality.Medium => 1.2f,
            _ => 2.5f,
        };

        // Approximate local budget pressure from frame delta.
        float frameMs = deltaTime * 1000.0f;
        float pressure = frameMs * 0.18f;

        if (pressure > budgetMs)
        {
            _qualityBudgetStrikeCount++;
            _qualityRecoveryCount = 0;
        }
        else
        {
            _qualityRecoveryCount++;
            _qualityBudgetStrikeCount = System.Math.Max(0, _qualityBudgetStrikeCount - 1);
        }

        if (_qualityBudgetStrikeCount > 60 && BlackHoleQualityTier > BlackHoleVisualQuality.Low)
        {
            BlackHoleQualityTier = (BlackHoleVisualQuality)((int)BlackHoleQualityTier - 1);
            _qualityBudgetStrikeCount = 0;
            _qualityRecoveryCount = 0;
        }
        else if (_qualityRecoveryCount > 480 && BlackHoleQualityTier < BlackHoleVisualQuality.High)
        {
            BlackHoleQualityTier = (BlackHoleVisualQuality)((int)BlackHoleQualityTier + 1);
            _qualityRecoveryCount = 0;
        }
    }

    public void Dispose()
    {
        _sphereRenderer.Dispose();
        _effectSphereRenderer.Dispose();
        _accretionDiskRenderer?.Dispose();
        _albedoAtlas?.Dispose();
        _gridRenderer.Dispose();
        _lineRenderer.Dispose();
        _backgroundRenderer.Dispose();
        _sphereShader?.Dispose();
        _gridShader?.Dispose();
        _lineShader?.Dispose();
        _backgroundShader?.Dispose();
    }
}
