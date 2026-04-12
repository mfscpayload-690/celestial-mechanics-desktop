using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation.Analysis;

public static class EventDetector
{
    private const double CollisionDistanceScale = 2.5;
    private const double EscapeVelocityScale = 1.02;
    private const double OrbitDecayPeriapsisScale = 4.0;
    private const double OrbitDecayInwardSpeedFloor = 1e-4;

    public static string? DetectPrimaryEvent(in PhysicsBody body, in PhysicsBody referenceBody, OrbitData? orbitData)
    {
        if (!body.IsActive || !referenceBody.IsActive || body.Id == referenceBody.Id)
            return null;

        if (IsCollisionImminent(body, referenceBody))
            return "Collision Imminent";

        if (IsEscapeTrajectory(body, referenceBody, orbitData))
            return "Escape Trajectory";

        if (IsOrbitDecay(body, referenceBody, orbitData))
            return "Orbit Decay";

        return null;
    }

    private static bool IsCollisionImminent(in PhysicsBody body, in PhysicsBody referenceBody)
    {
        double distance = (body.Position - referenceBody.Position).Length;
        double collisionThreshold = System.Math.Max((body.Radius + referenceBody.Radius) * CollisionDistanceScale, 1e-6);
        return distance <= collisionThreshold;
    }

    private static bool IsEscapeTrajectory(in PhysicsBody body, in PhysicsBody referenceBody, OrbitData? orbitData)
    {
        if (orbitData?.Type == OrbitType.Hyperbolic)
            return true;

        double distance = (body.Position - referenceBody.Position).Length;
        if (distance <= 1e-12)
            return false;

        double mu = PhysicalConstants.G_Sim * (System.Math.Max(body.Mass, 0.0) + System.Math.Max(referenceBody.Mass, 0.0));
        if (mu <= 1e-12)
            return false;

        double relativeSpeed = (body.Velocity - referenceBody.Velocity).Length;
        double escapeVelocity = System.Math.Sqrt(2.0 * mu / distance);

        return relativeSpeed >= escapeVelocity * EscapeVelocityScale;
    }

    private static bool IsOrbitDecay(in PhysicsBody body, in PhysicsBody referenceBody, OrbitData? orbitData)
    {
        if (orbitData == null)
            return false;

        if (orbitData.Type == OrbitType.Hyperbolic || orbitData.Type == OrbitType.Parabolic)
            return false;

        if (!double.IsFinite(orbitData.Periapsis) || orbitData.Periapsis <= 0.0)
            return false;

        double decayThreshold = System.Math.Max(referenceBody.Radius * OrbitDecayPeriapsisScale, (body.Radius + referenceBody.Radius) * 1.3);
        if (orbitData.Periapsis > decayThreshold)
            return false;

        Vec3d radial = body.Position - referenceBody.Position;
        double distance = radial.Length;
        if (distance <= 1e-12)
            return false;

        Vec3d radialDirection = radial / distance;
        double radialVelocity = Vec3d.Dot(body.Velocity - referenceBody.Velocity, radialDirection);

        return radialVelocity < -OrbitDecayInwardSpeedFloor;
    }
}