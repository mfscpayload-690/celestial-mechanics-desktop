using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public sealed class BackgroundRenderer : IDisposable
{
    private uint _vao;
    private GL? _gl;

    public void Initialize(GL gl)
    {
        _gl = gl;
        _vao = gl.GenVertexArray();
    }

    public void Render(GL gl)
    {
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_gl == null || _vao == 0)
            return;

        _gl.DeleteVertexArray(_vao);
        _vao = 0;
    }
}
