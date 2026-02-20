using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.SoA;

/// <summary>
/// Structure-of-Arrays (SoA) body storage for high-performance N-body simulation.
///
/// WHY SoA OVER AoS?
/// -----------------
/// The traditional Array-of-Structures (AoS) layout stores each body as a single object
/// with all its fields interleaved in memory:
///
///   AoS:  [PosX₀|PosY₀|PosZ₀|VelX₀|...][PosX₁|PosY₁|PosZ₁|VelX₁|...] ...
///
/// When the force loop accesses PosX, PosY, PosZ across all bodies, it must jump
/// over unrelated fields (Vel, Acc, Mass) between each body — resulting in many
/// cache misses.
///
/// SoA groups all values of the same field into a single contiguous array:
///
///   SoA:  PosX: [x₀|x₁|x₂|...n]
///         PosY: [y₀|y₁|y₂|...n]
///         Mass: [m₀|m₁|m₂|...n]
///
/// The inner force loop reads PosX[j], PosY[j], PosZ[j], Mass[j] sequentially.
/// These are in consecutive cache lines, so hardware prefetchers load them
/// automatically. A typical L1 cache line (64 bytes) holds 8 doubles, so
/// accessing j=0..7 in order causes only 1-3 cache misses regardless of body count,
/// versus n cache misses in AoS.
///
/// For 1000 bodies and an O(n²) loop, this reduces cache-induced stalls from
/// ~500,000 to ~125,000 — a 4x reduction in memory-bound work.
///
/// ALLOCATION POLICY
/// -----------------
/// Arrays are allocated once at construction. No heap allocations occur during
/// simulation steps. Capacity is fixed; resize by creating a new BodySoA.
/// </summary>
public sealed class BodySoA
{
    // ─── Position arrays ──────────────────────────────────────────────────────
    public readonly double[] PosX;
    public readonly double[] PosY;
    public readonly double[] PosZ;

    // ─── Velocity arrays ──────────────────────────────────────────────────────
    public readonly double[] VelX;
    public readonly double[] VelY;
    public readonly double[] VelZ;

    // ─── Current acceleration arrays (filled by IPhysicsComputeBackend) ───────
    public readonly double[] AccX;
    public readonly double[] AccY;
    public readonly double[] AccZ;

    // ─── Previous-step acceleration arrays (required by Velocity Verlet) ──────
    // Verlet needs a(t) when computing v(t+dt) = v(t) + 0.5·(a(t)+a(t+dt))·dt.
    // Storing old accelerations avoids recomputing forces a second time.
    public readonly double[] OldAccX;
    public readonly double[] OldAccY;
    public readonly double[] OldAccZ;

    // ─── Scalar per-body fields ────────────────────────────────────────────────
    public readonly double[] Mass;
    public readonly double[] Radius;
    public readonly double[] Density;
    public readonly bool[] IsActive;
    public readonly bool[] IsCollidable;
    public readonly int[] BodyTypeIndex;

    /// <summary>Number of bodies in this buffer.</summary>
    public int Count { get; private set; }

    /// <summary>Maximum bodies this buffer can hold (fixed at construction).</summary>
    public int Capacity { get; }

    /// <summary>
    /// Allocate SoA arrays for up to <paramref name="capacity"/> bodies.
    /// All arrays are zero-initialized by the CLR.
    /// </summary>
    public BodySoA(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        Count    = 0;

        PosX = new double[capacity];
        PosY = new double[capacity];
        PosZ = new double[capacity];

        VelX = new double[capacity];
        VelY = new double[capacity];
        VelZ = new double[capacity];

        AccX = new double[capacity];
        AccY = new double[capacity];
        AccZ = new double[capacity];

        OldAccX = new double[capacity];
        OldAccY = new double[capacity];
        OldAccZ = new double[capacity];

        Mass          = new double[capacity];
        Radius        = new double[capacity];
        Density       = new double[capacity];
        IsActive      = new bool[capacity];
        IsCollidable  = new bool[capacity];
        BodyTypeIndex = new int[capacity];
    }

    // ─── Conversion helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Copy the AoS <see cref="PhysicsBody"/> array into this SoA buffer.
    /// Call this once before entering the simulation loop.
    /// </summary>
    public void CopyFrom(PhysicsBody[] bodies)
    {
        int n = bodies.Length;
        if (n > Capacity)
            throw new InvalidOperationException(
                $"Body count {n} exceeds SoA capacity {Capacity}.");

        Count = n;
        for (int i = 0; i < n; i++)
        {
            ref readonly PhysicsBody b = ref bodies[i];
            PosX[i] = b.Position.X;
            PosY[i] = b.Position.Y;
            PosZ[i] = b.Position.Z;

            VelX[i] = b.Velocity.X;
            VelY[i] = b.Velocity.Y;
            VelZ[i] = b.Velocity.Z;

            AccX[i] = b.Acceleration.X;
            AccY[i] = b.Acceleration.Y;
            AccZ[i] = b.Acceleration.Z;

            // OldAcc starts equal to current; first Verlet half-kick is correct.
            OldAccX[i] = b.Acceleration.X;
            OldAccY[i] = b.Acceleration.Y;
            OldAccZ[i] = b.Acceleration.Z;

            Mass[i]          = b.Mass;
            Radius[i]        = b.Radius;
            Density[i]       = b.Density;
            IsActive[i]      = b.IsActive;
            IsCollidable[i]  = b.IsCollidable;
            BodyTypeIndex[i] = (int)b.Type;
        }
    }

    /// <summary>
    /// Write positions, velocities and accelerations back to the AoS array.
    /// The renderer and diagnostics read from <see cref="PhysicsBody"/> directly,
    /// so this must be called after every SoA step.
    /// </summary>
    public void CopyTo(PhysicsBody[] bodies)
    {
        int n = System.Math.Min(Count, bodies.Length);
        for (int i = 0; i < n; i++)
        {
            bodies[i].Position     = new CelestialMechanics.Math.Vec3d(PosX[i], PosY[i], PosZ[i]);
            bodies[i].Velocity     = new CelestialMechanics.Math.Vec3d(VelX[i], VelY[i], VelZ[i]);
            bodies[i].Acceleration = new CelestialMechanics.Math.Vec3d(AccX[i], AccY[i], AccZ[i]);
            bodies[i].Mass         = Mass[i];
            bodies[i].Radius       = Radius[i];
            bodies[i].Density      = Density[i];
            bodies[i].IsActive     = IsActive[i];
            bodies[i].IsCollidable = IsCollidable[i];
        }
    }
}
