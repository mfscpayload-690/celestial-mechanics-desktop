using System.Numerics;
using Silk.NET.OpenGL;
using CelestialMechanics.Math;

namespace CelestialMechanics.Renderer;

/// <summary>
/// Manages gravitational lensing post-processing for compact objects.
///
/// Pipeline:
///   1. Main scene is rendered to an off-screen FBO (scene texture)
///   2. This renderer binds the default framebuffer
///   3. Draws a full-screen quad with the lensing shader
///   4. The shader distorts UVs based on black hole positions/masses
///
/// FBO and shader are lazily initialized on first use.
/// </summary>
public sealed class RelativisticRenderer : IDisposable
{
    private readonly GL _gl;
    private ShaderProgram? _lensingShader;

    // Framebuffer objects for off-screen rendering
    private uint _fbo;
    private uint _sceneTexture;
    private uint _depthRbo;
    private uint _quadVao;

    private int _width;
    private int _height;
    private bool _initialized;

    /// <summary>Overall lensing strength multiplier.</summary>
    public float LensIntensity { get; set; } = 1.0f;

    /// <summary>Glow intensity near event horizons.</summary>
    public float EventHorizonGlow { get; set; } = 0.5f;

    public RelativisticRenderer(GL gl)
    {
        _gl = gl;
    }

    /// <summary>
    /// Initialize or resize the FBO to match viewport dimensions.
    /// </summary>
    public void EnsureInitialized(int width, int height)
    {
        if (_initialized && _width == width && _height == height)
            return;

        // Clean up existing resources
        if (_initialized)
            CleanupFBO();

        _width = width;
        _height = height;

        // Create FBO
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        // Scene colour texture
        _sceneTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _sceneTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _sceneTexture, 0);

        // Depth renderbuffer
        _depthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
            (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthRbo);

        // Verify completeness
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            Console.Error.WriteLine($"[RelativisticRenderer] FBO incomplete: {status}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        // Create empty VAO for full-screen quad (vertex positions generated in shader)
        _quadVao = _gl.GenVertexArray();

        // Load lensing shader
        if (_lensingShader == null)
        {
            string basePath = AppContext.BaseDirectory;
            string vertSrc = File.ReadAllText(Path.Combine(basePath, "Shaders", "lensing.vert"));
            string fragSrc = File.ReadAllText(Path.Combine(basePath, "Shaders", "lensing.frag"));
            _lensingShader = new ShaderProgram(_gl, vertSrc, fragSrc);
        }

        _initialized = true;
    }

    /// <summary>
    /// Begin the off-screen rendering pass. Call this BEFORE rendering the scene.
    /// </summary>
    public void BeginSceneCapture()
    {
        if (!_initialized) return;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// End the off-screen pass and apply the lensing post-process to the default framebuffer.
    /// </summary>
    /// <param name="blackHoles">Array of black hole screen-space data.</param>
    /// <param name="blackHoleCount">Number of active black holes (max 8).</param>
    public unsafe void ApplyLensing(BlackHoleRenderData[] blackHoles, int blackHoleCount)
    {
        if (!_initialized || _lensingShader == null) return;

        // Bind default framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.Disable(EnableCap.DepthTest);

        _lensingShader.Use();

        // Bind scene texture to unit 0
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _sceneTexture);
        _lensingShader.SetUniform("uSceneTexture", 0);

        // Resolution
        _lensingShader.SetUniform("uResolution", new Vector2(_width, _height));

        // Lensing parameters
        _lensingShader.SetUniform("uLensIntensity", LensIntensity);
        _lensingShader.SetUniform("uEventHorizonGlow", EventHorizonGlow);

        // Black hole data
        int count = System.Math.Min(blackHoleCount, 8);
        _lensingShader.SetUniform("uBlackHoleCount", count);

        for (int i = 0; i < count; i++)
        {
            string uniform = $"uBlackHoles[{i}]";
            var bh = blackHoles[i];
            int loc = _lensingShader.GetUniformLocation(uniform);
            if (loc >= 0)
            {
                _gl.Uniform4(loc, bh.ScreenX, bh.ScreenY, bh.RsScreen, bh.LensStrength);
            }
        }

        // Draw full-screen quad
        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        _gl.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Project world-space black hole data to screen space for the lensing shader.
    /// </summary>
    public static BlackHoleRenderData ProjectToScreen(
        Vector3 worldPos, float mass,
        Matrix4x4 viewProjection, int screenWidth, int screenHeight)
    {
        // Project to clip space
        Vector4 clip = Vector4.Transform(new Vector4(worldPos, 1.0f), viewProjection);
        if (clip.W <= 0) // Behind camera
            return new BlackHoleRenderData();

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;

        float screenX = (ndcX * 0.5f + 0.5f) * screenWidth;
        float screenY = (ndcY * 0.5f + 0.5f) * screenHeight;

        // Schwarzschild radius in world units
        float rs = (float)(PhysicalConstants.SchwarzschildFactorSim * mass);

        // Approximate Rs in screen pixels (based on clip-space scale)
        float rsScreen = rs / clip.W * screenHeight * 0.5f;

        // Lensing strength: 4GM/c² in screen-space units
        float lensStr = rsScreen * 2.0f;

        return new BlackHoleRenderData
        {
            ScreenX = screenX,
            ScreenY = screenY,
            RsScreen = System.Math.Max(rsScreen, 1.0f),
            LensStrength = lensStr
        };
    }

    private void CleanupFBO()
    {
        if (_sceneTexture != 0) { _gl.DeleteTexture(_sceneTexture); _sceneTexture = 0; }
        if (_depthRbo != 0) { _gl.DeleteRenderbuffer(_depthRbo); _depthRbo = 0; }
        if (_fbo != 0) { _gl.DeleteFramebuffer(_fbo); _fbo = 0; }
        if (_quadVao != 0) { _gl.DeleteVertexArray(_quadVao); _quadVao = 0; }
    }

    public void Dispose()
    {
        CleanupFBO();
        _lensingShader?.Dispose();
    }
}

/// <summary>
/// Screen-space data for a single black hole, passed to the lensing shader.
/// </summary>
public struct BlackHoleRenderData
{
    /// <summary>X position in screen pixels.</summary>
    public float ScreenX;
    /// <summary>Y position in screen pixels.</summary>
    public float ScreenY;
    /// <summary>Schwarzschild radius projected to screen pixels.</summary>
    public float RsScreen;
    /// <summary>Lensing strength (4GM/c² in screen units).</summary>
    public float LensStrength;
}
