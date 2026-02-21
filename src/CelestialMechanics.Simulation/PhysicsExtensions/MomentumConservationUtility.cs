using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.PhysicsExtensions;

/// <summary>
/// Static utilities for momentum-safe mass redistribution and velocity correction.
/// All operations clamp velocities to the relativistic threshold (0.3c).
/// </summary>
public static class MomentumConservationUtility
{
    /// <summary>Relativistic velocity cap: 0.3c in simulation units.</summary>
    public static readonly double RelativisticVelocityCap = 0.3 * PhysicalConstants.C_Sim;

    /// <summary>
    /// Compute the velocity-corrected remnant after mass ejection.
    /// Ensures total momentum is conserved:
    ///   M_orig * V_orig = M_remnant * V_remnant + Σ(m_i * v_i)
    /// </summary>
    /// <param name="originalMomentum">Total momentum before event.</param>
    /// <param name="ejectaMomentum">Total momentum of all ejecta.</param>
    /// <param name="remnantMass">Mass of the remnant.</param>
    /// <returns>Corrected remnant velocity, clamped to relativistic limit.</returns>
    public static Vec3d ComputeRemnantVelocity(Vec3d originalMomentum, Vec3d ejectaMomentum, double remnantMass)
    {
        if (remnantMass <= 0.0)
            return Vec3d.Zero;

        Vec3d vel = (originalMomentum - ejectaMomentum) / remnantMass;
        return ClampVelocity(vel);
    }

    /// <summary>
    /// Compute merged velocity from two-body coalescence.
    /// v_merged = (m1*v1 + m2*v2) / (m1 + m2)
    /// </summary>
    public static Vec3d ComputeMergedVelocity(double mass1, Vec3d vel1, double mass2, Vec3d vel2)
    {
        double totalMass = mass1 + mass2;
        if (totalMass <= 0.0)
            return Vec3d.Zero;

        Vec3d merged = (vel1 * mass1 + vel2 * mass2) / totalMass;
        return ClampVelocity(merged);
    }

    /// <summary>
    /// Compute total momentum of a set of entities.
    /// </summary>
    public static Vec3d ComputeTotalMomentum(IReadOnlyList<Entity> entities)
    {
        Vec3d total = Vec3d.Zero;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].IsActive) continue;
            var pc = entities[i].GetComponent<PhysicsComponent>();
            if (pc == null) continue;
            total += pc.Velocity * pc.Mass;
        }
        return total;
    }

    /// <summary>
    /// Validate that momentum drift is within tolerance.
    /// Returns the fractional drift: |current - original| / |original|.
    /// </summary>
    public static double ValidateMomentumDrift(Vec3d originalMomentum, Vec3d currentMomentum)
    {
        double origMag = originalMomentum.Length;
        if (origMag < 1e-30)
            return currentMomentum.Length;

        return (currentMomentum - originalMomentum).Length / origMag;
    }

    /// <summary>
    /// Clamp velocity magnitude to the relativistic cap (0.3c).
    /// Returns Vec3d.Zero for NaN inputs.
    /// </summary>
    public static Vec3d ClampVelocity(Vec3d velocity)
    {
        if (double.IsNaN(velocity.X) || double.IsNaN(velocity.Y) || double.IsNaN(velocity.Z))
            return Vec3d.Zero;

        if (double.IsInfinity(velocity.X) || double.IsInfinity(velocity.Y) || double.IsInfinity(velocity.Z))
            return Vec3d.Zero;

        double mag = velocity.Length;
        if (mag > RelativisticVelocityCap)
            return velocity * (RelativisticVelocityCap / mag);

        return velocity;
    }

    /// <summary>
    /// Distribute ejecta mass into N particles with Fibonacci-sphere directions.
    /// Returns arrays of (direction, speed) for each ejecta particle.
    /// Speed is derived from total kinetic energy: KE = 0.5 * m_total * v^2.
    /// </summary>
    public static void ComputeEjectaVelocities(
        int count, double totalKineticEnergy, double totalEjectaMass,
        Vec3d starVelocity, double speedVariation,
        double[] outSpeedX, double[] outSpeedY, double[] outSpeedZ,
        Random rng)
    {
        if (count <= 0 || totalEjectaMass <= 0.0) return;

        // v = sqrt(2 * KE / m), capped at 0.3c
        double baseSpeed = System.Math.Sqrt(
            System.Math.Max(0.0, 2.0 * totalKineticEnergy / totalEjectaMass));
        baseSpeed = System.Math.Min(baseSpeed, RelativisticVelocityCap);

        for (int i = 0; i < count; i++)
        {
            // Fibonacci sphere for uniform distribution
            double phi = System.Math.Acos(1.0 - 2.0 * (i + 0.5) / count);
            double theta = System.Math.PI * (1.0 + System.Math.Sqrt(5.0)) * i;

            double sinPhi = System.Math.Sin(phi);
            double dx = sinPhi * System.Math.Cos(theta);
            double dy = sinPhi * System.Math.Sin(theta);
            double dz = System.Math.Cos(phi);

            // Speed variation: (1 - variation/2) to (1 + variation/2)
            double factor = 1.0 - speedVariation * 0.5 + speedVariation * rng.NextDouble();
            double speed = baseSpeed * factor;
            speed = System.Math.Min(speed, RelativisticVelocityCap);

            // Galilean boost: add star velocity
            outSpeedX[i] = starVelocity.X + dx * speed;
            outSpeedY[i] = starVelocity.Y + dy * speed;
            outSpeedZ[i] = starVelocity.Z + dz * speed;
        }
    }
}
