using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Abstraction for physics computation backend.
/// Phase 1: CPU implementation using standard loops.
/// Phase 4: GPU implementation using CUDA.
/// </summary>
public interface IPhysicsCompute
{
    void UploadBodies(PhysicsBody[] bodies);
    void ComputeForces(double dt);
    void DownloadResults(PhysicsBody[] bodies);
}
