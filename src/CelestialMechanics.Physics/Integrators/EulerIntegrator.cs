using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Integrators;

/// <summary>
/// Forward Euler integrator. First-order, non-symplectic.
/// Included strictly for educational comparison. Orbits spiral outward
/// within ~100 steps due to O(dt) energy drift per step.
/// </summary>
public class EulerIntegrator : IIntegrator
{
    public string Name => "Euler";

    public void Step(PhysicsBody[] bodies, double dt, IForceCalculator[] forces)
    {
        int n = bodies.Length;

        // Compute accelerations from pairwise forces (symmetric: i < j)
        for (int i = 0; i < n; i++)
        {
            bodies[i].Acceleration = Vec3d.Zero;
        }

        for (int i = 0; i < n; i++)
        {
            if (!bodies[i].IsActive) continue;

            for (int j = i + 1; j < n; j++)
            {
                if (!bodies[j].IsActive) continue;

                Vec3d totalForce = Vec3d.Zero;
                for (int f = 0; f < forces.Length; f++)
                {
                    totalForce += forces[f].ComputeForce(in bodies[i], in bodies[j]);
                }

                // Newton's 3rd law: +F on i, -F on j
                bodies[i].Acceleration += totalForce / bodies[i].Mass;
                bodies[j].Acceleration -= totalForce / bodies[j].Mass;
            }
        }

        // Forward Euler: update position with OLD velocity, then update velocity
        // This is explicitly non-symplectic to demonstrate energy drift
        for (int i = 0; i < n; i++)
        {
            if (!bodies[i].IsActive) continue;

            bodies[i].Position += bodies[i].Velocity * dt;
            bodies[i].Velocity += bodies[i].Acceleration * dt;
        }
    }
}
