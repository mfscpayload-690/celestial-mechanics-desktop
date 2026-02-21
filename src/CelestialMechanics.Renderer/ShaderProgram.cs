using System.Numerics;
using Silk.NET.OpenGL;

namespace CelestialMechanics.Renderer;

public class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocationCache = new();

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetProgramInfoLog(_handle);
            throw new Exception($"Shader program link failed: {info}");
        }

        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Shader compilation failed ({type}): {info}");
        }

        return shader;
    }

    public void Use() => _gl.UseProgram(_handle);

    public int GetUniformLocation(string name)
    {
        if (_uniformLocationCache.TryGetValue(name, out int location))
            return location;

        location = _gl.GetUniformLocation(_handle, name);
        _uniformLocationCache[name] = location;
        return location;
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
            _gl.UniformMatrix4(location, 1, false, (float*)&value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
            _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, float value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, int value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector2 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
            _gl.Uniform2(location, value.X, value.Y);
    }

    public uint GetAttribLocation(string name) => (uint)_gl.GetAttribLocation(_handle, name);

    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
    }
}
