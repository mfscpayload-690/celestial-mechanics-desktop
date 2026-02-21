using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;

namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Handles supernova explosion mechanics as an IEventAction.
///
/// When executed on a star entity:
/// 1. Removes excess mass (retains neutron star remnant ≈ 1.4 M☉)
/// 2. Spawns ejecta particles with radial velocities
/// 3. Conserves total momentum: M_star * V_star = M_remnant * V_remnant + Σ(m_i * v_i)
/// 4. Publishes explosion event on the EventBus for GW detection
///
/// Constraints:
/// - No NaN velocities (clamped to max ejecta speed)
/// - No Barnes-Hut tree corruption (ejecta positions are offset from star)
/// - Ejecta count capped by MaxEjectaCount
/// </summary>
public sealed class ExplosionSystem : IEventAction
{
    /// <summary>Maximum number of ejecta particles per supernova.</summary>
    public int MaxEjectaCount { get; set; } = 100;

    /// <summary>Remnant mass after supernova in solar masses (normalized).</summary>
    public double RemnantMass { get; set; } = 1.4;

    /// <summary>Maximum ejecta velocity in simulation units.</summary>
    public double MaxEjectaSpeed { get; set; } = 100.0;

    /// <summary>Minimum radial offset for spawned ejecta (prevents zero-separation).</summary>
    public double MinEjectaOffset { get; set; } = 0.01;

    /// <summary>
    /// Callback invoked for each spawned ejecta entity.
    /// Set by SimulationManager to register new entities.
    /// </summary>
    public Action<Entity>? OnEjectaSpawned { get; set; }

    /// <summary>Optional EventBus for publishing explosion events.</summary>
    public EventBus? EventBus { get; set; }

    private readonly Random _rng = new(42);

    public void Execute(Entity entity)
    {
        var physics = entity.GetComponent<PhysicsComponent>();
        var stellar = entity.GetComponent<StellarEvolutionComponent>();
        if (physics == null || stellar == null) return;

        double originalMass = physics.Mass;
        Vec3d originalVelocity = physics.Velocity;
        Vec3d originalPosition = physics.Position;
        Vec3d originalMomentum = originalVelocity * originalMass;

        // Mark as collapsed
        stellar.HasCollapsed = true;

        // Compute ejecta mass
        double ejectaMass = originalMass - RemnantMass;
        if (ejectaMass <= 0.0) return; // Not enough mass to eject

        // Determine ejecta count: proportional to ejected mass, capped
        int ejectaCount = System.Math.Min(MaxEjectaCount, System.Math.Max(4, (int)(ejectaMass * 10)));

        double massPerEjecta = ejectaMass / ejectaCount;

        // Generate random radial directions (deterministic via seeded RNG)
        // Use Fibonacci sphere for uniform distribution
        Vec3d totalEjectaMomentum = Vec3d.Zero;
        var ejectaEntities = new List<Entity>(ejectaCount);

        for (int i = 0; i < ejectaCount; i++)
        {
            // Fibonacci sphere point distribution for uniform radial directions
            double phi = System.Math.Acos(1.0 - 2.0 * (i + 0.5) / ejectaCount);
            double theta = System.Math.PI * (1.0 + System.Math.Sqrt(5.0)) * i;

            double sinPhi = System.Math.Sin(phi);
            double dx = sinPhi * System.Math.Cos(theta);
            double dy = sinPhi * System.Math.Sin(theta);
            double dz = System.Math.Cos(phi);

            // Random speed variation: 50%–100% of max
            double speed = MaxEjectaSpeed * (0.5 + 0.5 * _rng.NextDouble());

            // Clamp to prevent extreme values
            speed = System.Math.Min(speed, MaxEjectaSpeed);

            Vec3d ejectaVelocity = new Vec3d(dx * speed, dy * speed, dz * speed);
            Vec3d ejectaPosition = originalPosition + new Vec3d(
                dx * MinEjectaOffset,
                dy * MinEjectaOffset,
                dz * MinEjectaOffset);

            // Create ejecta entity
            var ejecta = new Entity();
            ejecta.Tag = "Ejecta";
            ejecta.AddComponent(new PhysicsComponent(
                massPerEjecta,
                ejectaPosition,
                originalVelocity + ejectaVelocity, // Add star velocity for Galilean boost
                0.001 // Small radius
            ));
            ejecta.AddComponent(new ExplosionComponent
            {
                IsDebris = true,
                TimeSinceExplosion = 0.0
            });

            ejectaEntities.Add(ejecta);
            totalEjectaMomentum += (originalVelocity + ejectaVelocity) * massPerEjecta;
        }

        // Set remnant mass
        physics.Mass = RemnantMass;
        physics.Radius = System.Math.Max(physics.Radius * 0.01, 0.001); // Compact remnant

        // Conserve momentum: adjust remnant velocity
        // M_orig * V_orig = M_remnant * V_remnant + totalEjectaMomentum
        // V_remnant = (M_orig * V_orig - totalEjectaMomentum) / M_remnant
        if (RemnantMass > 0.0)
        {
            Vec3d correctedRemnantVel = (originalMomentum - totalEjectaMomentum) / RemnantMass;

            // Clamp remnant velocity to prevent NaN or extreme values
            if (double.IsNaN(correctedRemnantVel.X) || double.IsNaN(correctedRemnantVel.Y) || double.IsNaN(correctedRemnantVel.Z))
            {
                physics.Velocity = originalVelocity;
            }
            else
            {
                double velMag = correctedRemnantVel.Length;
                if (velMag > MaxEjectaSpeed * 10.0)
                {
                    correctedRemnantVel = correctedRemnantVel * (MaxEjectaSpeed * 10.0 / velMag);
                }
                physics.Velocity = correctedRemnantVel;
            }
        }

        // Register ejecta entities
        if (OnEjectaSpawned != null)
        {
            for (int i = 0; i < ejectaEntities.Count; i++)
            {
                OnEjectaSpawned(ejectaEntities[i]);
            }
        }

        // Mark entity as exploding
        var explosionComp = entity.GetComponent<ExplosionComponent>();
        if (explosionComp != null)
        {
            explosionComp.IsExploding = true;
        }

        // Publish event for GW analyzer and other observers
        EventBus?.Publish(new SimulationEvent
        {
            Type = "Supernova",
            Message = $"Supernova: entity {entity.Id}, ejected {ejectaMass:F3} M☉ in {ejectaCount} fragments",
            Time = 0.0 // Will be set by caller if needed
        });
    }
}
