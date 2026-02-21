using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;
using CelestialMechanics.Simulation.PhysicsExtensions;

namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Detects and resolves binary compact object mergers.
///
/// Triggers when two compact objects (neutron stars, black holes) satisfy:
///   distance &lt; sum of radii   AND   relative velocity within merge threshold
///
/// Upon merger:
///   1. Combine masses
///   2. Compute merged velocity via momentum conservation: p = mv
///   3. Emit gravitational wave burst (amplitude ∝ mass² / separation)
///   4. Remove original entities
///   5. Spawn remnant via RemnantFormationSystem
/// </summary>
public sealed class MergerResolutionSystem
{
    /// <summary>
    /// Multiplier for radii overlap check. Merger occurs when
    /// distance &lt; (r1 + r2) * RadiusMultiplier.
    /// </summary>
    public double RadiusMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Maximum relative velocity for merger (sim units).
    /// Prevents mergers from hyperbolic encounters.
    /// </summary>
    public double MaxRelativeVelocity { get; set; } = 500.0;

    /// <summary>
    /// GW burst strain prefactor: h ∝ prefactor * (m1*m2) / (r * d_observer).
    /// Uses quadrupole approximation.
    /// </summary>
    private static readonly double GwBurstPrefactor = 4.0 / PhysicalConstants.C_Sim4;

    /// <summary>Observer distance for GW amplitude computation.</summary>
    public double ObserverDistance { get; set; } = 1000.0;

    /// <summary>Callback for removing merged entities from simulation.</summary>
    public Action<Entity>? OnEntityRemoved { get; set; }

    /// <summary>Callback for adding the merged remnant entity.</summary>
    public Action<Entity>? OnEntityAdded { get; set; }

    /// <summary>Optional EventBus for publishing merger events.</summary>
    public EventBus? EventBus { get; set; }

    /// <summary>
    /// Scan all entity pairs for merger conditions and resolve any found.
    /// Returns list of merger events that occurred this step.
    /// </summary>
    public List<MergerEvent> Process(IReadOnlyList<Entity> entities, double currentTime)
    {
        var events = new List<MergerEvent>();

        // Collect compact objects (entities with RelativisticComponent)
        // Use a simple list to avoid repeated GetComponent calls
        var compactObjects = new List<(Entity Entity, PhysicsComponent Physics, RelativisticComponent Rel)>();

        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            if (!e.IsActive) continue;

            var pc = e.GetComponent<PhysicsComponent>();
            var rel = e.GetComponent<RelativisticComponent>();
            if (pc == null || rel == null) continue;

            compactObjects.Add((e, pc, rel));
        }

        // Track which entities have already been merged this step
        var merged = new HashSet<Guid>();

        // Pairwise merger check (O(k²) where k = compact objects, typically small)
        for (int a = 0; a < compactObjects.Count; a++)
        {
            if (merged.Contains(compactObjects[a].Entity.Id)) continue;

            for (int b = a + 1; b < compactObjects.Count; b++)
            {
                if (merged.Contains(compactObjects[b].Entity.Id)) continue;

                var (eA, pcA, relA) = compactObjects[a];
                var (eB, pcB, relB) = compactObjects[b];

                // Distance check
                double dist = pcA.Position.DistanceTo(pcB.Position);
                double mergeRadius = (pcA.Radius + pcB.Radius) * RadiusMultiplier;

                if (dist > mergeRadius) continue;

                // Relative velocity check
                Vec3d relVel = pcA.Velocity - pcB.Velocity;
                if (relVel.Length > MaxRelativeVelocity) continue;

                // ── Execute merger ──
                var mergerEvent = ResolveMerger(eA, pcA, eB, pcB, dist, currentTime);
                events.Add(mergerEvent);

                merged.Add(eA.Id);
                merged.Add(eB.Id);
                break; // Entity A can only merge once per step
            }
        }

        return events;
    }

    private MergerEvent ResolveMerger(
        Entity entityA, PhysicsComponent pcA,
        Entity entityB, PhysicsComponent pcB,
        double separation, double currentTime)
    {
        double totalMass = pcA.Mass + pcB.Mass;
        Vec3d mergedPos = (pcA.Position * pcA.Mass + pcB.Position * pcB.Mass) / totalMass;

        // Momentum-conserving merged velocity
        Vec3d mergedVel = MomentumConservationUtility.ComputeMergedVelocity(
            pcA.Mass, pcA.Velocity, pcB.Mass, pcB.Velocity);

        // GW burst amplitude ∝ (m1 * m2) / (separation * observer_distance)
        double gwAmplitude = 0.0;
        if (separation > 0.0 && ObserverDistance > 0.0)
        {
            gwAmplitude = GwBurstPrefactor * pcA.Mass * pcB.Mass /
                (separation * ObserverDistance);
        }

        // Determine remnant type
        bool isBlackHole = totalMass >= RemnantFormationSystem.TovLimit;

        // Create remnant entity
        var remnant = new Entity();
        remnant.Tag = isBlackHole ? "BlackHole" : "Star_Neutron";

        double rs = PhysicalConstants.SchwarzschildFactorSim * totalMass;
        double visualRadius = isBlackHole
            ? System.Math.Max(rs, 0.01 * System.Math.Cbrt(totalMass))
            : DensityModel.ComputeRadius(totalMass, DensityModel.NeutronStarDensity);

        remnant.AddComponent(new PhysicsComponent
        {
            Mass = totalMass,
            Position = mergedPos,
            Velocity = mergedVel,
            Density = isBlackHole ? 1.0 : DensityModel.NeutronStarDensity,
            Radius = visualRadius,
            IsCollidable = true
        });

        var rel = new RelativisticComponent
        {
            SchwarzschildRadius = rs,
            EnablePostNewtonian = true,
            EnableLensing = isBlackHole
        };
        remnant.AddComponent(rel);

        // Deactivate original entities and add remnant
        entityA.IsActive = false;
        entityB.IsActive = false;
        OnEntityRemoved?.Invoke(entityA);
        OnEntityRemoved?.Invoke(entityB);
        OnEntityAdded?.Invoke(remnant);

        // Publish event
        var mergerEvent = new MergerEvent
        {
            EntityAId = entityA.Id,
            EntityBId = entityB.Id,
            MassA = pcA.Mass,
            MassB = pcB.Mass,
            RemnantMass = totalMass,
            RemnantVelocity = mergedVel,
            Position = mergedPos,
            GravitationalWaveAmplitude = gwAmplitude,
            Separation = separation,
            Time = currentTime,
            FormedBlackHole = isBlackHole
        };

        EventBus?.Publish(new SimulationEvent
        {
            Type = "Merger",
            Message = $"Merger: {pcA.Mass:F2}+{pcB.Mass:F2} M☉ → {totalMass:F2} M☉ " +
                      $"{(isBlackHole ? "BH" : "NS")}, GW h={gwAmplitude:E2}",
            Time = currentTime
        });

        return mergerEvent;
    }
}
