using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Validation;

/// <summary>
/// Computes kinetic energy, potential energy, linear momentum, and angular momentum
/// for a set of physics bodies. Used for simulation validation and diagnostics.
/// </summary>
public class EnergyCalculator
{
    /// <summary>
    /// Compute total kinetic energy: sum of 0.5 * m * v^2 for all active bodies.
    /// </summary>
    public double ComputeKE(PhysicsBody[] bodies)
    {
        double ke = 0.0;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive) continue;
            ke += 0.5 * bodies[i].Mass * bodies[i].Velocity.LengthSquared;
        }
        return ke;
    }

    /// <summary>
    /// Compute total potential energy: sum of pairwise PE for all active body pairs.
    /// Uses the same i &lt; j iteration pattern to avoid double-counting.
    /// </summary>
    public double ComputePE(PhysicsBody[] bodies, IList<IForceCalculator> forces)
    {
        double pe = 0.0;
        int n = bodies.Length;

        for (int i = 0; i < n; i++)
        {
            if (!bodies[i].IsActive) continue;

            for (int j = i + 1; j < n; j++)
            {
                if (!bodies[j].IsActive) continue;

                for (int f = 0; f < forces.Count; f++)
                {
                    pe += forces[f].ComputePotentialEnergy(in bodies[i], in bodies[j]);
                }
            }
        }

        return pe;
    }

    /// <summary>
    /// Compute total linear momentum: sum of m * v for all active bodies.
    /// </summary>
    public Vec3d ComputeMomentum(PhysicsBody[] bodies)
    {
        Vec3d momentum = Vec3d.Zero;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive) continue;
            momentum += bodies[i].Velocity * bodies[i].Mass;
        }
        return momentum;
    }

    /// <summary>
    /// Compute total angular momentum magnitude: |sum of r x (m*v)| for all active bodies.
    /// </summary>
    public double ComputeAngularMomentum(PhysicsBody[] bodies)
    {
        Vec3d angularMomentum = Vec3d.Zero;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive) continue;
            Vec3d mv = bodies[i].Velocity * bodies[i].Mass;
            angularMomentum += Vec3d.Cross(bodies[i].Position, mv);
        }
        return angularMomentum.Length;
    }
}
