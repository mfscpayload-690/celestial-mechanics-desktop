using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Integrators;

/// <summary>
/// Velocity Verlet integrator. Second-order, symplectic.
/// Default integrator for the simulation. Exactly solves a nearby Hamiltonian
/// H_tilde = H + O(dt^2), guaranteeing bounded energy error that oscillates
/// but never drifts secularly. Phase space volume is exactly preserved.
/// </summary>
public class VerletIntegrator : IIntegrator
{
    public string Name => "Verlet";

    public void Step(PhysicsBody[] bodies, double dt, IForceCalculator[] forces)
    {
        int n = bodies.Length;
        double halfDt = 0.5 * dt;

        // Phase 1: Half-kick velocities + full-drift positions
        for (int i = 0; i < n; i++)
        {
            if (!bodies[i].IsActive) continue;

            bodies[i].Velocity += bodies[i].Acceleration * halfDt;
            bodies[i].Position += bodies[i].Velocity * dt;
        }

        // Phase 2: Recompute accelerations at new positions (pairwise, symmetric)
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

        // Phase 3: Second half-kick
        for (int i = 0; i < n; i++)
        {
            if (!bodies[i].IsActive) continue;

            bodies[i].Velocity += bodies[i].Acceleration * halfDt;
        }
    }
}
