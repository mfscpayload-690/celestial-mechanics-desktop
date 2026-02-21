using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Decorator backend that appends 1PN relativistic corrections after
/// the inner backend computes Newtonian gravitational accelerations.
///
/// This pattern preserves the Velocity Verlet integrator's symplectic
/// structure: the integrator calls <c>ComputeForces()</c> and receives
/// the full corrected accelerations (Newtonian + 1PN) in a single call.
///
/// No interface changes are needed — the <see cref="SoAVerletIntegrator"/>
/// sees the corrected forces transparently.
/// </summary>
public sealed class PostNewtonianBackend : IPhysicsComputeBackend
{
    private readonly IPhysicsComputeBackend _inner;
    private readonly PostNewtonian1Correction _correction;

    /// <summary>
    /// Create a decorator that wraps <paramref name="inner"/> and appends
    /// 1PN corrections via <paramref name="correction"/>.
    /// </summary>
    public PostNewtonianBackend(IPhysicsComputeBackend inner, PostNewtonian1Correction correction)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _correction = correction ?? throw new ArgumentNullException(nameof(correction));
    }

    /// <inheritdoc/>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        // 1. Compute Newtonian forces via the inner backend
        _inner.ComputeForces(bodies, softening);

        // 2. Apply 1PN corrections to the acceleration arrays
        //    The correction reads positions, velocities, and masses,
        //    then ADDS to AccX/AccY/AccZ (does not overwrite).
        _correction.SofteningEpsilon = softening;
        _correction.ApplyCorrections(bodies);
    }
}
