using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation.Placement;

public static class PlacementMath
{
    public static Vec3d MapVelocityFromDrag(
        in Vec3d anchor,
        in Vec3d dragTarget,
        double speedScale,
        double minSpeed,
        double maxSpeed,
        out Vec3d direction,
        out double speed)
    {
        var drag = dragTarget - anchor;
        var dragLength = drag.Length;

        if (dragLength < 1e-12)
        {
            direction = Vec3d.Zero;
            speed = minSpeed;
            return Vec3d.Zero;
        }

        direction = drag / dragLength;
        speed = System.Math.Clamp(dragLength * speedScale, minSpeed, maxSpeed);
        return direction * speed;
    }

    public static List<Vec3d> BuildGravityAwarePreview(
        in Vec3d startPosition,
        in Vec3d startVelocity,
        IReadOnlyList<PhysicsBody> bodies,
        int steps,
        double dt,
        double softening = 1e-4)
    {
        var result = new List<Vec3d>(System.Math.Max(steps, 0) + 1) { startPosition };
        if (steps <= 0 || dt <= 0.0)
            return result;

        var p = startPosition;
        var v = startVelocity;

        for (int i = 0; i < steps; i++)
        {
            Vec3d acc = Vec3d.Zero;

            for (int b = 0; b < bodies.Count; b++)
            {
                var body = bodies[b];
                if (!body.IsActive)
                    continue;

                var delta = body.Position - p;
                var r2 = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z + softening * softening;
                var invR = 1.0 / System.Math.Sqrt(r2);
                var invR3 = invR * invR * invR;
                var strength = PhysicalConstants.G_Sim * body.Mass * invR3;
                acc += delta * strength;
            }

            // Semi-implicit Euler for stable lightweight preview integration.
            v += acc * dt;
            p += v * dt;
            result.Add(p);
        }

        return result;
    }
}
