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
    private const int MaxEffectBodiesCapacity = 12000;
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

    private struct LightSource
    {
        public Vector3 Position;
        public Vector3 Color;
        public float Intensity;
    }

    private struct ExplosionEvent
    {
        public Vector3 Position;
        public Vector4 Color;
        public float Radius;
        public float MaxRadius;
        public float ExpansionSpeed;
        public float Age;
        public float Lifetime;
        public float Brightness;
        public EmissionTier Tier;
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
    private readonly RenderSettings _settings;
    private readonly List<CollisionFlash> _collisionFlashes = new(64);
    private double _lastCollisionEventTime = double.NegativeInfinity;
    private float _bhParticleHeat;
    private float _bhParticleDensity;
    private int _qualityBudgetStrikeCount;
    private int _qualityRecoveryCount;
    private const int MaxStarLightsCapacity = 8;
    private const int MaxRayOccluders = 24;
    private readonly Vector4[] _starLightData = new Vector4[MaxStarLightsCapacity];
    private readonly Vector3[] _starLightColorData = new Vector3[MaxStarLightsCapacity];
    private readonly Vector4[] _rayOccluderData = new Vector4[MaxRayOccluders];
    private readonly LightSource[] _activeEmitters = new LightSource[MaxStarLightsCapacity];
    private int _starLightCount;
    private int _rayOccluderCount;
    private Vector3 _currentFrameOrigin;
    private readonly List<ExplosionEvent> _explosions = new(32);
    private uint _reflectionFbo;
    private uint _reflectionColorTex;
    private uint _reflectionDepthTex;
    private int _reflectionBufferWidth;
    private int _reflectionBufferHeight;

    public GLRenderer(RenderSettings settings, ISelectionContext? selectionContext = null)
    {
        _settings = settings ?? new RenderSettings();
        _selectionContext = selectionContext;
    }

    public GLRenderer(ISelectionContext? selectionContext = null)
        : this(new RenderSettings(), selectionContext)
    {
    }

    public Camera Camera => _camera;
    public RenderState RenderState => _renderState;
    public RenderSettings Settings => _settings;
    public ReferenceFrameManager ReferenceFrame { get; } = new();
    public bool ShowGrid { get; set; } = true;
    public bool ShowVelocityArrows { get; set; } = false;
    public bool ShowBackground { get; set; } = true;
    public bool ShowOrbitalTrails
    {
        get => _settings.EnableTrails;
        set => _settings.EnableTrails = value;
    }
    public bool ShowPersistentOrbitPaths { get; set; } = true;
    public bool ShowPredictedTrajectory { get; set; } = true;
    public bool ShowAccretionDisks
    {
        get => _settings.EnableParticles;
        set => _settings.EnableParticles = value;
    }
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

        AppendCollisionBursts(engine);

        if (_renderState.BodyCount == 0 && engine.CurrentState.CollisionBursts.Count == 0)
        {
            _collisionFlashes.Clear();
            _explosions.Clear();
        }

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
            ReadOnlySpan<DiskParticle> particles = _settings.EnableParticles && ShowAccretionDisks
                ? engine.GetAccretionParticles()
                : ReadOnlySpan<DiskParticle>.Empty;

            int cappedParticles = System.Math.Clamp(_settings.MaxParticles, 250, AccretionRendererCapacity);
            if (particles.Length > cappedParticles)
                particles = particles.Slice(0, cappedParticles);

            _accretionDiskRenderer.UpdateParticles(particles);
            _bhParticleHeat = _accretionDiskRenderer.AverageTemperatureNormalized;
            _bhParticleDensity = _accretionDiskRenderer.ActiveParticleRatio;
        }

        _lineRenderer.Clear();

        if (engine.Bodies != null && ShowPersistentOrbitPaths && _settings.EnableTrails)
        {
            UpdateOrbitPaths(engine.Bodies);
            AppendOrbitPathLines(engine.Bodies);
        }

        if (ShowOrbitalTrails && engine.Bodies != null && _settings.EnableTrails)
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
        int estimatedFromFlashes = _collisionFlashes.Count * 2;
        int estimatedFromExplosions = _explosions.Count * 3;
        int estimatedParticles = System.Math.Min(_settings.MaxExplosionParticles, _explosions.Count * 180);
        int needed = System.Math.Min(MaxEffectBodiesCapacity, estimatedFromFlashes + estimatedFromExplosions + estimatedParticles + 16);
        if (_effectBodies.Length < needed)
            _effectBodies = new RenderBody[System.Math.Max(needed, 32)];

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

        if (!_settings.EnableExplosions)
            return;

        int maxExplosionParticles = System.Math.Clamp(_settings.MaxExplosionParticles, 64, MaxEffectBodiesCapacity);
        int particleBudgetPerExplosion = _explosions.Count == 0
            ? 0
            : System.Math.Max(24, maxExplosionParticles / _explosions.Count);

        for (int i = 0; i < _explosions.Count; i++)
        {
            var explosion = _explosions[i];
            float lifeT = explosion.Age / System.Math.Max(explosion.Lifetime, 1e-5f);
            float fade = System.Math.Clamp(1.0f - lifeT, 0.0f, 1.0f);
            var framePosition = explosion.Position - _currentFrameOrigin;

            float coreRadius = System.MathF.Max(explosion.Radius * 0.18f, 0.04f);
            float shockRadius = System.MathF.Max(explosion.Radius, 0.08f);
            float shellRadius = System.MathF.Max(explosion.Radius * 1.25f, 0.1f);

            float tierBoost = System.Math.Clamp(RenderSettings.GetTierMultiplier(explosion.Tier) / 200.0f, 1.0f, 12.0f);
            AddEffectBody(
                framePosition,
                coreRadius,
                new Vector4(1.0f, 0.98f, 0.96f, 0.35f * fade),
                new Vector4(7.0f, explosion.Brightness * tierBoost, 1.8f * tierBoost, 0.25f));

            AddEffectBody(
                framePosition,
                shockRadius,
                new Vector4(explosion.Color.X, explosion.Color.Y, explosion.Color.Z, 0.22f * fade),
                new Vector4(7.0f, explosion.Brightness * 0.75f * tierBoost, 1.3f * tierBoost, 0.7f));

            AddEffectBody(
                framePosition,
                shellRadius,
                new Vector4(1.0f, 0.75f, 0.42f, 0.14f * fade),
                new Vector4(7.0f, explosion.Brightness * 0.45f * tierBoost, 1.1f, 0.95f));

            float cameraDistance = Vector3.Distance(_camera.Position, framePosition);
            float lod = cameraDistance > 40.0f ? 0.25f : (cameraDistance > 16.0f ? 0.5f : 1.0f);
            int ejectaCount = System.Math.Clamp((int)(particleBudgetPerExplosion * lod), 12, particleBudgetPerExplosion);

            for (int p = 0; p < ejectaCount; p++)
            {
                if (_effectBodyCount >= _effectBodies.Length)
                    break;

                float phase = (p + 1) * 0.6180339f;
                float theta = 6.2831853f * (phase - System.MathF.Floor(phase));
                float phi = System.MathF.Acos(System.Math.Clamp(1.0f - 2.0f * (p + 0.5f) / (ejectaCount + 0.5f), -1.0f, 1.0f));
                Vector3 dir = new(
                    System.MathF.Sin(phi) * System.MathF.Cos(theta),
                    System.MathF.Cos(phi),
                    System.MathF.Sin(phi) * System.MathF.Sin(theta));

                float particleRadius = 0.0035f + 0.008f * (1.0f - lifeT);
                float ringOffset = shockRadius * (0.58f + 0.45f * ((p % 19) / 19.0f));
                var pos = framePosition + dir * ringOffset;
                var col = Vector4.Lerp(new Vector4(1.0f, 0.96f, 0.88f, 0.22f), new Vector4(0.95f, 0.42f, 0.16f, 0.08f), lifeT);

                AddEffectBody(
                    pos,
                    particleRadius,
                    col,
                    new Vector4(7.0f, explosion.Brightness * 0.15f, 0.95f, 0.7f));
            }
        }
    }

    private void BuildStarLightsAndOccluders()
    {
        _starLightCount = 0;
        _rayOccluderCount = 0;
        int maxConfiguredLights = System.Math.Clamp(_settings.MaxStarLights, 1, MaxStarLightsCapacity);

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
            if (!emitsLight || _starLightCount >= maxConfiguredLights)
                continue;

            float baseLuminosity = System.MathF.Max(body.VisualParams.Y, 0.05f) * StarLightIntensityScale;
            float massFactor = System.MathF.Sqrt(System.MathF.Max(body.Mass, 0.01f));
            float temperatureFactor = body.StarTemperatureK > 0.0f
                ? System.Math.Clamp((body.StarTemperatureK - 2400.0f) / 11000.0f, 0.35f, 4.0f)
                : 1.0f;

            float intensity = System.MathF.Max(0.1f, baseLuminosity * massFactor * temperatureFactor);
            Vector3 color = body.StarTemperatureK > 0.0f
                ? BlackbodyColorApprox(body.StarTemperatureK)
                : Vector3.Clamp(new Vector3(body.Color.X, body.Color.Y, body.Color.Z), Vector3.Zero, Vector3.One);

            _activeEmitters[_starLightCount] = new LightSource
            {
                Position = body.Position,
                Color = color,
                Intensity = intensity,
            };

            _starLightData[_starLightCount] = new Vector4(body.Position.X, body.Position.Y, body.Position.Z, intensity);
            _starLightColorData[_starLightCount] = color;
            _starLightCount++;
        }

        if (_settings.EnableExplosions)
        {
            for (int i = 0; i < _explosions.Count && _starLightCount < maxConfiguredLights; i++)
            {
                var explosion = _explosions[i];
                float ageFactor = 1.0f - System.Math.Clamp(explosion.Age / System.Math.Max(explosion.Lifetime, 1e-5f), 0.0f, 1.0f);
                float tierScale = RenderSettings.GetTierMultiplier(explosion.Tier) / 50.0f;
                float intensity = System.MathF.Max(0.12f, explosion.Brightness * tierScale * ageFactor);

                _activeEmitters[_starLightCount] = new LightSource
                {
                    Position = explosion.Position - _currentFrameOrigin,
                    Color = new Vector3(explosion.Color.X, explosion.Color.Y, explosion.Color.Z),
                    Intensity = intensity,
                };

                _starLightData[_starLightCount] = new Vector4(
                    explosion.Position.X - _currentFrameOrigin.X,
                    explosion.Position.Y - _currentFrameOrigin.Y,
                    explosion.Position.Z - _currentFrameOrigin.Z,
                    intensity);
                _starLightColorData[_starLightCount] = new Vector3(explosion.Color.X, explosion.Color.Y, explosion.Color.Z);
                _starLightCount++;
            }
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

    private void AppendCollisionBursts(SimulationEngine engine)
    {
        SimulationState state = engine.CurrentState;
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

            if (_settings.EnableExplosions)
            {
                EmissionTier tier = DetermineEmissionTier(burst);
                AddExplosionEvent(
                    position: new Vector3((float)burst.Position.X, (float)burst.Position.Y, (float)burst.Position.Z),
                    tier: tier,
                    combinedMass: (float)System.Math.Max(burst.CombinedMass, 1e-5),
                    releasedEnergy: (float)System.Math.Max(burst.ReleasedEnergy, 1e-5),
                    bindingEnergy: (float)System.Math.Max(burst.BindingEnergy, 0.0),
                    expansionVelocityMps: (float)System.Math.Max(burst.ExpansionVelocity, 0.0),
                    luminosityW: (float)System.Math.Max(burst.Luminosity, 0.0),
                    eventHorizonAbsorption: burst.EventHorizonAbsorption);
            }
        }

        _lastCollisionEventTime = state.Time;
    }

    private void AddExplosionEvent(
        Vector3 position,
        EmissionTier tier,
        float combinedMass,
        float releasedEnergy,
        float bindingEnergy,
        float expansionVelocityMps,
        float luminosityW,
        bool eventHorizonAbsorption)
    {
        if (eventHorizonAbsorption)
            return;

        float tierMultiplier = RenderSettings.GetTierMultiplier(tier);
        float massScale = System.Math.Clamp(System.MathF.Cbrt(System.MathF.Max(combinedMass, 1e-4f)), 0.6f, 8.0f);
        float energyRatio = bindingEnergy > 0.0f
            ? releasedEnergy / bindingEnergy
            : System.MathF.Log10(1.0f + releasedEnergy);
        float energyScale = System.Math.Clamp(System.MathF.Log10(1.0f + System.MathF.Max(energyRatio, 0.0f) * 10.0f), 0.05f, 4.0f);

        float expansionSpeed = expansionVelocityMps > 0.0f
            ? System.Math.Clamp(expansionVelocityMps / 15000.0f, 0.3f, 32.0f)
            : System.Math.Clamp(1.2f + 4.2f * energyScale, 0.3f, 32.0f);

        float maxRadius = System.Math.Clamp(
            0.18f + 0.2f * massScale + expansionSpeed * 0.55f,
            0.15f,
            _settings.MaxExplosionRadius);

        float luminosityScale = luminosityW > 0.0f
            ? System.Math.Clamp((float)CelestialMechanics.Physics.Astrophysics.Units.RenderScale(luminosityW / 1.0e20f), 0.2f, 12.0f)
            : System.Math.Clamp(energyScale * 2.0f, 0.2f, 12.0f);

        float brightness = System.Math.Clamp((tierMultiplier / 200.0f) * (0.3f + 0.4f * luminosityScale + 0.2f * energyScale), 0.25f, 24.0f);
        float lifetime = System.Math.Clamp(maxRadius / System.Math.Max(expansionSpeed, 0.01f), 0.8f, 18.0f);

        Vector4 color = tier switch
        {
            EmissionTier.Supernova => new Vector4(0.92f, 0.66f, 0.38f, 1.0f),
            EmissionTier.Kilonova => new Vector4(0.70f, 0.83f, 1.0f, 1.0f),
            EmissionTier.BigBang => new Vector4(1.0f, 0.97f, 0.92f, 1.0f),
            _ => new Vector4(1.0f, 0.86f, 0.58f, 1.0f),
        };

        _explosions.Add(new ExplosionEvent
        {
            Position = position,
            Color = color,
            Radius = 0.06f,
            MaxRadius = maxRadius,
            ExpansionSpeed = expansionSpeed,
            Age = 0.0f,
            Lifetime = lifetime,
            Brightness = brightness,
            Tier = tier,
        });

        while (_explosions.Count > 24)
            _explosions.RemoveAt(0);
    }

    private static EmissionTier DetermineEmissionTier(in CollisionBurstEvent burst)
    {
        double ratio = burst.BindingEnergy > 0.0
            ? burst.ReleasedEnergy / burst.BindingEnergy
            : burst.ReleasedEnergy;

        if (ratio > 5.0 || burst.CombinedMass > 20.0)
            return EmissionTier.BigBang;

        if (ratio > 2.0 || burst.CombinedMass > 7.0)
            return EmissionTier.Kilonova;

        if (ratio > 0.8 || burst.CombinedMass > 2.4)
            return EmissionTier.Supernova;

        return EmissionTier.Star;
    }

    private static Vector3 BlackbodyColorApprox(float temperatureK)
    {
        float t = System.Math.Clamp(temperatureK, 1000.0f, 50000.0f);
        if (t < 3500.0f)
        {
            float f = (t - 1000.0f) / 2500.0f;
            return Vector3.Lerp(new Vector3(1.0f, 0.10f, 0.0f), new Vector3(1.0f, 0.55f, 0.10f), f);
        }

        if (t < 6500.0f)
        {
            float f = (t - 3500.0f) / 3000.0f;
            return Vector3.Lerp(new Vector3(1.0f, 0.55f, 0.10f), new Vector3(1.0f, 0.95f, 0.90f), f);
        }

        if (t < 15000.0f)
        {
            float f = (t - 6500.0f) / 8500.0f;
            return Vector3.Lerp(new Vector3(1.0f, 0.95f, 0.90f), new Vector3(0.7f, 0.8f, 1.0f), f);
        }

        float highF = System.Math.Clamp((t - 15000.0f) / 35000.0f, 0.0f, 1.0f);
        return Vector3.Lerp(new Vector3(0.7f, 0.8f, 1.0f), new Vector3(0.4f, 0.5f, 1.0f), highF);
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

    private void UpdateExplosions(float dt)
    {
        if (dt <= 0.0f)
            return;

        if (_settings.EnableBigBangMode && _explosions.Count == 0)
        {
            AddExplosionEvent(
                Vector3.Zero,
                EmissionTier.BigBang,
                combinedMass: 30.0f,
                releasedEnergy: 2.0e7f,
                bindingEnergy: 1.0e6f,
                expansionVelocityMps: 2.0e7f,
                luminosityW: 5.0e30f,
                eventHorizonAbsorption: false);
            _settings.EnableBigBangMode = false;
        }

        for (int i = _explosions.Count - 1; i >= 0; i--)
        {
            var explosion = _explosions[i];
            explosion.Age += dt;
            explosion.Radius = System.Math.Clamp(
                explosion.Radius + explosion.ExpansionSpeed * dt,
                0.04f,
                System.Math.Min(_settings.MaxExplosionRadius, explosion.MaxRadius));

            if (explosion.Age >= explosion.Lifetime || explosion.Radius >= _settings.MaxExplosionRadius)
            {
                _explosions.RemoveAt(i);
                continue;
            }

            _explosions[i] = explosion;
        }
    }

    public void Render(float deltaTime, int width, int height)
    {
        if (_gl == null) return;

        ApplyAdaptivePerformanceControl(deltaTime);

        bool renderOnlyParticles = _settings.DebugOnlyParticles;
        bool renderOnlyWaves = _settings.DebugOnlyWaves;
        bool renderSceneBodies = !renderOnlyParticles;
        bool renderParticles = _settings.EnableParticles && !renderOnlyWaves;

        float exposure = System.Math.Clamp(_settings.Exposure, 0.1f, 4.0f);
        float bloomEnabled = _settings.EnableBloom ? 1.0f : 0.0f;
        float bloomIntensity = System.Math.Clamp(_settings.BloomIntensity, 0.0f, 4.0f);
        float bloomRadiusScale = System.Math.Clamp(_settings.BloomRadius / 5.0f, 0.2f, 2.5f);
        float starEmission = System.Math.Clamp(_settings.StarEmissionMultiplier, 0.0f, 4.0f);
        float particleEmission = System.Math.Clamp(_settings.ParticleEmissionMultiplier * _settings.ParticleEmissionScale, 0.0f, 4.0f);

        if (AutoBlackHoleQuality)
            UpdateBlackHoleQualityTier(deltaTime);

        _timeSeconds += deltaTime;
        UpdateCollisionFlashes(System.Math.Max(deltaTime, 1e-5f));
        UpdateExplosions(System.Math.Max(deltaTime, 1e-5f));
        _camera.Update(deltaTime);

        float aspect = width / (float)System.Math.Max(height, 1);
        var view = _camera.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix(aspect);
        var viewPos = _camera.Position;

        EnsureReflectionBuffers(width, height);

        if (_settings.EnableReflections && ShowBackground && _reflectionFbo != 0)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _reflectionFbo);
            _gl.Viewport(0, 0, (uint)_reflectionBufferWidth, (uint)_reflectionBufferHeight);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            RenderBackgroundPass(_reflectionBufferWidth, _reflectionBufferHeight, viewPos, exposure, starEmission);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
        }

        if (ShowBackground && !renderOnlyParticles)
        {
            RenderBackgroundPass(width, height, viewPos, exposure, starEmission);
        }

        // Render grid
        if (ShowGrid && renderSceneBodies)
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
        if (renderSceneBodies)
        {
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
            _sphereShader.SetUniform("uGlobalLuminosity", GlobalLuminosityScale * starEmission * exposure);
            _sphereShader.SetUniform("uGlobalGlow", GlobalGlowScale * starEmission * bloomEnabled * bloomIntensity);
            _sphereShader.SetUniform("uGlobalSaturation", GlobalSaturation);
            _sphereShader.SetUniform("uUseAlbedoAtlas", EnableAlbedoTextureMaps && _albedoAtlas != null ? 1 : 0);
            _sphereShader.SetUniform("uBodyAlbedoAtlas", 1);
            if (_reflectionColorTex != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, _reflectionColorTex);
                _sphereShader.SetUniform("uScreenTexture", 2);
            }

            if (_reflectionDepthTex != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture3);
                _gl.BindTexture(TextureTarget.Texture2D, _reflectionDepthTex);
                _sphereShader.SetUniform("uDepthTexture", 3);
            }

            _sphereShader.SetUniform("uAlbedoBlend", System.Math.Clamp(AlbedoTextureBlend, 0.0f, 1.0f));
            _sphereShader.SetUniform("uEnableStarLighting", EnableStarDrivenLighting && _starLightCount > 0 ? 1 : 0);
            _sphereShader.SetUniform("uStarLightCount", _starLightCount);
            _sphereShader.SetUniform("uStarLightFalloff", System.Math.Clamp(StarLightFalloff, 0.05f, 4.0f));
            _sphereShader.SetUniform("uAmbientFloor", 0.0f);
            _sphereShader.SetUniform("uEnableHdr", _settings.EnableHdr ? 1 : 0);
            _sphereShader.SetUniform("uExposure", exposure);
            _sphereShader.SetUniform("uEnableReflections", _settings.EnableReflections ? 1 : 0);
            _sphereShader.SetUniform("uReflectionScale", System.Math.Clamp(_settings.ReflectionScale, 0.001f, 0.04f));
            _sphereShader.SetUniform("uMaxReflectionSamples", System.Math.Clamp(_settings.MaxReflectionSamples, 1, 16));
            _sphereShader.SetUniform("uResolution", new Vector2(width, height));
            _sphereShader.SetUniform("uEnableGlowScaling", _settings.EnableGlowScaling ? 1 : 0);
            _sphereShader.SetUniform("uGlowDistanceScale", System.Math.Clamp(_settings.GlowDistanceScale, 2.0f, 200.0f));
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
            _sphereShader.SetUniform("uBhBloomScale", System.Math.Clamp(BlackHoleBloomScale * bloomEnabled * bloomIntensity * bloomRadiusScale * particleEmission, 0.0f, 4.0f));
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
        }

        if (renderSceneBodies && _effectBodyCount > 0)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
            _effectSphereRenderer.Render(_gl, _sphereShader!);
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        if (renderParticles && ShowAccretionDisks && _accretionDiskRenderer != null)
        {
            var viewProjection = view * projection;
            _accretionDiskRenderer.ConfigureBlackHoleVisuals(
                qualityTier: (int)BlackHoleQualityTier,
                preset: (int)BlackHolePreset,
                dopplerBoost: System.Math.Clamp(BlackHoleDopplerBoost, 0.0f, 3.0f),
                opticalDepth: System.Math.Clamp(BlackHoleOpticalDepth, 0.0f, 4.0f),
                temperatureScale: System.Math.Clamp(BlackHoleTemperatureScale, 0.25f, 3.0f),
                bloomScale: System.Math.Clamp(BlackHoleBloomScale * bloomEnabled * bloomIntensity * bloomRadiusScale * particleEmission, 0.0f, 4.0f),
                debugMode: (int)BlackHoleDebugMode);
            _accretionDiskRenderer.Draw(viewProjection);
        }

        // Render line overlays (trails, velocity arrows, placement vectors)
        if (_lineRenderer.HasLines && !renderOnlyParticles)
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

    private void ApplyAdaptivePerformanceControl(float deltaTime)
    {
        if (deltaTime <= 0.0f)
            return;

        float fps = 1.0f / System.Math.Max(deltaTime, 1e-4f);
        if (fps >= 50.0f)
            return;

        _settings.MaxParticles = System.Math.Clamp((int)(_settings.MaxParticles * 0.9f), 250, AccretionRendererCapacity);
        _settings.BloomRadius = System.Math.Clamp(_settings.BloomRadius * 0.9f, 1.0f, 20.0f);
        _settings.MaxReflectionSamples = System.Math.Clamp(_settings.MaxReflectionSamples - 1, 1, 16);
        _settings.MaxExplosionRadius = System.Math.Clamp(_settings.MaxExplosionRadius * 0.98f, 4.0f, 80.0f);
    }

    private void RenderBackgroundPass(int width, int height, Vector3 viewPos, float exposure, float starEmission)
    {
        if (_gl == null)
            return;

        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);

        _backgroundShader!.Use();
        _backgroundShader.SetUniform("uTime", _timeSeconds);
        _backgroundShader.SetUniform("uResolution", new Vector2(width, height));
        _backgroundShader.SetUniform("uCameraPos", viewPos);
        _backgroundShader.SetUniform("uExposure", exposure);
        _backgroundShader.SetUniform("uStarEmissionMultiplier", starEmission);
        _backgroundShader.SetUniform("uNebulaEmissionMultiplier", System.Math.Clamp(_settings.NebulaEmissionMultiplier, 0.0f, 4.0f));
        _backgroundShader.SetUniform("uFogDensity", System.Math.Clamp(_settings.FogDensity, 0.0f, 0.2f));
        _backgroundShader.SetUniform("uFogColor", _settings.FogColor);
        _backgroundRenderer.Render(_gl);

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
    }

    private unsafe void EnsureReflectionBuffers(int width, int height)
    {
        if (_gl == null)
            return;

        int safeWidth = System.Math.Max(1, width);
        int safeHeight = System.Math.Max(1, height);
        if (_reflectionFbo != 0 && safeWidth == _reflectionBufferWidth && safeHeight == _reflectionBufferHeight)
            return;

        ReleaseReflectionBuffers();

        _reflectionBufferWidth = safeWidth;
        _reflectionBufferHeight = safeHeight;

        _reflectionFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _reflectionFbo);

        _reflectionColorTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _reflectionColorTex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)_reflectionBufferWidth, (uint)_reflectionBufferHeight, 0, PixelFormat.Rgba, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _reflectionColorTex, 0);

        _reflectionDepthTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _reflectionDepthTex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, (uint)_reflectionBufferWidth, (uint)_reflectionBufferHeight, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _reflectionDepthTex, 0);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void ReleaseReflectionBuffers()
    {
        if (_gl == null)
            return;

        if (_reflectionColorTex != 0)
        {
            _gl.DeleteTexture(_reflectionColorTex);
            _reflectionColorTex = 0;
        }

        if (_reflectionDepthTex != 0)
        {
            _gl.DeleteTexture(_reflectionDepthTex);
            _reflectionDepthTex = 0;
        }

        if (_reflectionFbo != 0)
        {
            _gl.DeleteFramebuffer(_reflectionFbo);
            _reflectionFbo = 0;
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
        ReleaseReflectionBuffers();
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
