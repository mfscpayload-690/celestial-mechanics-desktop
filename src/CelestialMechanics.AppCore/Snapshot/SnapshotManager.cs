using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Snapshot;

/// <summary>
/// Manages a fixed-size circular buffer of <see cref="SimulationSnapshot"/> objects.
///
/// Design goals:
///   • O(1) capture — single pass over entity list
///   • Configurable count limit (<see cref="MaxSnapshots"/>)
///   • Configurable memory ceiling (<see cref="MaxMemoryBytes"/>)
///   • Safe concurrent access via <see cref="ReaderWriterLockSlim"/>
///   • Index-based random access for time-bar scrubbing
/// </summary>
public sealed class SnapshotManager : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Maximum number of snapshots retained. Oldest is evicted when full. Default: 512.</summary>
    public int MaxSnapshots { get; set; } = 512;

    /// <summary>
    /// Approximate maximum memory budget in bytes.
    /// Capture is skipped (not an error) when the buffer would exceed this.
    /// Default: 256 MB.
    /// </summary>
    public long MaxMemoryBytes { get; set; } = 256L * 1024 * 1024;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly List<SimulationSnapshot> _ring = new();
    private int _stepCounter;
    private long _totalBytes;

    // ── Properties ────────────────────────────────────────────────────────────

    public int SnapshotCount
    {
        get { _lock.EnterReadLock(); try { return _ring.Count; } finally { _lock.ExitReadLock(); } }
    }

    public long EstimatedMemoryBytes
    {
        get { _lock.EnterReadLock(); try { return _totalBytes; } finally { _lock.ExitReadLock(); } }
    }

    // ── Capture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the current state of all active entities with a <see cref="PhysicsComponent"/>.
    /// Skips capture if memory limit would be exceeded (non-fatal).
    /// Thread-safe.
    /// </summary>
    /// <returns>The captured snapshot, or null if capture was skipped.</returns>
    public SimulationSnapshot? CaptureSnapshot(SimulationManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var snapshot = BuildSnapshot(manager);

        _lock.EnterWriteLock();
        try
        {
            // Memory guard — skip rather than throw
            if (_totalBytes + snapshot.EstimatedBytes > MaxMemoryBytes)
                return null;

            // Circular eviction
            if (_ring.Count >= MaxSnapshots && _ring.Count > 0)
            {
                _totalBytes -= _ring[0].EstimatedBytes;
                _ring.RemoveAt(0);
            }

            _ring.Add(snapshot);
            _totalBytes += snapshot.EstimatedBytes;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return snapshot;
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes entity state from <paramref name="snapshot"/> back into the live
    /// <see cref="SimulationManager"/>. Matched by <see cref="EntitySnapshotData.EntityId"/>.
    ///
    /// The simulation should be paused before calling this.
    /// </summary>
    public void RestoreSnapshot(SimulationSnapshot snapshot, SimulationManager manager)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manager);

        // Build quick lookup from entity ID → snapshot data
        var lookup = new Dictionary<Guid, EntitySnapshotData>(snapshot.Entities.Length);
        foreach (var data in snapshot.Entities)
            lookup[data.EntityId] = data;

        // Apply to entities
        foreach (var entity in manager.Entities)
        {
            if (!lookup.TryGetValue(entity.Id, out var data)) continue;

            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null) continue;

            pc.Position = data.Position;
            pc.Velocity = data.Velocity;
            pc.Acceleration = data.Acceleration;
            pc.Mass     = data.Mass;
            pc.Radius   = data.Radius;
            entity.IsActive = data.IsActive;
        }

        // Rewind simulation clock
        manager.Time.Reset();
        manager.Time.AdvanceTime(snapshot.SimulationTime);
    }

    // ── Access ────────────────────────────────────────────────────────────────

    /// <summary>Returns the snapshot at buffer index <paramref name="index"/> (oldest = 0).</summary>
    public SimulationSnapshot? GetSnapshot(int index)
    {
        _lock.EnterReadLock();
        try
        {
            if (index < 0 || index >= _ring.Count) return null;
            return _ring[index];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns the most recently captured snapshot, or null if empty.</summary>
    public SimulationSnapshot? GetLatest()
    {
        _lock.EnterReadLock();
        try { return _ring.Count > 0 ? _ring[^1] : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns the oldest retained snapshot, or null if empty.</summary>
    public SimulationSnapshot? GetOldest()
    {
        _lock.EnterReadLock();
        try { return _ring.Count > 0 ? _ring[0] : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Clears all snapshots and resets the memory counter.</summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try { _ring.Clear(); _totalBytes = 0; _stepCounter = 0; }
        finally { _lock.ExitWriteLock(); }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private SimulationSnapshot BuildSnapshot(SimulationManager manager)
    {
        var entities = manager.Entities;
        var dataList = new List<EntitySnapshotData>(entities.Count);

        foreach (var entity in entities)
        {
            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null) continue;

            dataList.Add(new EntitySnapshotData
            {
                EntityId     = entity.Id,
                Position     = pc.Position,
                Velocity     = pc.Velocity,
                Acceleration = pc.Acceleration,
                Mass         = pc.Mass,
                Radius       = pc.Radius,
                IsActive     = entity.IsActive,
            });
        }

        return new SimulationSnapshot
        {
            SimulationTime = manager.Time.SimulationTime,
            StepIndex      = _stepCounter++,
            Entities       = dataList.ToArray(),
        };
    }

    public void Dispose() => _lock.Dispose();
}
