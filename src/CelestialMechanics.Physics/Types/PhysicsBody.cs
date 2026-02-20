using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Mutable struct representing one gravitational body.
/// Phase 1: Array of Structures (AoS) layout.
/// Phase 4: Extended with physical density and collision support.
/// </summary>
public struct PhysicsBody
{
    public int Id;
    public double Mass;
    public double Density;
    public double Radius;
    public Vec3d Position;
    public Vec3d Velocity;
    public Vec3d Acceleration;
    public double GravityStrength;
    public double GravityRange;
    public BodyType Type;
    public bool IsActive;
    public bool IsCollidable;

    public PhysicsBody(int id, double mass, Vec3d position, Vec3d velocity, BodyType type)
    {
        Id = id;
        Mass = mass;
        Type = type;
        Density = DensityModel.GetDefaultDensity(type);
        Radius = DensityModel.ComputeBodyRadius(mass, Density, type);
        Position = position;
        Velocity = velocity;
        Acceleration = Vec3d.Zero;
        GravityStrength = 0.0;
        GravityRange = 0.0;
        IsActive = true;
        IsCollidable = true;
    }

    /// <summary>
    /// Recompute radius from current mass and density.
    /// Call after mass changes (e.g. merges).
    /// </summary>
    public void RecalculateRadius()
    {
        Radius = DensityModel.ComputeBodyRadius(Mass, Density, Type);
    }
}
