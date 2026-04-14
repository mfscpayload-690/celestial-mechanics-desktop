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
    public double Temperature;
    public double HeatCapacity;
    public double Luminosity;
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
        Temperature = GetDefaultTemperature(type);
        HeatCapacity = GetDefaultHeatCapacity(type);
        Luminosity = GetDefaultLuminosity(type, mass, Temperature);
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

    private static double GetDefaultTemperature(BodyType type)
    {
        return type switch
        {
            BodyType.Star => 5772.0,
            BodyType.NeutronStar => 1.0e6,
            BodyType.BlackHole => 3.0,
            BodyType.Comet => 180.0,
            BodyType.GasGiant => 130.0,
            BodyType.Planet or BodyType.RockyPlanet => 288.0,
            _ => 250.0,
        };
    }

    private static double GetDefaultHeatCapacity(BodyType type)
    {
        return type switch
        {
            BodyType.Star => 2.0e4,
            BodyType.NeutronStar => 5.0e3,
            BodyType.BlackHole => 1.0e8,
            BodyType.GasGiant => 1.2e4,
            BodyType.Planet or BodyType.RockyPlanet => 9.0e2,
            BodyType.Comet => 2.1e3,
            _ => 1.0e3,
        };
    }

    private static double GetDefaultLuminosity(BodyType type, double mass, double temperature)
    {
        return type switch
        {
            BodyType.Star => 3.828e26 * System.Math.Pow(System.Math.Max(mass, 0.08), 3.5),
            BodyType.NeutronStar => 1.0e25 * System.Math.Pow(System.Math.Max(temperature, 1.0) / 1.0e6, 4.0),
            _ => 0.0,
        };
    }
}
