using System.Numerics;
using Silk.NET.OpenGL;
using CelestialMechanics.Simulation;

namespace CelestialMechanics.Renderer;

public class GLRenderer : IDisposable
{
    private GL? _gl;
    private InstancedSphereRenderer _sphereRenderer = new();
    private GridRenderer _gridRenderer = new();
    private LineRenderer _lineRenderer = new();
    private ShaderProgram? _sphereShader;
    private ShaderProgram? _gridShader;
    private ShaderProgram? _lineShader;
    private Camera _camera = new();
    private RenderState _renderState = new();

    public Camera Camera => _camera;
    public RenderState RenderState => _renderState;
    public bool ShowGrid { get; set; } = true;
    public bool ShowVelocityArrows { get; set; } = false;

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

        _sphereRenderer.Initialize(gl);
        _gridRenderer.Initialize(gl);
        _lineRenderer.Initialize(gl);
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

        // Update sphere instances
        _sphereRenderer.UpdateInstances(_renderState.Bodies, _renderState.BodyCount);

        // Update velocity arrows if enabled
        if (ShowVelocityArrows && engine.Bodies != null)
        {
            _lineRenderer.Clear();
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
            _lineRenderer.Upload();
        }
    }

    public void Render(float deltaTime, int width, int height)
    {
        if (_gl == null) return;

        _camera.Update(deltaTime);

        float aspect = width / (float)System.Math.Max(height, 1);
        var view = _camera.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix(aspect);
        var viewPos = _camera.Position;

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
        _sphereShader!.Use();
        _sphereShader.SetUniform("uView", view);
        _sphereShader.SetUniform("uProjection", projection);
        _sphereShader.SetUniform("uViewPos", viewPos);
        _sphereRenderer.Render(_gl, _sphereShader);

        // Render velocity arrows
        if (ShowVelocityArrows)
        {
            _lineShader!.Use();
            _lineShader.SetUniform("uView", view);
            _lineShader.SetUniform("uProjection", projection);
            _lineRenderer.Render(_gl, _lineShader);
        }
    }

    public void Dispose()
    {
        _sphereRenderer.Dispose();
        _gridRenderer.Dispose();
        _lineRenderer.Dispose();
        _sphereShader?.Dispose();
        _gridShader?.Dispose();
        _lineShader?.Dispose();
    }
}
