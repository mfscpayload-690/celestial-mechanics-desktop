using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Components;

/// <summary>
/// Wraps the existing physics body properties into the ECS framework.
/// SimulationManager synchronises these fields to/from PhysicsBody[] arrays
/// for the NBodySolver backend. Does NOT duplicate Barnes–Hut or force computation.
/// </summary>
public sealed class PhysicsComponent : IComponent
{
    public double Mass { get; set; }
    public Vec3d Position { get; set; }
    public Vec3d Velocity { get; set; }
    public double Radius { get; set; }
    public Vec3d Acceleration { get; set; }
    public Vec3d ForceAccumulator { get; set; }
    public bool IsCollidable { get; set; } = true;
    public double Density { get; set; } = 1000.0;

    /// <summary>
    /// Index into the NBodySolver PhysicsBody[] array.
    /// Set by SimulationManager during synchronisation. -1 if not assigned.
    /// </summary>
    public int BodyIndex { get; set; } = -1;

    public PhysicsComponent() { }

    public PhysicsComponent(double mass, Vec3d position, Vec3d velocity, double radius)
    {
        Mass = mass;
        Position = position;
        Velocity = velocity;
        Radius = radius;
    }

    /// <summary>
    /// Reset the force accumulator. Called by SimulationManager before each force computation.
    /// Physics integration is handled by the NBodySolver, not here.
    /// </summary>
    public void Update(double dt)
    {
        ForceAccumulator = Vec3d.Zero;
    }
}
