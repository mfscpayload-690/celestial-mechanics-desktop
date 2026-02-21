using System.Numerics;
using Silk.NET.OpenGL;
using CelestialMechanics.Physics.Extensions;

namespace CelestialMechanics.Renderer;

/// <summary>
/// Point-sprite renderer for accretion disk particles.
///
/// Uploads active particle positions and temperatures to a VBO each frame,
/// then renders as GL_POINTS with point sprites enabled.
/// Temperature maps to a blackbody colour ramp in the fragment shader.
/// </summary>
public sealed class AccretionDiskRenderer : IDisposable
{
    private readonly GL _gl;
    private ShaderProgram? _shader;
    private uint _vao;
    private uint _vbo;
    private bool _initialized;

    // Interleaved vertex data: [posX, posY, posZ, temperature, age, maxAge]
    private const int FLOATS_PER_PARTICLE = 6;
    private float[] _vertexData = Array.Empty<float>();
    private int _particleCount;

    /// <summary>Point size scale factor (adjust for screen DPI).</summary>
    public float PointScale { get; set; } = 1.0f;

    public AccretionDiskRenderer(GL gl)
    {
        _gl = gl;
    }

    /// <summary>
    /// Initialize GPU resources. Call once after GL context is ready.
    /// </summary>
    public void Initialize(int maxParticles = 5000)
    {
        if (_initialized) return;

        _vertexData = new float[maxParticles * FLOATS_PER_PARTICLE];

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // Allocate buffer (dynamic draw since updated every frame)
        unsafe
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(_vertexData.Length * sizeof(float)),
                null, BufferUsageARB.DynamicDraw);
        }

        uint stride = FLOATS_PER_PARTICLE * sizeof(float);

        // Position: location 0
        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0); }

        // Temperature: location 1
        _gl.EnableVertexAttribArray(1);
        unsafe { _gl.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float))); }

        // Age: location 2
        _gl.EnableVertexAttribArray(2);
        unsafe { _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float))); }

        // MaxAge: location 3
        _gl.EnableVertexAttribArray(3);
        unsafe { _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(5 * sizeof(float))); }

        _gl.BindVertexArray(0);

        // Load shader
        string basePath = AppContext.BaseDirectory;
        string vertSrc = File.ReadAllText(Path.Combine(basePath, "Shaders", "accretion.vert"));
        string fragSrc = File.ReadAllText(Path.Combine(basePath, "Shaders", "accretion.frag"));
        _shader = new ShaderProgram(_gl, vertSrc, fragSrc);

        _initialized = true;
    }

    /// <summary>
    /// Upload active particle data from the accretion disk system.
    /// </summary>
    public void UpdateParticles(ReadOnlySpan<DiskParticle> particles)
    {
        if (!_initialized) return;

        _particleCount = 0;

        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly var p = ref particles[i];
            if (!p.IsActive) continue;

            if (_particleCount * FLOATS_PER_PARTICLE + FLOATS_PER_PARTICLE > _vertexData.Length)
                break;

            int offset = _particleCount * FLOATS_PER_PARTICLE;
            _vertexData[offset + 0] = (float)p.PosX;
            _vertexData[offset + 1] = (float)p.PosY;
            _vertexData[offset + 2] = (float)p.PosZ;
            _vertexData[offset + 3] = (float)p.Temperature;
            _vertexData[offset + 4] = (float)p.Age;
            _vertexData[offset + 5] = (float)p.MaxAge;
            _particleCount++;
        }

        // Upload to GPU
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = _vertexData)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                    (nuint)(_particleCount * FLOATS_PER_PARTICLE * sizeof(float)), ptr);
            }
        }
    }

    /// <summary>
    /// Draw the accretion disk particles.
    /// </summary>
    public unsafe void Draw(Matrix4x4 viewProjection)
    {
        if (!_initialized || _shader == null || _particleCount == 0)
            return;

        _gl.Enable(EnableCap.ProgramPointSize);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false); // Don't write to depth buffer

        _shader.Use();
        _shader.SetUniform("uViewProjection", viewProjection);
        _shader.SetUniform("uPointScale", PointScale);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_particleCount);
        _gl.BindVertexArray(0);

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ProgramPointSize);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }
}
