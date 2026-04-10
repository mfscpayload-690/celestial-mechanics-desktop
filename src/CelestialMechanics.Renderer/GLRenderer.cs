using System.Numerics;
using Silk.NET.OpenGL;
using CelestialMechanics.Simulation;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Extensions;

namespace CelestialMechanics.Renderer;

public class GLRenderer : IDisposable
{
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
    private readonly List<CollisionFlash> _collisionFlashes = new(64);
    private double _lastCollisionEventTime = double.NegativeInfinity;

    public Camera Camera => _camera;
    public RenderState RenderState => _renderState;
    public bool ShowGrid { get; set; } = true;
    public bool ShowVelocityArrows { get; set; } = false;
    public bool ShowBackground { get; set; } = true;
    public bool ShowOrbitalTrails { get; set; } = true;
    public bool ShowAccretionDisks { get; set; } = true;
    public int MaxTrailPoints { get; set; } = 24;
    public float TrailMinDistance { get; set; } = 0.0035f;
    public float GlobalLuminosityScale { get; set; } = 1.18f;
    public float GlobalGlowScale { get; set; } = 1.15f;
    public float GlobalSaturation { get; set; } = 1.03f;

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

    public void ClearPlacementPreview()
    {
        _showGhost = false;
        _showDirectionVector = false;
        _previewTrajectory.Clear();
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
        _accretionDiskRenderer = new AccretionDiskRenderer(gl);
        _accretionDiskRenderer.Initialize(6000);
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
        _renderState.UpdateFrom(engine);

        AppendCollisionBursts(engine.CurrentState);
        BuildCollisionEffectBodies();

        int compositeCount = _renderState.BodyCount + (_showGhost ? 1 : 0);
        if (_compositeBodies.Length < compositeCount)
            _compositeBodies = new RenderBody[compositeCount];

        if (_renderState.BodyCount > 0)
            Array.Copy(_renderState.Bodies, _compositeBodies, _renderState.BodyCount);

        if (_showGhost)
            _compositeBodies[_renderState.BodyCount] = _ghostBody;

        // Update sphere instances
        _sphereRenderer.UpdateInstances(_compositeBodies, compositeCount);
        _effectSphereRenderer.UpdateInstances(_effectBodies, _effectBodyCount);

        if (_accretionDiskRenderer != null)
        {
            ReadOnlySpan<DiskParticle> particles = ShowAccretionDisks
                ? engine.GetAccretionParticles()
                : ReadOnlySpan<DiskParticle>.Empty;

            _accretionDiskRenderer.UpdateParticles(particles);
        }

        _lineRenderer.Clear();

        if (ShowOrbitalTrails && engine.Bodies != null)
        {
            UpdateTrails(engine.Bodies);
            AppendTrailLines(engine.Bodies);
        }

        // Update velocity arrows if enabled
        if (ShowVelocityArrows && engine.Bodies != null)
        {
            foreach (var body in engine.Bodies)
            {
                if (!body.IsActive) continue;
                var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);
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

            AddEffectBody(
                fx.Position,
                coreRadius,
                new Vector4(0.98f, 0.95f, 0.88f, 0.32f * fx.Intensity * fade),
                new Vector4(7.0f, 1.55f * fx.Intensity, 1.25f * fx.Intensity, 0.55f));

            AddEffectBody(
                fx.Position,
                plumeRadius,
                new Vector4(0.62f, 0.76f, 1.0f, 0.19f * fx.Intensity * fade),
                new Vector4(7.0f, 0.95f * fx.Intensity, 1.05f * fx.Intensity, 0.85f));
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
            _gridRenderer.Render(_gl, _gridShader);
            _gl.Disable(EnableCap.Blend);
        }

        // Render bodies
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _sphereShader!.Use();
        _sphereShader.SetUniform("uView", view);
        _sphereShader.SetUniform("uProjection", projection);
        _sphereShader.SetUniform("uViewPos", viewPos);
        _sphereShader.SetUniform("uTime", _timeSeconds);
        _sphereShader.SetUniform("uGlobalLuminosity", GlobalLuminosityScale);
        _sphereShader.SetUniform("uGlobalGlow", GlobalGlowScale);
        _sphereShader.SetUniform("uGlobalSaturation", GlobalSaturation);
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

            Vector3? prev = null;
            foreach (var point in trail)
            {
                if (prev.HasValue)
                    _lineRenderer.AddLine(prev.Value, point, color);
                prev = point;
            }
        }
    }

    private void AppendDirectionVector()
    {
        var glow = new Vector4(1.0f, 1.0f, 1.0f, 0.95f);
        _lineRenderer.AddLine(_directionVectorStart, _directionVectorEnd, glow);

        var dir = _directionVectorEnd - _directionVectorStart;
        if (dir.LengthSquared() < 1e-8f)
            return;

        dir = Vector3.Normalize(dir);
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        if (side.LengthSquared() < 1e-8f)
            side = Vector3.UnitX;

        float headLen = MathF.Min((_directionVectorEnd - _directionVectorStart).Length() * 0.2f, 0.2f);
        var headBase = _directionVectorEnd - dir * headLen;
        _lineRenderer.AddLine(_directionVectorEnd, headBase + side * (headLen * 0.4f), glow);
        _lineRenderer.AddLine(_directionVectorEnd, headBase - side * (headLen * 0.4f), glow);
    }

    private void AppendPreviewTrajectory()
    {
        var color = new Vector4(0.75f, 0.9f, 1.0f, 0.8f);
        for (int i = 1; i < _previewTrajectory.Count; i++)
            _lineRenderer.AddLine(_previewTrajectory[i - 1], _previewTrajectory[i], color);
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

    public void Dispose()
    {
        _sphereRenderer.Dispose();
        _effectSphereRenderer.Dispose();
        _accretionDiskRenderer?.Dispose();
        _gridRenderer.Dispose();
        _lineRenderer.Dispose();
        _backgroundRenderer.Dispose();
        _sphereShader?.Dispose();
        _gridShader?.Dispose();
        _lineShader?.Dispose();
        _backgroundShader?.Dispose();
    }
}
