using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Mutable struct representing one gravitational body.
/// Phase 1: Array of Structures (AoS) layout.
/// Phase 3+: Migrate to Structure of Arrays (SoA) for cache/SIMD.
/// </summary>
public struct PhysicsBody
{
    public int Id;
    public double Mass;
    public double Radius;
    public Vec3d Position;
    public Vec3d Velocity;
    public Vec3d Acceleration;
    public double GravityStrength;
    public double GravityRange;
    public BodyType Type;
    public bool IsActive;

    public PhysicsBody(int id, double mass, Vec3d position, Vec3d velocity, BodyType type)
    {
        Id = id;
        Mass = mass;
        Radius = 0.0;
        Position = position;
        Velocity = velocity;
        Acceleration = Vec3d.Zero;
        GravityStrength = 0.0;
        GravityRange = 0.0;
        Type = type;
        IsActive = true;
    }
}
