using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Manages spherical shockwave propagation using the Sedov–Taylor blast wave model.
///
/// Shock radius evolves as:
///   R(t) = ξ · (E / ρ₀)^(1/5) · t^(2/5)
///
/// where E = explosion energy, ρ₀ = ambient density, ξ ≈ 1.15.
///
/// Shockwaves apply impulse to bodies within the expanding front.
/// Uses pre-allocated fixed-size array — no per-frame heap allocations.
/// Evaluation is O(n) per shockwave per active body.
/// </summary>
public sealed class ShockwaveSystem
{
    /// <summary>Sedov–Taylor self-similar constant ξ ≈ 1.15.</summary>
    private const double SedovConstant = 1.15;

    /// <summary>Default ambient density for Sedov–Taylor formula (sim units).</summary>
    public double AmbientDensity { get; set; } = 1.0;

    /// <summary>Shockwave impulse strength multiplier.</summary>
    public double ImpulseStrength { get; set; } = 1.0;

    /// <summary>Maximum shockwave lifetime before expiry (sim time units).</summary>
    public double MaxShockwaveLifetime { get; set; } = 50.0;

    /// <summary>Softening distance to prevent singularity at shockwave centre.</summary>
    public double SofteningDistance { get; set; } = 0.01;

    /// <summary>Thickness of the shock front shell (fraction of radius).</summary>
    public double ShellThicknessFraction { get; set; } = 0.2;

    // Pre-allocated shockwave slots
    private readonly ShockwaveData[] _shockwaves;
    private int _activeCount;

    /// <summary>Maximum concurrent shockwaves (fixed at construction).</summary>
    public int Capacity { get; }

    /// <summary>Number of currently active shockwaves.</summary>
    public int ActiveCount => _activeCount;

    public ShockwaveSystem(int capacity = 64)
    {
        Capacity = capacity;
        _shockwaves = new ShockwaveData[capacity];
        _activeCount = 0;
    }

    /// <summary>
    /// Create a new shockwave from an explosion event.
    /// </summary>
    /// <param name="origin">Explosion centre.</param>
    /// <param name="energy">Total explosion energy in sim units.</param>
    /// <param name="birthTime">Simulation time when the shockwave was created.</param>
    /// <returns>True if the shockwave was created; false if pool is full.</returns>
    public bool CreateShockwave(Vec3d origin, double energy, double birthTime)
    {
        if (energy <= 0.0) return false;

        // Find a free slot
        for (int i = 0; i < Capacity; i++)
        {
            if (!_shockwaves[i].IsActive)
            {
                _shockwaves[i] = new ShockwaveData
                {
                    Origin = origin,
                    Energy = energy,
                    BirthTime = birthTime,
                    IsActive = true
                };
                _activeCount++;
                return true;
            }
        }
        return false; // Pool full
    }

    /// <summary>
    /// Compute the Sedov–Taylor radius for a shockwave at elapsed time Δt.
    /// R(Δt) = ξ · (E / ρ₀)^(1/5) · Δt^(2/5)
    /// </summary>
    public double ComputeShockRadius(double energy, double elapsedTime)
    {
        if (elapsedTime <= 0.0 || energy <= 0.0 || AmbientDensity <= 0.0)
            return 0.0;

        double factor = energy / AmbientDensity;
        return SedovConstant * System.Math.Pow(factor, 0.2) * System.Math.Pow(elapsedTime, 0.4);
    }

    /// <summary>
    /// Apply shockwave impulses to all affected entities.
    /// Called once per simulation step after catastrophic events are processed.
    /// </summary>
    /// <param name="entities">Active entity list.</param>
    /// <param name="currentTime">Current simulation time.</param>
    /// <param name="dt">Current timestep.</param>
    public void Update(IReadOnlyList<Entity> entities, double currentTime, double dt)
    {
        if (_activeCount == 0) return;

        for (int s = 0; s < Capacity; s++)
        {
            ref var sw = ref _shockwaves[s];
            if (!sw.IsActive) continue;

            double elapsed = currentTime - sw.BirthTime;

            // Expire old shockwaves
            if (elapsed > MaxShockwaveLifetime)
            {
                sw.IsActive = false;
                _activeCount--;
                continue;
            }

            if (elapsed <= 0.0) continue;

            double radius = ComputeShockRadius(sw.Energy, elapsed);
            if (radius <= 0.0) continue;

            double shellThickness = radius * ShellThicknessFraction;
            double innerRadius = radius - shellThickness;
            if (innerRadius < 0.0) innerRadius = 0.0;

            // Shock velocity: dR/dt = (2/5) * R / t
            double shockVelocity = elapsed > 1e-30 ? 0.4 * radius / elapsed : 0.0;

            // Apply impulse to bodies within the shock shell
            for (int e = 0; e < entities.Count; e++)
            {
                var entity = entities[e];
                if (!entity.IsActive) continue;

                var pc = entity.GetComponent<PhysicsComponent>();
                if (pc == null) continue;

                Vec3d delta = pc.Position - sw.Origin;
                double dist = delta.Length;

                // Skip bodies at the origin (the remnant itself) using softening
                if (dist < SofteningDistance) continue;

                // Only affect bodies within the shock shell
                if (dist < innerRadius || dist > radius + shellThickness)
                    continue;

                // Impulse decays with distance: F ∝ E / (4π r²)
                double impulsePerUnitMass = ImpulseStrength * sw.Energy /
                    (4.0 * System.Math.PI * dist * dist + SofteningDistance);

                // Scale by timestep and apply as velocity kick in radial direction
                Vec3d direction = delta / dist;
                double velocityKick = impulsePerUnitMass * dt / System.Math.Max(pc.Mass, 1e-30);

                // Clamp kick to prevent instability
                double maxKick = shockVelocity * 0.5; // Don't exceed half the shock speed
                if (velocityKick > maxKick) velocityKick = maxKick;

                pc.Velocity = pc.Velocity + direction * velocityKick;

                // Stability: clamp final velocity
                pc.Velocity = PhysicsExtensions.MomentumConservationUtility.ClampVelocity(pc.Velocity);
            }
        }
    }

    /// <summary>Reset all shockwaves.</summary>
    public void Reset()
    {
        for (int i = 0; i < Capacity; i++)
            _shockwaves[i].IsActive = false;
        _activeCount = 0;
    }

    /// <summary>Pre-allocated shockwave descriptor. Value type to avoid heap allocation.</summary>
    private struct ShockwaveData
    {
        public Vec3d Origin;
        public double Energy;
        public double BirthTime;
        public bool IsActive;
    }
}
