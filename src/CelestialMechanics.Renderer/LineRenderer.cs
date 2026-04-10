using System.Numerics;
using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public class LineRenderer : IDisposable
{
    private uint _vao;
    private uint _vbo;
    private int _vertexCount;
    private GL? _gl;
    private readonly List<float> _vertexData = new();
    private bool _dirty;
    private float[] _uploadBuffer = Array.Empty<float>();
    private nuint _vboCapacityBytes;

    public bool HasLines => _vertexCount > 0;

    public void Initialize(GL gl)
    {
        _gl = gl;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _vboCapacityBytes = 1024;
        unsafe
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, _vboCapacityBytes, null, BufferUsageARB.DynamicDraw);
        }

        // Position (location 0)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);

        // Color (location 1)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));

        gl.BindVertexArray(0);
    }

    public void Clear()
    {
        _vertexData.Clear();
        _vertexCount = 0;
        _dirty = true;
    }

    public void AddLine(Vector3 start, Vector3 end, Vector4 color)
    {
        _vertexData.Add(start.X);
        _vertexData.Add(start.Y);
        _vertexData.Add(start.Z);
        _vertexData.Add(color.X);
        _vertexData.Add(color.Y);
        _vertexData.Add(color.Z);
        _vertexData.Add(color.W);

        _vertexData.Add(end.X);
        _vertexData.Add(end.Y);
        _vertexData.Add(end.Z);
        _vertexData.Add(color.X);
        _vertexData.Add(color.Y);
        _vertexData.Add(color.Z);
        _vertexData.Add(color.W);

        _vertexCount += 2;
        _dirty = true;
    }

    public void Upload()
    {
        if (!_dirty || _gl == null) return;

        int count = _vertexData.Count;
        if (count == 0)
        {
            _dirty = false;
            return;
        }

        if (_uploadBuffer.Length < count)
            _uploadBuffer = new float[NextPowerOfTwo(count)];

        _vertexData.CopyTo(_uploadBuffer);

        nuint neededBytes = (nuint)(count * sizeof(float));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        if (neededBytes > _vboCapacityBytes)
        {
            _vboCapacityBytes = NextPowerOfTwoBytes(neededBytes);
            unsafe
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, _vboCapacityBytes, null, BufferUsageARB.DynamicDraw);
            }
        }

        unsafe
        {
            fixed (float* ptr = _uploadBuffer)
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, neededBytes, ptr);
        }

        _dirty = false;
    }

    private static int NextPowerOfTwo(int value)
    {
        int p = 1;
        while (p < value)
            p <<= 1;
        return p;
    }

    private static nuint NextPowerOfTwoBytes(nuint value)
    {
        nuint p = 1;
        while (p < value)
            p <<= 1;
        return p;
    }

    public void Render(GL gl, ShaderProgram shader)
    {
        if (_vertexCount == 0) return;

        Upload();
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
