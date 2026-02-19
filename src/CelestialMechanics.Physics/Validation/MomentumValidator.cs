using CelestialMechanics.Math;

using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Validation;

/// <summary>
/// Tracks linear momentum conservation by comparing current total momentum
/// against the initial total momentum. Drift indicates numerical error accumulation.
/// </summary>
public class MomentumValidator
{
    private Vec3d _initialMomentum;
    private bool _initialized;

    /// <summary>
    /// The initial total momentum, set on the first call to ComputeDrift.
    /// </summary>
    public Vec3d InitialMomentum => _initialMomentum;

    /// <summary>
    /// Sets the initial momentum reference point. Called once when the simulation starts.
    /// </summary>
    public void SetInitialMomentum(Vec3d momentum)
    {
        _initialMomentum = momentum;
        _initialized = true;
    }

    /// <summary>
    /// Compute the momentum drift: |current_momentum - initial_momentum|.
    /// If initial momentum has not been set, it is captured from the current bodies.
    /// </summary>
    public double ComputeDrift(PhysicsBody[] bodies)
    {
        Vec3d currentMomentum = Vec3d.Zero;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive) continue;
            currentMomentum += bodies[i].Velocity * bodies[i].Mass;
        }

        if (!_initialized)
        {
            _initialMomentum = currentMomentum;
            _initialized = true;
            return 0.0;
        }

        Vec3d diff = currentMomentum - _initialMomentum;
        return diff.Length;
    }
}
