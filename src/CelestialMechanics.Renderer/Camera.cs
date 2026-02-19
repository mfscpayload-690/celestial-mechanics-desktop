using System.Numerics;

namespace CelestialMechanics.Renderer;

public class Camera
{
    public float Yaw { get; set; } = -90f;
    public float Pitch { get; set; } = 20f;
    public float Distance { get; set; } = 10f;
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float NearPlane { get; set; } = 0.01f;
    public float FarPlane { get; set; } = 10000f;
    public float Fov { get; set; } = 60f;

    private float _smoothYaw;
    private float _smoothPitch;
    private float _smoothDistance;
    private Vector3 _smoothTarget;
    private const float Damping = 8.0f;

    public Camera()
    {
        _smoothYaw = Yaw;
        _smoothPitch = Pitch;
        _smoothDistance = Distance;
        _smoothTarget = Target;
    }

    public Vector3 Position
    {
        get
        {
            float yawRad = MathF.PI / 180f * _smoothYaw;
            float pitchRad = MathF.PI / 180f * _smoothPitch;

            float x = _smoothDistance * MathF.Cos(pitchRad) * MathF.Cos(yawRad);
            float y = _smoothDistance * MathF.Sin(pitchRad);
            float z = _smoothDistance * MathF.Cos(pitchRad) * MathF.Sin(yawRad);

            return _smoothTarget + new Vector3(x, y, z);
        }
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, _smoothTarget, Vector3.UnitY);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        float fovRad = Fov * MathF.PI / 180f;
        return Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspectRatio, NearPlane, FarPlane);
    }

    public void ProcessMouseOrbit(float deltaX, float deltaY)
    {
        Yaw += deltaX * 0.3f;
        Pitch -= deltaY * 0.3f;
        Pitch = MathF.Max(-89f, MathF.Min(89f, Pitch));
    }

    public void ProcessMousePan(float deltaX, float deltaY)
    {
        float yawRad = MathF.PI / 180f * _smoothYaw;
        Vector3 right = new Vector3(-MathF.Sin(yawRad), 0, MathF.Cos(yawRad));
        Vector3 up = Vector3.UnitY;

        float panSpeed = _smoothDistance * 0.002f;
        Target -= right * deltaX * panSpeed;
        Target += up * deltaY * panSpeed;
    }

    public void ProcessMouseZoom(float scrollDelta)
    {
        Distance *= MathF.Pow(0.9f, scrollDelta);
        Distance = MathF.Max(0.1f, MathF.Min(10000f, Distance));
    }

    public void FocusOn(Vector3 position)
    {
        Target = position;
        Distance = 5f;
    }

    public void Update(float deltaTime)
    {
        float t = 1f - MathF.Exp(-Damping * deltaTime);
        _smoothYaw = Lerp(_smoothYaw, Yaw, t);
        _smoothPitch = Lerp(_smoothPitch, Pitch, t);
        _smoothDistance = Lerp(_smoothDistance, Distance, t);
        _smoothTarget = Vector3.Lerp(_smoothTarget, Target, t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
