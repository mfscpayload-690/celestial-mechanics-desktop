using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;

namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Creates compact object remnants after supernova core collapse.
///
/// Decision logic:
///   Core mass &lt; 3 M☉ → Neutron star (very high density, small radius, strong gravity)
///   Core mass ≥ 3 M☉ → Black hole (Schwarzschild radius rs = 2GM/c²)
///
/// Attaches appropriate components (RelativisticComponent) dynamically.
/// </summary>
public static class RemnantFormationSystem
{
    /// <summary>Tolman–Oppenheimer–Volkoff limit: max NS mass (M☉).</summary>
    public const double TovLimit = 3.0;

    /// <summary>
    /// Transform an existing entity into a compact remnant after supernova.
    /// Modifies components in-place — no new Entity allocation required.
    /// </summary>
    /// <param name="entity">The progenitor entity (already had mass reduced to remnant).</param>
    /// <param name="remnantMass">Mass of the remnant (M☉).</param>
    /// <param name="currentTime">Current simulation time for event recording.</param>
    /// <returns>CollapseEvent with details, or null if entity lacks PhysicsComponent.</returns>
    public static CollapseEvent? FormRemnant(Entity entity, double remnantMass, double currentTime)
    {
        var physics = entity.GetComponent<PhysicsComponent>();
        if (physics == null) return null;

        bool isBlackHole = remnantMass >= TovLimit;

        if (isBlackHole)
        {
            return FormBlackHole(entity, physics, remnantMass, currentTime);
        }
        else
        {
            return FormNeutronStar(entity, physics, remnantMass, currentTime);
        }
    }

    private static CollapseEvent FormBlackHole(Entity entity, PhysicsComponent physics,
        double mass, double currentTime)
    {
        // Schwarzschild radius: rs = 2 * G_Sim * M / c²_Sim
        double rs = PhysicalConstants.SchwarzschildFactorSim * mass;
        double visualRadius = System.Math.Max(rs, 0.01 * System.Math.Cbrt(mass));

        physics.Mass = mass;
        physics.Radius = visualRadius;
        physics.Density = 1.0; // Nominal for BH
        physics.IsCollidable = true;

        // Add or update RelativisticComponent
        var rel = entity.GetComponent<RelativisticComponent>();
        if (rel == null)
        {
            rel = new RelativisticComponent();
            entity.AddComponent(rel);
        }
        rel.SchwarzschildRadius = rs;
        rel.EnablePostNewtonian = true;
        rel.EnableLensing = true;

        entity.Tag = "BlackHole";

        return new CollapseEvent
        {
            EntityId = entity.Id,
            Position = physics.Position,
            CoreMass = mass,
            RemnantType = "BlackHole",
            SchwarzschildRadius = rs,
            Time = currentTime
        };
    }

    private static CollapseEvent FormNeutronStar(Entity entity, PhysicsComponent physics,
        double mass, double currentTime)
    {
        physics.Mass = mass;
        physics.Density = DensityModel.NeutronStarDensity;
        physics.Radius = DensityModel.ComputeRadius(mass, DensityModel.NeutronStarDensity);
        physics.IsCollidable = true;

        // Add or update RelativisticComponent
        var rel = entity.GetComponent<RelativisticComponent>();
        if (rel == null)
        {
            rel = new RelativisticComponent();
            entity.AddComponent(rel);
        }
        rel.ComputeSchwarzschildRadius(mass);
        rel.EnablePostNewtonian = true;

        entity.Tag = "Star_Neutron";

        return new CollapseEvent
        {
            EntityId = entity.Id,
            Position = physics.Position,
            CoreMass = mass,
            RemnantType = "NeutronStar",
            SchwarzschildRadius = rel.SchwarzschildRadius,
            Time = currentTime
        };
    }
}
