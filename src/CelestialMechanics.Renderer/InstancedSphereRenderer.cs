using System.Numerics;
using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public class InstancedSphereRenderer : IDisposable
{
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private uint _instanceVbo;
    private int _indexCount;
    private int _instanceCount;
    private GL? _gl;

    // Per-instance data: 4x4 matrix (16 floats) + color (4 floats) = 20 floats = 80 bytes
    private const int InstanceStride = 20 * sizeof(float);
    private float[] _instanceData = Array.Empty<float>();

    public void Initialize(GL gl)
    {
        _gl = gl;
        GenerateIcosphere(2, out float[] vertices, out uint[] indices);
        _indexCount = indices.Length;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Vertex buffer (position + normal)
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = vertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }

        // Position (location 0)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        // Normal (location 1)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

        // Element buffer
        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* ptr = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }

        // Instance buffer
        _instanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, 0, ReadOnlySpan<byte>.Empty, BufferUsageARB.DynamicDraw);

        // Instance model matrix (locations 2-5, 4 vec4s)
        for (uint i = 0; i < 4; i++)
        {
            gl.EnableVertexAttribArray(2 + i);
            gl.VertexAttribPointer(2 + i, 4, VertexAttribPointerType.Float, false, (uint)InstanceStride, (nint)(i * 4 * sizeof(float)));
            gl.VertexAttribDivisor(2 + i, 1);
        }

        // Instance color (location 6)
        gl.EnableVertexAttribArray(6);
        gl.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, (uint)InstanceStride, 16 * sizeof(float));
        gl.VertexAttribDivisor(6, 1);

        gl.BindVertexArray(0);
    }

    public void UpdateInstances(RenderBody[] bodies, int count)
    {
        _instanceCount = count;
        if (count == 0) return;

        int dataSize = count * 20;
        if (_instanceData.Length < dataSize)
            _instanceData = new float[dataSize];

        for (int i = 0; i < count; i++)
        {
            ref var body = ref bodies[i];
            float scale = body.Radius;
            var model = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(body.Position);

            int offset = i * 20;
            // Column-major for OpenGL
            _instanceData[offset + 0] = model.M11;
            _instanceData[offset + 1] = model.M21;
            _instanceData[offset + 2] = model.M31;
            _instanceData[offset + 3] = model.M41;
            _instanceData[offset + 4] = model.M12;
            _instanceData[offset + 5] = model.M22;
            _instanceData[offset + 6] = model.M32;
            _instanceData[offset + 7] = model.M42;
            _instanceData[offset + 8] = model.M13;
            _instanceData[offset + 9] = model.M23;
            _instanceData[offset + 10] = model.M33;
            _instanceData[offset + 11] = model.M43;
            _instanceData[offset + 12] = model.M14;
            _instanceData[offset + 13] = model.M24;
            _instanceData[offset + 14] = model.M34;
            _instanceData[offset + 15] = model.M44;
            _instanceData[offset + 16] = body.Color.X;
            _instanceData[offset + 17] = body.Color.Y;
            _instanceData[offset + 18] = body.Color.Z;
            _instanceData[offset + 19] = body.Color.W;
        }

        _gl!.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        unsafe
        {
            fixed (float* ptr = _instanceData)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(count * InstanceStride), ptr, BufferUsageARB.DynamicDraw);
        }
    }

    public unsafe void Render(GL gl, ShaderProgram shader)
    {
        if (_instanceCount == 0) return;

        shader.Use();
        gl.BindVertexArray(_vao);
        gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null, (uint)_instanceCount);
        gl.BindVertexArray(0);
    }

    private static void GenerateIcosphere(int subdivisions, out float[] vertices, out uint[] indices)
    {
        // Start with icosahedron
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            Vector3.Normalize(new(-1, t, 0)),
            Vector3.Normalize(new(1, t, 0)),
            Vector3.Normalize(new(-1, -t, 0)),
            Vector3.Normalize(new(1, -t, 0)),
            Vector3.Normalize(new(0, -1, t)),
            Vector3.Normalize(new(0, 1, t)),
            Vector3.Normalize(new(0, -1, -t)),
            Vector3.Normalize(new(0, 1, -t)),
            Vector3.Normalize(new(t, 0, -1)),
            Vector3.Normalize(new(t, 0, 1)),
            Vector3.Normalize(new(-t, 0, -1)),
            Vector3.Normalize(new(-t, 0, 1)),
        };

        var tris = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };

        var midpointCache = new Dictionary<long, int>();

        for (int s = 0; s < subdivisions; s++)
        {
            var newTris = new List<(int, int, int)>();
            midpointCache.Clear();

            foreach (var (i0, i1, i2) in tris)
            {
                int a = GetMidpoint(verts, midpointCache, i0, i1);
                int b = GetMidpoint(verts, midpointCache, i1, i2);
                int c = GetMidpoint(verts, midpointCache, i2, i0);

                newTris.Add((i0, a, c));
                newTris.Add((i1, b, a));
                newTris.Add((i2, c, b));
                newTris.Add((a, b, c));
            }

            tris = newTris;
        }

        // Output: interleaved position + normal (same for unit sphere)
        vertices = new float[verts.Count * 6];
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            vertices[i * 6 + 0] = v.X;
            vertices[i * 6 + 1] = v.Y;
            vertices[i * 6 + 2] = v.Z;
            vertices[i * 6 + 3] = v.X; // normal = position for unit sphere
            vertices[i * 6 + 4] = v.Y;
            vertices[i * 6 + 5] = v.Z;
        }

        indices = new uint[tris.Count * 3];
        for (int i = 0; i < tris.Count; i++)
        {
            indices[i * 3 + 0] = (uint)tris[i].Item1;
            indices[i * 3 + 1] = (uint)tris[i].Item2;
            indices[i * 3 + 2] = (uint)tris[i].Item3;
        }
    }

    private static int GetMidpoint(List<Vector3> verts, Dictionary<long, int> cache, int i0, int i1)
    {
        long key = ((long)System.Math.Min(i0, i1) << 32) + System.Math.Max(i0, i1);
        if (cache.TryGetValue(key, out int idx))
            return idx;

        var mid = Vector3.Normalize((verts[i0] + verts[i1]) * 0.5f);
        idx = verts.Count;
        verts.Add(mid);
        cache[key] = idx;
        return idx;
    }

    public void Dispose()
    {
        if (_gl == null) return;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_instanceVbo);
    }
}
