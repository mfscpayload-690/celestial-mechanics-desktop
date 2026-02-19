using System.Numerics;
using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public class GridRenderer : IDisposable
{
    private uint _vao;
    private uint _vbo;
    private int _vertexCount;
    private GL? _gl;

    public void Initialize(GL gl)
    {
        _gl = gl;

        var vertices = new List<float>();
        int halfSize = 50;
        float y = 0f;

        for (int i = -halfSize; i <= halfSize; i++)
        {
            bool major = (i % 10) == 0;
            float alpha = major ? 0.4f : 0.15f;
            float gray = major ? 0.5f : 0.3f;

            // Line along X (Z-constant)
            AddVertex(vertices, -halfSize, y, i, gray, gray, gray, alpha);
            AddVertex(vertices, halfSize, y, i, gray, gray, gray, alpha);

            // Line along Z (X-constant)
            AddVertex(vertices, i, y, -halfSize, gray, gray, gray, alpha);
            AddVertex(vertices, i, y, halfSize, gray, gray, gray, alpha);
        }

        // X-axis (red)
        AddVertex(vertices, -halfSize, y, 0, 0.8f, 0.2f, 0.2f, 0.6f);
        AddVertex(vertices, halfSize, y, 0, 0.8f, 0.2f, 0.2f, 0.6f);

        // Z-axis (blue)
        AddVertex(vertices, 0, y, -halfSize, 0.2f, 0.2f, 0.8f, 0.6f);
        AddVertex(vertices, 0, y, halfSize, 0.2f, 0.2f, 0.8f, 0.6f);

        _vertexCount = vertices.Count / 7;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var data = vertices.ToArray();
        unsafe
        {
            fixed (float* ptr = data)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }

        // Position (location 0)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);

        // Color (location 1)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));

        gl.BindVertexArray(0);
    }

    private static void AddVertex(List<float> vertices, float x, float y, float z, float r, float g, float b, float a)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(z);
        vertices.Add(r);
        vertices.Add(g);
        vertices.Add(b);
        vertices.Add(a);
    }

    public void Render(GL gl, ShaderProgram shader)
    {
        shader.Use();
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_vertexCount);
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_gl == null) return;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
