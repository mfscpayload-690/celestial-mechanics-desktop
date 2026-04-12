using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation.Placement;

public static class TrajectoryPredictor
{
    public static List<Vec3d> PredictBodyTrajectory(
        IReadOnlyList<PhysicsBody> sourceBodies,
        int trackedBodyId,
        PhysicsConfig config,
        int steps,
        double dt)
    {
        var path = new List<Vec3d>(System.Math.Max(steps, 0) + 1);
        if (sourceBodies.Count == 0 || steps <= 0 || dt <= 0.0)
            return path;

        var bodies = new PhysicsBody[sourceBodies.Count];
        for (int i = 0; i < sourceBodies.Count; i++)
            bodies[i] = sourceBodies[i];

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity
        {
            SofteningEpsilon = config.SofteningEpsilon,
            RangeScale = config.GravityRangeScale,
            EnableShellTheorem = config.EnableShellTheorem
        });

        IIntegrator integrator = config.IntegratorName switch
        {
            "Euler" => new EulerIntegrator(),
            "RK4" => new RK4Integrator(),
            _ => new VerletIntegrator()
        };

        solver.SetIntegrator(integrator);

        bool soaCapable = integrator is VerletIntegrator;
        solver.ConfigureSoA(
            enabled: soaCapable && config.UseSoAPath,
            softening: config.SofteningEpsilon,
            deterministic: config.DeterministicMode,
            useParallel: config.UseParallelComputation,
            useBarnesHut: config.UseBarnesHut,
            theta: config.Theta,
            enableCollisions: false,
            useSimd: config.UseSimd,
            enableShellTheorem: config.EnableShellTheorem,
            collisionMode: config.CollisionMode,
            collisionRestitution: config.CollisionRestitution,
            fragmentationSpecificEnergyThreshold: config.FragmentationSpecificEnergyThreshold,
            fragmentationMassLossCap: config.FragmentationMassLossCap,
            captureVelocityFactor: config.CaptureVelocityFactor,
            enableCollisionBroadPhase: config.EnableCollisionBroadPhase,
            collisionBroadPhaseThreshold: config.CollisionBroadPhaseThreshold,
            enablePostNewtonian: config.EnablePostNewtonian,
            enableAccretionDisks: false,
            enableGravitationalWaves: false,
            maxAccretionParticles: config.MaxAccretionParticles,
            enableJets: false,
            jetThreshold: config.AccretionJetThreshold,
            gwObserverDistance: config.GravitationalWaveObserverDistance);

        int trackedIndex = FindTrackedIndex(bodies, trackedBodyId);
        if (trackedIndex < 0)
            return path;

        path.Add(bodies[trackedIndex].Position);

        for (int i = 0; i < steps; i++)
        {
            solver.Step(bodies, dt);
            trackedIndex = FindTrackedIndex(bodies, trackedBodyId);
            if (trackedIndex < 0 || !bodies[trackedIndex].IsActive)
                break;

            path.Add(bodies[trackedIndex].Position);
        }

        return path;
    }

    private static int FindTrackedIndex(IReadOnlyList<PhysicsBody> bodies, int trackedBodyId)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Id == trackedBodyId)
                return i;
        }

        return -1;
    }
}
