using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Integrators;

/// <summary>
/// 4th-order Runge-Kutta integrator. O(dt^5) local error, O(dt^4) global error.
/// Not symplectic -- exhibits secular energy drift over long simulations.
/// Use for high-accuracy short-term trajectory prediction or comparison studies.
/// </summary>
public class RK4Integrator : IIntegrator
{
    public string Name => "RK4";

    public void Step(PhysicsBody[] bodies, double dt, IForceCalculator[] forces)
    {
        int n = bodies.Length;
        double halfDt = 0.5 * dt;

        // Store original state
        var origPos = new Vec3d[n];
        var origVel = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            origPos[i] = bodies[i].Position;
            origVel[i] = bodies[i].Velocity;
        }

        // Temporary arrays for k-stages
        var k1Vel = new Vec3d[n];
        var k1Acc = new Vec3d[n];
        var k2Vel = new Vec3d[n];
        var k2Acc = new Vec3d[n];
        var k3Vel = new Vec3d[n];
        var k3Acc = new Vec3d[n];
        var k4Vel = new Vec3d[n];
        var k4Acc = new Vec3d[n];

        // --- k1: evaluate at t, original state ---
        ComputeAccelerations(bodies, forces, k1Acc);
        for (int i = 0; i < n; i++)
        {
            k1Vel[i] = bodies[i].Velocity;
        }

        // --- k2: evaluate at t + dt/2, state + k1*dt/2 ---
        for (int i = 0; i < n; i++)
        {
            bodies[i].Position = origPos[i] + k1Vel[i] * halfDt;
            bodies[i].Velocity = origVel[i] + k1Acc[i] * halfDt;
        }
        ComputeAccelerations(bodies, forces, k2Acc);
        for (int i = 0; i < n; i++)
        {
            k2Vel[i] = bodies[i].Velocity;
        }

        // --- k3: evaluate at t + dt/2, state + k2*dt/2 ---
        for (int i = 0; i < n; i++)
        {
            bodies[i].Position = origPos[i] + k2Vel[i] * halfDt;
            bodies[i].Velocity = origVel[i] + k2Acc[i] * halfDt;
        }
        ComputeAccelerations(bodies, forces, k3Acc);
        for (int i = 0; i < n; i++)
        {
            k3Vel[i] = bodies[i].Velocity;
        }

        // --- k4: evaluate at t + dt, state + k3*dt ---
        for (int i = 0; i < n; i++)
        {
            bodies[i].Position = origPos[i] + k3Vel[i] * dt;
            bodies[i].Velocity = origVel[i] + k3Acc[i] * dt;
        }
        ComputeAccelerations(bodies, forces, k4Acc);
        for (int i = 0; i < n; i++)
        {
            k4Vel[i] = bodies[i].Velocity;
        }

        // --- Combine: weighted average of k1..k4 ---
        for (int i = 0; i < n; i++)
        {
            bodies[i].Position = origPos[i] + (k1Vel[i] + k2Vel[i] * 2.0 + k3Vel[i] * 2.0 + k4Vel[i]) * (dt / 6.0);
            bodies[i].Velocity = origVel[i] + (k1Acc[i] + k2Acc[i] * 2.0 + k3Acc[i] * 2.0 + k4Acc[i]) * (dt / 6.0);
        }

        // Recompute final accelerations at the new state for consistency
        ComputeAccelerations(bodies, forces, k1Acc);
        for (int i = 0; i < n; i++)
        {
            bodies[i].Acceleration = k1Acc[i];
        }
    }

    /// <summary>
    /// Compute pairwise accelerations for all active bodies. Uses Newton's 3rd law symmetry.
    /// </summary>
    private static void ComputeAccelerations(PhysicsBody[] bodies, IForceCalculator[] forces, Vec3d[] accelerations)
    {
        int n = bodies.Length;

        for (int i = 0; i < n; i++)
        {
            accelerations[i] = Vec3d.Zero;
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
                accelerations[i] += totalForce / bodies[i].Mass;
                accelerations[j] -= totalForce / bodies[j].Mass;
            }
        }
    }
}
