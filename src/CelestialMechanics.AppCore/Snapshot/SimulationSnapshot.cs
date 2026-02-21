using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Snapshot;

/// <summary>
/// Lightweight snapshot of entity physics state at a single point in simulation time.
/// Only captures data required for time-bar replay and determinism validation.
/// Full component trees are NOT copied — snapshots are cheap to allocate.
/// </summary>
public sealed record SimulationSnapshot
{
    /// <summary>Simulation clock value when the snapshot was taken.</summary>
    public double SimulationTime { get; init; }

    /// <summary>Step counter at capture time (monotonically increasing).</summary>
    public int StepIndex { get; init; }

    /// <summary>Captured entity states. One entry per active entity with a PhysicsComponent.</summary>
    public EntitySnapshotData[] Entities { get; init; } = Array.Empty<EntitySnapshotData>();

    /// <summary>Approximate memory cost of this snapshot in bytes.</summary>
    internal long EstimatedBytes => sizeof(double) * 2          // time + step
                                  + Entities.Length * EntitySnapshotData.ByteSize;
}

/// <summary>
/// Physics state of a single entity at snapshot time.
/// Deliberately a lightweight value type: 8 doubles + 2 bools + 2 Guid ≈ 112 bytes.
/// </summary>
public sealed record EntitySnapshotData
{
    public Guid   EntityId   { get; init; }
    public Vec3d  Position   { get; init; }
    public Vec3d  Velocity   { get; init; }
    public Vec3d  Acceleration { get; init; }
    public double Mass       { get; init; }
    public double Radius     { get; init; }
    public bool   IsActive   { get; init; }

    /// <summary>Approximate byte cost used for memory-limit estimation.</summary>
    internal const int ByteSize = 16 + (8 * 3 * 3) + 8 + 8 + 1; // Guid + 3×Vec3d + mass + radius + active
}
