using CelestialMechanics.AppCore.Snapshot;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Tests;

public sealed class SnapshotTests
{
    // ── CaptureSnapshot ───────────────────────────────────────────────────────

    [Fact]
    public void CaptureSnapshot_ContainsAllActiveEntities()
    {
        var manager = BuildManager(entityCount: 3);
        var sm      = new SnapshotManager();

        var snap = sm.CaptureSnapshot(manager);

        Assert.NotNull(snap);
        Assert.Equal(3, snap!.Entities.Length);
    }

    [Fact]
    public void CaptureSnapshot_RecordsSimulationTime()
    {
        var manager = BuildManager(1);
        // Advance time before capture
        for (int i = 0; i < 10; i++) manager.Step(0.01);

        var sm   = new SnapshotManager();
        var snap = sm.CaptureSnapshot(manager)!;

        Assert.True(snap.SimulationTime > 0.0);
    }

    // ── RestoreSnapshot ───────────────────────────────────────────────────────

    [Fact]
    public void RestoreSnapshot_ResetsEntityPositions()
    {
        var manager = BuildManager(1);
        var sm      = new SnapshotManager();

        // Capture at t=0
        var initial = sm.CaptureSnapshot(manager)!;
        Vec3d savedPos = initial.Entities[0].Position;

        // Advance and move the body
        for (int i = 0; i < 50; i++) manager.Step(0.001);

        var pc = manager.Entities[0].GetComponent<PhysicsComponent>()!;
        Assert.NotEqual(savedPos, pc.Position); // must have moved

        // Restore
        sm.RestoreSnapshot(initial, manager);

        var restored = manager.Entities[0].GetComponent<PhysicsComponent>()!;
        Assert.Equal(savedPos.X, restored.Position.X, precision: 10);
        Assert.Equal(savedPos.Y, restored.Position.Y, precision: 10);
        Assert.Equal(savedPos.Z, restored.Position.Z, precision: 10);
    }

    // ── Circular buffer ───────────────────────────────────────────────────────

    [Fact]
    public void CircularBuffer_MaxSnapshots_OldestEvicted()
    {
        const int Max = 5;
        var manager = BuildManager(1);
        var sm      = new SnapshotManager { MaxSnapshots = Max };

        for (int i = 0; i < Max + 3; i++)
        {
            manager.Step(0.001);
            sm.CaptureSnapshot(manager);
        }

        Assert.Equal(Max, sm.SnapshotCount);
    }

    [Fact]
    public void GetOldest_And_GetLatest_ReturnCorrectSnapshots()
    {
        var manager = BuildManager(1);
        var sm      = new SnapshotManager();

        sm.CaptureSnapshot(manager);              // oldest
        for (int i = 0; i < 5; i++) manager.Step(0.01);
        sm.CaptureSnapshot(manager);              // latest

        var oldest = sm.GetOldest()!;
        var latest = sm.GetLatest()!;

        Assert.True(oldest.SimulationTime < latest.SimulationTime);
    }

    // ── Memory limit ──────────────────────────────────────────────────────────

    [Fact]
    public void MemoryLimit_SkipsCapture_WhenExceeded()
    {
        var manager = BuildManager(100);   // 100 entities → large snapshot
        var sm      = new SnapshotManager
        {
            MaxMemoryBytes = 1,            // ridiculously small
            MaxSnapshots   = 512,
        };

        var result = sm.CaptureSnapshot(manager);

        Assert.Null(result);               // should be skipped
        Assert.Equal(0, sm.SnapshotCount);
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllSnapshots()
    {
        var manager = BuildManager(1);
        var sm      = new SnapshotManager();

        for (int i = 0; i < 10; i++) sm.CaptureSnapshot(manager);
        sm.Clear();

        Assert.Equal(0, sm.SnapshotCount);
        Assert.Equal(0L, sm.EstimatedMemoryBytes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimulationManager BuildManager(int entityCount)
    {
        var config  = new PhysicsConfig { DeterministicMode = true, UseSoAPath = false };
        var manager = new SimulationManager(config);
        var rng     = new Random(42);

        for (int i = 0; i < entityCount; i++)
        {
            var e = new Entity();
            e.AddComponent(new PhysicsComponent(
                mass:     1.0 + rng.NextDouble(),
                position: new Vec3d(rng.NextDouble(), rng.NextDouble(), rng.NextDouble()),
                velocity: new Vec3d(rng.NextDouble() * 0.1, 0, 0),
                radius:   0.05));
            manager.AddEntity(e);
        }
        manager.FlushPendingChanges();
        return manager;
    }
}
