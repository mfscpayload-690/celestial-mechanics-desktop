using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Solvers;

namespace CelestialMechanics.Physics.Integrators;

/// <summary>
/// Integrator that operates directly on a <see cref="BodySoA"/> buffer.
///
/// Unlike <see cref="IIntegrator"/>, which works on the legacy AoS
/// <c>PhysicsBody[]</c> array, this interface accepts a pre-allocated SoA
/// buffer and an <see cref="IPhysicsComputeBackend"/> for the O(n²) force
/// pass. This separation allows:
///
///   1. Swapping compute backends (single-thread, parallel, CUDA) without
///      touching integration logic.
///   2. Clean extension to new integrators (Leapfrog, Yoshida 4th-order)
///      operating entirely on cache-friendly contiguous arrays.
///   3. A clear boundary for future GPU uploads: Step() uploads positions,
///      the backend computes forces on the device, Step() downloads and
///      integrates velocities.
/// </summary>
public interface ISoAIntegrator
{
    /// <summary>Display name shown in diagnostics / UI.</summary>
    string Name { get; }

    /// <summary>
    /// Advance <paramref name="bodies"/> by one timestep <paramref name="dt"/>.
    ///
    /// Contract:
    ///   • On entry  : PosX/Y/Z, VelX/Y/Z, OldAccX/Y/Z hold state at time t.
    ///   • On exit   : PosX/Y/Z, VelX/Y/Z, AccX/Y/Z hold state at time t+dt.
    ///                 OldAccX/Y/Z are updated ready for the next call.
    ///   • No heap allocations are permitted inside this method.
    /// </summary>
    void Step(BodySoA bodies, IPhysicsComputeBackend backend, double softening, double dt);
}
