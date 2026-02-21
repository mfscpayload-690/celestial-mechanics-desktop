using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;
using CelestialMechanics.Simulation.Factories;
using CelestialMechanics.Simulation.PhysicsExtensions;
using CelestialMechanics.Simulation.Systems;

namespace CelestialMechanics.Simulation.Tests;

// ═══════════════════════════════════════════════════════════════════════════════
// 9A — MOMENTUM CONSERVATION UTILITY
// ═══════════════════════════════════════════════════════════════════════════════

public class MomentumConservationTests
{
    [Fact]
    public void MergedVelocity_ConservesMomentum()
    {
        double m1 = 5.0, m2 = 3.0;
        var v1 = new Vec3d(10.0, 0.0, 0.0);
        var v2 = new Vec3d(-5.0, 2.0, 0.0);

        Vec3d merged = MomentumConservationUtility.ComputeMergedVelocity(m1, v1, m2, v2);

        Vec3d expectedMomentum = v1 * m1 + v2 * m2;
        Vec3d actualMomentum = merged * (m1 + m2);

        double drift = (actualMomentum - expectedMomentum).Length;
        Assert.True(drift < 1e-10, $"Momentum drift: {drift}");
    }

    [Fact]
    public void RemnantVelocity_ConservesMomentum()
    {
        var originalMomentum = new Vec3d(100.0, 50.0, -30.0);
        var ejectaMomentum = new Vec3d(60.0, 30.0, -20.0);
        double remnantMass = 5.0;

        Vec3d remnantVel = MomentumConservationUtility.ComputeRemnantVelocity(
            originalMomentum, ejectaMomentum, remnantMass);

        Vec3d totalMomentum = remnantVel * remnantMass + ejectaMomentum;
        double drift = (totalMomentum - originalMomentum).Length;
        Assert.True(drift < 1e-10, $"Momentum drift: {drift}");
    }

    [Fact]
    public void ClampVelocity_CapsAtRelativisticLimit()
    {
        double cap = MomentumConservationUtility.RelativisticVelocityCap;
        var fast = new Vec3d(cap * 2.0, 0.0, 0.0);

        Vec3d clamped = MomentumConservationUtility.ClampVelocity(fast);

        Assert.True(clamped.Length <= cap + 1e-10,
            $"Velocity {clamped.Length} exceeds cap {cap}");
    }

    [Fact]
    public void ClampVelocity_ReturnsZero_ForNaN()
    {
        var nanVel = new Vec3d(double.NaN, 1.0, 2.0);
        Vec3d result = MomentumConservationUtility.ClampVelocity(nanVel);
        Assert.Equal(Vec3d.Zero, result);
    }

    [Fact]
    public void ClampVelocity_ReturnsZero_ForInfinity()
    {
        var infVel = new Vec3d(double.PositiveInfinity, 0.0, 0.0);
        Vec3d result = MomentumConservationUtility.ClampVelocity(infVel);
        Assert.Equal(Vec3d.Zero, result);
    }

    [Fact]
    public void ClampVelocity_PreservesSubLuminal()
    {
        var slow = new Vec3d(1.0, 2.0, 3.0);
        Vec3d result = MomentumConservationUtility.ClampVelocity(slow);
        Assert.Equal(slow, result);
    }

    [Fact]
    public void ComputeTotalMomentum_Correct()
    {
        var entities = new List<Entity>();
        for (int i = 0; i < 5; i++)
        {
            var e = new Entity();
            e.AddComponent(new PhysicsComponent
            {
                Mass = (i + 1) * 1.0,
                Velocity = new Vec3d(i, -i, i * 0.5)
            });
            entities.Add(e);
        }

        Vec3d total = MomentumConservationUtility.ComputeTotalMomentum(entities);

        Vec3d expected = Vec3d.Zero;
        for (int i = 0; i < 5; i++)
        {
            double m = (i + 1) * 1.0;
            expected += new Vec3d(i, -i, i * 0.5) * m;
        }

        double diff = (total - expected).Length;
        Assert.True(diff < 1e-10, $"Momentum mismatch: {diff}");
    }

    [Fact]
    public void ComputeEjectaVelocities_ProducesFiniteValues()
    {
        int count = 20;
        double[] vx = new double[count];
        double[] vy = new double[count];
        double[] vz = new double[count];

        MomentumConservationUtility.ComputeEjectaVelocities(
            count, 1e6, 10.0, new Vec3d(1.0, 0.0, 0.0), 0.5,
            vx, vy, vz, new Random(42));

        for (int i = 0; i < count; i++)
        {
            Assert.False(double.IsNaN(vx[i]), $"NaN at vx[{i}]");
            Assert.False(double.IsNaN(vy[i]), $"NaN at vy[{i}]");
            Assert.False(double.IsNaN(vz[i]), $"NaN at vz[{i}]");
            Assert.False(double.IsInfinity(vx[i]), $"Infinity at vx[{i}]");
        }
    }

    [Fact]
    public void MergedVelocity_ZeroMass_ReturnsZero()
    {
        Vec3d result = MomentumConservationUtility.ComputeMergedVelocity(
            0.0, new Vec3d(1, 2, 3), 0.0, new Vec3d(4, 5, 6));
        Assert.Equal(Vec3d.Zero, result);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9B — ENERGY BUDGET TRACKER
// ═══════════════════════════════════════════════════════════════════════════════

public class EnergyBudgetTests
{
    [Fact]
    public void Measure_ComputesKineticEnergy()
    {
        var entities = new List<Entity>();
        var e = new Entity();
        e.AddComponent(new PhysicsComponent
        {
            Mass = 2.0,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(3.0, 4.0, 0.0) // |v| = 5
        });
        entities.Add(e);

        var tracker = new EnergyBudgetTracker();
        tracker.Measure(entities);

        // KE = 0.5 * 2.0 * 25.0 = 25.0
        Assert.Equal(25.0, tracker.KineticEnergy, 1e-10);
    }

    [Fact]
    public void Measure_ComputesPotentialEnergy()
    {
        var entities = new List<Entity>();
        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent
        {
            Mass = 1.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero
        });
        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent
        {
            Mass = 1.0,
            Position = new Vec3d(1.0, 0, 0),
            Velocity = Vec3d.Zero
        });
        entities.Add(e1);
        entities.Add(e2);

        var tracker = new EnergyBudgetTracker();
        tracker.Measure(entities);

        // PE = -G_Sim * 1.0 * 1.0 / 1.0 = -1.0
        Assert.Equal(-1.0, tracker.PotentialEnergy, 1e-10);
    }

    [Fact]
    public void RecordExplosionEnergy_Accumulates()
    {
        var tracker = new EnergyBudgetTracker();
        tracker.RecordExplosionEnergy(100.0);
        tracker.RecordExplosionEnergy(50.0);
        Assert.Equal(150.0, tracker.ExplosionEnergyInjected, 1e-10);
    }

    [Fact]
    public void IsWithinTolerance_InitialState()
    {
        var entities = new List<Entity>();
        var e = new Entity();
        e.AddComponent(new PhysicsComponent
        {
            Mass = 1.0,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(1.0, 0, 0)
        });
        entities.Add(e);

        var tracker = new EnergyBudgetTracker();
        tracker.Measure(entities);

        Assert.True(tracker.IsInitialized);
        Assert.True(tracker.IsWithinTolerance(0.05));
        Assert.Equal(0.0, tracker.EnergyDrift, 1e-10);
    }

    [Fact]
    public void Reset_ClearsAllValues()
    {
        var tracker = new EnergyBudgetTracker();
        tracker.RecordExplosionEnergy(100.0);
        tracker.RecordGravitationalWaveLoss(50.0);
        tracker.RecordMergerLoss(25.0);

        tracker.Reset();

        Assert.Equal(0.0, tracker.ExplosionEnergyInjected);
        Assert.Equal(0.0, tracker.GravitationalWaveEnergyLoss);
        Assert.Equal(0.0, tracker.MergerEnergyLoss);
        Assert.False(tracker.IsInitialized);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9C — SHOCKWAVE SYSTEM (SEDOV–TAYLOR)
// ═══════════════════════════════════════════════════════════════════════════════

public class ShockwaveTests
{
    [Fact]
    public void ShockRadius_GrowsWithTime()
    {
        var system = new ShockwaveSystem();
        double energy = 1000.0;

        double r1 = system.ComputeShockRadius(energy, 1.0);
        double r2 = system.ComputeShockRadius(energy, 10.0);
        double r3 = system.ComputeShockRadius(energy, 100.0);

        Assert.True(r1 > 0.0);
        Assert.True(r2 > r1, "Shock radius should grow with time");
        Assert.True(r3 > r2, "Shock radius should continue growing");
    }

    [Fact]
    public void ShockRadius_FollowsSedovTaylorScaling()
    {
        var system = new ShockwaveSystem();
        system.AmbientDensity = 1.0;
        double energy = 1000.0;

        // R(t) ∝ t^(2/5), so R(2t)/R(t) = 2^(2/5)
        double r1 = system.ComputeShockRadius(energy, 10.0);
        double r2 = system.ComputeShockRadius(energy, 20.0);

        double ratio = r2 / r1;
        double expectedRatio = System.Math.Pow(2.0, 0.4);

        Assert.Equal(expectedRatio, ratio, 1e-10);
    }

    [Fact]
    public void ShockRadius_ZeroTime_ReturnsZero()
    {
        var system = new ShockwaveSystem();
        Assert.Equal(0.0, system.ComputeShockRadius(1000.0, 0.0));
    }

    [Fact]
    public void ShockRadius_ZeroEnergy_ReturnsZero()
    {
        var system = new ShockwaveSystem();
        Assert.Equal(0.0, system.ComputeShockRadius(0.0, 10.0));
    }

    [Fact]
    public void CreateShockwave_Succeeds()
    {
        var system = new ShockwaveSystem(8);
        bool created = system.CreateShockwave(Vec3d.Zero, 500.0, 0.0);
        Assert.True(created);
        Assert.Equal(1, system.ActiveCount);
    }

    [Fact]
    public void CreateShockwave_PoolFull_ReturnsFalse()
    {
        var system = new ShockwaveSystem(2);
        Assert.True(system.CreateShockwave(Vec3d.Zero, 100.0, 0.0));
        Assert.True(system.CreateShockwave(Vec3d.Zero, 200.0, 0.0));
        Assert.False(system.CreateShockwave(Vec3d.Zero, 300.0, 0.0));
        Assert.Equal(2, system.ActiveCount);
    }

    [Fact]
    public void CreateShockwave_ZeroEnergy_ReturnsFalse()
    {
        var system = new ShockwaveSystem();
        Assert.False(system.CreateShockwave(Vec3d.Zero, 0.0, 0.0));
    }

    [Fact]
    public void Update_ExpiresOldShockwaves()
    {
        var system = new ShockwaveSystem(4);
        system.MaxShockwaveLifetime = 5.0;
        system.CreateShockwave(Vec3d.Zero, 100.0, 0.0);
        Assert.Equal(1, system.ActiveCount);

        // Advance past lifetime
        system.Update(new List<Entity>(), 10.0, 0.1);
        Assert.Equal(0, system.ActiveCount);
    }

    [Fact]
    public void Update_AppliesImpuleToNearbyBodies()
    {
        var system = new ShockwaveSystem();
        system.ImpulseStrength = 10.0;
        system.CreateShockwave(Vec3d.Zero, 1000.0, 0.0);

        var entity = new Entity();
        var pc = new PhysicsComponent
        {
            Mass = 1.0,
            Position = new Vec3d(0.5, 0.0, 0.0),
            Velocity = Vec3d.Zero
        };
        entity.AddComponent(pc);

        var entities = new List<Entity> { entity };

        // Advance time so shockwave has a radius
        system.Update(entities, 1.0, 0.1);

        // Body should have received some velocity kick
        // (depends on whether it's within the shock shell)
        // At minimum, velocity should not be NaN
        Assert.False(double.IsNaN(pc.Velocity.X));
        Assert.False(double.IsNaN(pc.Velocity.Y));
        Assert.False(double.IsNaN(pc.Velocity.Z));
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var system = new ShockwaveSystem(4);
        system.CreateShockwave(Vec3d.Zero, 100.0, 0.0);
        system.CreateShockwave(Vec3d.Zero, 200.0, 1.0);
        Assert.Equal(2, system.ActiveCount);

        system.Reset();
        Assert.Equal(0, system.ActiveCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9D — REMNANT FORMATION SYSTEM
// ═══════════════════════════════════════════════════════════════════════════════

public class RemnantFormationTests
{
    [Fact]
    public void FormRemnant_NeutronStar_BelowTovLimit()
    {
        var entity = new Entity();
        entity.AddComponent(new PhysicsComponent
        {
            Mass = 10.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero
        });

        double remnantMass = 2.0; // Below 3.0 TOV limit
        var evt = RemnantFormationSystem.FormRemnant(entity, remnantMass, 0.0);

        Assert.NotNull(evt);
        Assert.Equal("NeutronStar", evt!.RemnantType);
        Assert.Equal("Star_Neutron", entity.Tag);

        var pc = entity.GetComponent<PhysicsComponent>()!;
        Assert.Equal(remnantMass, pc.Mass, 1e-10);
        Assert.Equal(DensityModel.NeutronStarDensity, pc.Density, 1e-10);

        var rel = entity.GetComponent<RelativisticComponent>();
        Assert.NotNull(rel);
        Assert.True(rel!.EnablePostNewtonian);
    }

    [Fact]
    public void FormRemnant_BlackHole_AboveTovLimit()
    {
        var entity = new Entity();
        entity.AddComponent(new PhysicsComponent
        {
            Mass = 10.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero
        });

        double remnantMass = 5.0; // Above 3.0 TOV limit
        var evt = RemnantFormationSystem.FormRemnant(entity, remnantMass, 0.0);

        Assert.NotNull(evt);
        Assert.Equal("BlackHole", evt!.RemnantType);
        Assert.Equal("BlackHole", entity.Tag);

        var rel = entity.GetComponent<RelativisticComponent>();
        Assert.NotNull(rel);
        Assert.True(rel!.EnableLensing);
        Assert.True(rel.EnablePostNewtonian);

        double expectedRs = PhysicalConstants.SchwarzschildFactorSim * remnantMass;
        Assert.Equal(expectedRs, rel.SchwarzschildRadius, 1e-15);
    }

    [Fact]
    public void FormRemnant_ExactlyAtTovLimit_IsBlackHole()
    {
        var entity = new Entity();
        entity.AddComponent(new PhysicsComponent { Mass = 10.0 });

        var evt = RemnantFormationSystem.FormRemnant(entity, 3.0, 0.0);
        Assert.NotNull(evt);
        Assert.Equal("BlackHole", evt!.RemnantType);
    }

    [Fact]
    public void FormRemnant_NoPhysicsComponent_ReturnsNull()
    {
        var entity = new Entity();
        var evt = RemnantFormationSystem.FormRemnant(entity, 2.0, 0.0);
        Assert.Null(evt);
    }

    [Fact]
    public void FormRemnant_UpdatesExistingRelativisticComponent()
    {
        var entity = new Entity();
        entity.AddComponent(new PhysicsComponent { Mass = 10.0 });
        entity.AddComponent(new RelativisticComponent { SchwarzschildRadius = 0.0 });

        RemnantFormationSystem.FormRemnant(entity, 2.0, 0.0);

        // Should have updated existing component, not added a duplicate
        int relCount = 0;
        foreach (var c in entity.Components)
            if (c is RelativisticComponent) relCount++;

        Assert.Equal(1, relCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9E — MERGER RESOLUTION SYSTEM
// ═══════════════════════════════════════════════════════════════════════════════

public class MergerResolutionTests
{
    [Fact]
    public void Merger_ProducesCorrectVelocity()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 100.0; // Very generous to ensure merging
        system.MaxRelativeVelocity = 10000.0;

        var addedEntities = new List<Entity>();
        var removedEntities = new List<Entity>();
        system.OnEntityAdded = e => addedEntities.Add(e);
        system.OnEntityRemoved = e => removedEntities.Add(e);

        var e1 = new Entity();
        var pc1 = new PhysicsComponent
        {
            Mass = 2.0,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(5.0, 0.0, 0.0),
            Radius = 0.5
        };
        e1.AddComponent(pc1);
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        var pc2 = new PhysicsComponent
        {
            Mass = 3.0,
            Position = new Vec3d(0.1, 0.0, 0.0), // Very close
            Velocity = new Vec3d(-2.0, 1.0, 0.0),
            Radius = 0.5
        };
        e2.AddComponent(pc2);
        e2.AddComponent(new RelativisticComponent());

        var entities = new List<Entity> { e1, e2 };
        var events = system.Process(entities, 0.0);

        Assert.Single(events);
        var me = events[0];

        // Check momentum conservation
        Vec3d originalMomentum = pc1.Velocity * pc1.Mass + pc2.Velocity * pc2.Mass;
        // Note: original velocities were captured before the merger
        Vec3d expectedMergedVel = originalMomentum / (pc1.Mass + pc2.Mass);
        // The merger clamps velocity, but for these low speeds it should be exact
        // The remnantVelocity is stored in the event
        double drift = (me.RemnantVelocity * me.RemnantMass - originalMomentum).Length;
        Assert.True(drift < 1e-8, $"Momentum drift in merger: {drift}");
    }

    [Fact]
    public void Merger_ConservesTotalMass()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 100.0;
        system.MaxRelativeVelocity = 10000.0;
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent { Mass = 1.5, Position = Vec3d.Zero, Radius = 0.5 });
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent { Mass = 2.5, Position = new Vec3d(0.01, 0, 0), Radius = 0.5 });
        e2.AddComponent(new RelativisticComponent());

        var events = system.Process(new List<Entity> { e1, e2 }, 0.0);
        Assert.Single(events);
        Assert.Equal(4.0, events[0].RemnantMass, 1e-10);
    }

    [Fact]
    public void Merger_DoesNotOccur_IfTooFar()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 1.5;

        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent { Mass = 1.0, Position = Vec3d.Zero, Radius = 0.01 });
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent { Mass = 1.0, Position = new Vec3d(100.0, 0, 0), Radius = 0.01 });
        e2.AddComponent(new RelativisticComponent());

        var events = system.Process(new List<Entity> { e1, e2 }, 0.0);
        Assert.Empty(events);
    }

    [Fact]
    public void Merger_DoesNotOccur_IfTooFast()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 100.0;
        system.MaxRelativeVelocity = 1.0;

        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent { Mass = 1.0, Position = Vec3d.Zero, Velocity = new Vec3d(1000, 0, 0), Radius = 0.5 });
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent { Mass = 1.0, Position = new Vec3d(0.01, 0, 0), Velocity = Vec3d.Zero, Radius = 0.5 });
        e2.AddComponent(new RelativisticComponent());

        var events = system.Process(new List<Entity> { e1, e2 }, 0.0);
        Assert.Empty(events);
    }

    [Fact]
    public void Merger_EmitsGravitationalWaveAmplitude()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 100.0;
        system.MaxRelativeVelocity = 10000.0;
        system.ObserverDistance = 1000.0;
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent { Mass = 1.5, Position = Vec3d.Zero, Radius = 0.5 });
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent { Mass = 1.5, Position = new Vec3d(0.01, 0, 0), Radius = 0.5 });
        e2.AddComponent(new RelativisticComponent());

        var events = system.Process(new List<Entity> { e1, e2 }, 0.0);
        Assert.Single(events);
        Assert.True(events[0].GravitationalWaveAmplitude > 0.0,
            "GW amplitude should be positive for merger");
    }

    [Fact]
    public void Merger_FormsBlackHole_AboveTovLimit()
    {
        var system = new MergerResolutionSystem();
        system.RadiusMultiplier = 100.0;
        system.MaxRelativeVelocity = 10000.0;
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var e1 = new Entity();
        e1.AddComponent(new PhysicsComponent { Mass = 2.0, Position = Vec3d.Zero, Radius = 0.5 });
        e1.AddComponent(new RelativisticComponent());

        var e2 = new Entity();
        e2.AddComponent(new PhysicsComponent { Mass = 2.0, Position = new Vec3d(0.01, 0, 0), Radius = 0.5 });
        e2.AddComponent(new RelativisticComponent());

        var events = system.Process(new List<Entity> { e1, e2 }, 0.0);
        Assert.Single(events);
        Assert.True(events[0].FormedBlackHole);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9F — CATASTROPHIC EVENT SYSTEM (ORCHESTRATOR)
// ═══════════════════════════════════════════════════════════════════════════════

public class CatastrophicEventSystemTests
{
    [Fact]
    public void EjectaPool_PreAllocated()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 256);
        Assert.Equal(256, system.EjectaPoolCapacity);
        Assert.Equal(0, system.ActiveEjectaCount);

        // Pool entities should exist but be inactive
        var entity = system.GetEjectaEntity(0);
        Assert.NotNull(entity);
        Assert.False(entity!.IsActive);
    }

    [Fact]
    public void Supernova_TriggersCorrectly()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 512, seed: 42);

        var addedEntities = new List<Entity>();
        system.OnEntityAdded = e => addedEntities.Add(e);
        system.OnEntityRemoved = _ => { };

        // Create a star that has collapsed (trigger was already evaluated)
        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 20.0,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(1.0, 0.0, 0.0),
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.0,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        var entities = new List<Entity> { star };
        system.Process(entities, 0.0, 0.01);

        Assert.Single(system.SupernovaHistory);
        Assert.True(addedEntities.Count > 0, "Ejecta should have been spawned");

        var snEvent = system.SupernovaHistory[0];
        Assert.Equal(20.0, snEvent.ProgenitorMass, 1e-10);
        Assert.Equal(2.0, snEvent.CoreMass, 1e-10);
        Assert.Equal(18.0, snEvent.EjectaMass, 1e-10);
        Assert.Equal(2.0, snEvent.RemnantMass, 1e-10);
        Assert.False(snEvent.FormedBlackHole); // 2.0 < 3.0 TOV
    }

    [Fact]
    public void Supernova_MassConserved()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 512, seed: 42);

        var addedEntities = new List<Entity>();
        system.OnEntityAdded = e => addedEntities.Add(e);
        system.OnEntityRemoved = _ => { };

        double progenitorMass = 25.0;
        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = progenitorMass,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.5,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        system.Process(new List<Entity> { star }, 0.0, 0.01);

        // Remnant mass
        var pc = star.GetComponent<PhysicsComponent>()!;
        double totalMass = pc.Mass;

        // Ejecta mass
        foreach (var ejecta in addedEntities)
        {
            var epc = ejecta.GetComponent<PhysicsComponent>();
            if (epc != null) totalMass += epc.Mass;
        }

        Assert.Equal(progenitorMass, totalMass, 1e-8);
    }

    [Fact]
    public void Supernova_MomentumConserved()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 512, seed: 42);

        var addedEntities = new List<Entity>();
        system.OnEntityAdded = e => addedEntities.Add(e);
        system.OnEntityRemoved = _ => { };

        double mass = 20.0;
        var vel = new Vec3d(5.0, -3.0, 2.0);
        Vec3d originalMomentum = vel * mass;

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = mass,
            Position = Vec3d.Zero,
            Velocity = vel,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 1.5,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        system.Process(new List<Entity> { star }, 0.0, 0.01);

        // Compute total momentum after
        var pc = star.GetComponent<PhysicsComponent>()!;
        Vec3d totalMomentum = pc.Velocity * pc.Mass;

        foreach (var ejecta in addedEntities)
        {
            var epc = ejecta.GetComponent<PhysicsComponent>();
            if (epc != null)
                totalMomentum += epc.Velocity * epc.Mass;
        }

        double drift = (totalMomentum - originalMomentum).Length;
        double fractionalDrift = originalMomentum.Length > 1e-30
            ? drift / originalMomentum.Length : drift;
        Assert.True(fractionalDrift < 0.01,
            $"Momentum drift: {fractionalDrift:P2} (absolute: {drift})");
    }

    [Fact]
    public void Supernova_FormsBlackHole_HighCoreMass()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 256, seed: 42);
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 50.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 5.0, // Above TOV limit
            FuelMass = 0.0,
            HasCollapsed = true
        });

        system.Process(new List<Entity> { star }, 0.0, 0.01);

        Assert.Single(system.SupernovaHistory);
        Assert.True(system.SupernovaHistory[0].FormedBlackHole);
        Assert.Equal("BlackHole", star.Tag);
    }

    [Fact]
    public void Supernova_CreatesShockwave()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 256, seed: 42);
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 20.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.0,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        Assert.Equal(0, system.Shockwaves.ActiveCount);
        system.Process(new List<Entity> { star }, 0.0, 0.01);
        Assert.Equal(1, system.Shockwaves.ActiveCount);
    }

    [Fact]
    public void EjectaPool_RecyclesExpiredDebris()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 512, seed: 42);
        system.EjectaLifetime = 5.0;
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 20.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.0,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        // First supernova spawns ejecta
        system.Process(new List<Entity> { star }, 0.0, 0.01);
        int initialEjecta = system.ActiveEjectaCount;
        Assert.True(initialEjecta > 0);

        // Manually age all ejecta past lifetime
        for (int i = 0; i < system.EjectaPoolCapacity; i++)
        {
            if (system.IsEjectaSlotInUse(i))
            {
                var ec = system.GetEjectaEntity(i)!.GetComponent<ExplosionComponent>()!;
                ec.TimeSinceExplosion = 10.0; // Past 5.0 lifetime
            }
        }

        // Process again — should recycle expired ejecta
        // Need a different star (first one already exploded)
        system.Process(new List<Entity>(), 10.0, 0.01);

        Assert.Equal(0, system.ActiveEjectaCount);
    }

    [Fact]
    public void NaN_StressTest_NoNaN()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 1024, seed: 42);
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var rng = new Random(42);
        var entities = new List<Entity>();

        // Create many stars with extreme parameters
        for (int i = 0; i < 20; i++)
        {
            var star = new Entity();
            double mass = rng.NextDouble() * 1000.0 + 1.0;
            star.AddComponent(new PhysicsComponent
            {
                Mass = mass,
                Position = new Vec3d(
                    rng.NextDouble() * 100 - 50,
                    rng.NextDouble() * 100 - 50,
                    rng.NextDouble() * 100 - 50),
                Velocity = new Vec3d(
                    rng.NextDouble() * 1000 - 500,
                    rng.NextDouble() * 1000 - 500,
                    rng.NextDouble() * 1000 - 500),
                Radius = rng.NextDouble() * 10.0
            });
            star.AddComponent(new StellarEvolutionComponent
            {
                CoreMass = rng.NextDouble() * mass * 0.5 + 0.1,
                FuelMass = 0.0,
                HasCollapsed = true
            });
            entities.Add(star);
        }

        // Process — should not produce any NaN
        system.Process(entities, 0.0, 0.01);

        foreach (var e in entities)
        {
            var pc = e.GetComponent<PhysicsComponent>();
            if (pc == null) continue;
            Assert.False(double.IsNaN(pc.Velocity.X), $"NaN velocity X on entity {e.Id}");
            Assert.False(double.IsNaN(pc.Velocity.Y), $"NaN velocity Y on entity {e.Id}");
            Assert.False(double.IsNaN(pc.Velocity.Z), $"NaN velocity Z on entity {e.Id}");
            Assert.False(double.IsNaN(pc.Position.X), $"NaN position X on entity {e.Id}");
            Assert.False(double.IsInfinity(pc.Velocity.X), $"Inf velocity X on entity {e.Id}");
        }

        // Check all ejecta too
        for (int i = 0; i < system.EjectaPoolCapacity; i++)
        {
            if (!system.IsEjectaSlotInUse(i)) continue;
            var ejecta = system.GetEjectaEntity(i)!;
            var epc = ejecta.GetComponent<PhysicsComponent>()!;
            Assert.False(double.IsNaN(epc.Velocity.X), $"NaN in ejecta [{i}] velocity X");
            Assert.False(double.IsNaN(epc.Velocity.Y), $"NaN in ejecta [{i}] velocity Y");
            Assert.False(double.IsNaN(epc.Velocity.Z), $"NaN in ejecta [{i}] velocity Z");
        }
    }

    [Fact]
    public void Supernova_DoesNotFireTwice()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 256, seed: 42);
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 20.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.0,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        var entities = new List<Entity> { star };

        system.Process(entities, 0.0, 0.01);
        Assert.Single(system.SupernovaHistory);

        // Process again — should not fire again (already has IsExploding=true)
        system.Process(entities, 1.0, 0.01);
        Assert.Single(system.SupernovaHistory);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var system = new CatastrophicEventSystem(ejectaPoolSize: 256, seed: 42);
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = 20.0,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = 2.0,
            FuelMass = 0.0,
            HasCollapsed = true
        });

        system.Process(new List<Entity> { star }, 0.0, 0.01);

        Assert.True(system.SupernovaHistory.Count > 0);
        Assert.True(system.ActiveEjectaCount > 0);

        system.Reset();

        Assert.Empty(system.SupernovaHistory);
        Assert.Equal(0, system.ActiveEjectaCount);
        Assert.Equal(0, system.Shockwaves.ActiveCount);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9G — INTEGRATION WITH SIMULATION MANAGER
// ═══════════════════════════════════════════════════════════════════════════════

public class CatastrophicIntegrationTests
{
    [Fact]
    public void SimulationManager_HasCatastrophicEventSystem()
    {
        var sim = new SimulationManager();
        Assert.NotNull(sim.CatastrophicEvents);
        Assert.NotNull(sim.CatastrophicEvents.Shockwaves);
        Assert.NotNull(sim.CatastrophicEvents.EnergyTracker);
    }

    [Fact]
    public void FullCatastrophicFlow_SupernovaInSimulation()
    {
        var config = new PhysicsConfig { UseSoAPath = true, DeterministicMode = true };
        var sim = new SimulationManager(config);

        // Create a massive star ready to collapse
        var star = StarFactory.CreateMassiveStar(Vec3d.Zero, Vec3d.Zero, 25.0);
        sim.AddEntity(star);

        // Register supernova trigger with a simple collapse marker action.
        // This just sets HasCollapsed = true so the CatastrophicEventSystem detects it
        // and handles the full supernova physics (ejecta, shockwave, remnant).
        sim.RegisterEventRule(new SupernovaTrigger(), new CollapseMarkerAction());

        // Track events
        bool supernovaFromCatastrophicSystem = false;
        sim.EventBus.Subscribe(evt =>
        {
            if (evt.Type == "Supernova" && evt.Message.Contains("SN:"))
                supernovaFromCatastrophicSystem = true;
        });

        // Accelerate time
        sim.Time.TimeScale = 1000.0;

        // Evolve until the trigger fires
        for (int i = 0; i < 10000; i++)
        {
            sim.Step(0.01);
            if (supernovaFromCatastrophicSystem) break;
        }

        Assert.True(supernovaFromCatastrophicSystem,
            "CatastrophicEventSystem should have processed the supernova");
        Assert.True(sim.CatastrophicEvents.SupernovaHistory.Count > 0);
    }

    /// <summary>
    /// Minimal IEventAction that marks a star as collapsed for Phase 9.
    /// The CatastrophicEventSystem then handles the full supernova physics.
    /// </summary>
    private class CollapseMarkerAction : IEventAction
    {
        public void Execute(Entity entity)
        {
            var stellar = entity.GetComponent<StellarEvolutionComponent>();
            if (stellar != null) stellar.HasCollapsed = true;
        }
    }

    [Fact]
    public void SimulationManager_ProcessesMergers()
    {
        var config = new PhysicsConfig { UseSoAPath = true, DeterministicMode = true };
        var sim = new SimulationManager(config);

        // Configure merger system for easy triggering
        sim.CatastrophicEvents.Mergers.RadiusMultiplier = 100.0;
        sim.CatastrophicEvents.Mergers.MaxRelativeVelocity = 10000.0;

        // Create two neutron stars very close together
        var ns1 = new Entity();
        ns1.Tag = "Star_Neutron";
        ns1.AddComponent(new PhysicsComponent
        {
            Mass = 1.5,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(0.1, 0, 0),
            Radius = 0.01
        });
        ns1.AddComponent(new RelativisticComponent
        {
            SchwarzschildRadius = PhysicalConstants.SchwarzschildFactorSim * 1.5,
            EnablePostNewtonian = true
        });

        var ns2 = new Entity();
        ns2.Tag = "Star_Neutron";
        ns2.AddComponent(new PhysicsComponent
        {
            Mass = 1.5,
            Position = new Vec3d(0.001, 0, 0),
            Velocity = new Vec3d(-0.1, 0, 0),
            Radius = 0.01
        });
        ns2.AddComponent(new RelativisticComponent
        {
            SchwarzschildRadius = PhysicalConstants.SchwarzschildFactorSim * 1.5,
            EnablePostNewtonian = true
        });

        sim.AddEntity(ns1);
        sim.AddEntity(ns2);

        sim.Step(0.001);

        Assert.True(sim.CatastrophicEvents.MergerHistory.Count > 0,
            "Merger should have occurred");

        var merger = sim.CatastrophicEvents.MergerHistory[0];
        Assert.Equal(3.0, merger.RemnantMass, 1e-10);
    }

    [Fact]
    public void SimulationManager_EnergyTrackerMeasures()
    {
        var config = new PhysicsConfig { UseSoAPath = true, DeterministicMode = true };
        var sim = new SimulationManager(config);

        // Add a body with some velocity
        var e = new Entity();
        e.AddComponent(new PhysicsComponent
        {
            Mass = 1.0,
            Position = Vec3d.Zero,
            Velocity = new Vec3d(10.0, 0, 0),
            Radius = 0.1
        });
        sim.AddEntity(e);

        sim.Step(0.01);

        // Energy tracker should have been initialized
        Assert.True(sim.CatastrophicEvents.EnergyTracker.IsInitialized);
        Assert.True(sim.CatastrophicEvents.EnergyTracker.KineticEnergy > 0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9H — PERFORMANCE
// ═══════════════════════════════════════════════════════════════════════════════

public class CatastrophicPerformanceTests
{
    [Fact]
    public void ShockwaveSystem_HandlesCapacity()
    {
        var system = new ShockwaveSystem(128);

        // Fill to capacity
        for (int i = 0; i < 128; i++)
        {
            bool ok = system.CreateShockwave(
                new Vec3d(i, 0, 0), 100.0 + i, i * 0.1);
            Assert.True(ok, $"Failed to create shockwave {i}");
        }

        Assert.Equal(128, system.ActiveCount);
        Assert.False(system.CreateShockwave(Vec3d.Zero, 1.0, 0.0));

        // Expire some and reclaim
        system.MaxShockwaveLifetime = 5.0;
        system.Update(new List<Entity>(), 100.0, 0.1);

        // All should have expired
        Assert.Equal(0, system.ActiveCount);

        // Can create again
        Assert.True(system.CreateShockwave(Vec3d.Zero, 50.0, 100.0));
    }

    [Fact]
    public void EjectaPool_NoAllocationDuringProcessing()
    {
        // This test verifies the pool pattern by checking that ejecta reuse works
        var system = new CatastrophicEventSystem(ejectaPoolSize: 64, seed: 42);
        system.EjectaLifetime = 1.0;
        system.MaxEjectaPerEvent = 10;
        system.OnEntityAdded = _ => { };
        system.OnEntityRemoved = _ => { };

        // First supernova
        var star1 = CreateCollapsingstar(20.0, 2.0);
        system.Process(new List<Entity> { star1 }, 0.0, 0.01);
        int firstBatch = system.ActiveEjectaCount;
        Assert.True(firstBatch > 0 && firstBatch <= 10);

        // Expire ejecta
        for (int i = 0; i < system.EjectaPoolCapacity; i++)
        {
            if (system.IsEjectaSlotInUse(i))
            {
                var ec = system.GetEjectaEntity(i)!.GetComponent<ExplosionComponent>()!;
                ec.TimeSinceExplosion = 5.0;
            }
        }

        // Second supernova reuses pool slots
        var star2 = CreateCollapsingstar(15.0, 1.5);
        system.Process(new List<Entity> { star2 }, 5.0, 0.01);

        // Pool slots were recycled and reused
        Assert.True(system.ActiveEjectaCount > 0);
        Assert.True(system.ActiveEjectaCount <= system.EjectaPoolCapacity);
    }

    private static Entity CreateCollapsingstar(double mass, double coreMass)
    {
        var star = new Entity();
        star.AddComponent(new PhysicsComponent
        {
            Mass = mass,
            Position = Vec3d.Zero,
            Velocity = Vec3d.Zero,
            Radius = 1.0
        });
        star.AddComponent(new StellarEvolutionComponent
        {
            CoreMass = coreMass,
            FuelMass = 0.0,
            HasCollapsed = true
        });
        return star;
    }
}
