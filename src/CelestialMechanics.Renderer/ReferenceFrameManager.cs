using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Renderer;

public enum ReferenceFrameKind
{
    Inertial = 0,
    CenterOfMass = 1,
    BodyRelative = 2,
}

public sealed class ReferenceFrameManager
{
    public ReferenceFrameKind ActiveFrame { get; set; } = ReferenceFrameKind.Inertial;
    public int RelativeBodyId { get; set; } = -1;

    public Vec3d ComputeOrigin(IReadOnlyList<PhysicsBody> bodies)
    {
        return ActiveFrame switch
        {
            ReferenceFrameKind.CenterOfMass => ComputeCenterOfMass(bodies),
            ReferenceFrameKind.BodyRelative => FindBodyOrigin(bodies, RelativeBodyId),
            _ => Vec3d.Zero
        };
    }

    private static Vec3d ComputeCenterOfMass(IReadOnlyList<PhysicsBody> bodies)
    {
        double totalMass = 0.0;
        Vec3d weighted = Vec3d.Zero;

        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            if (!body.IsActive || body.Mass <= 0.0)
                continue;

            totalMass += body.Mass;
            weighted += body.Position * body.Mass;
        }

        if (totalMass <= 1e-12)
            return Vec3d.Zero;

        return weighted / totalMass;
    }

    private static Vec3d FindBodyOrigin(IReadOnlyList<PhysicsBody> bodies, int bodyId)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            if (body.IsActive && body.Id == bodyId)
                return body.Position;
        }

        return Vec3d.Zero;
    }
}
