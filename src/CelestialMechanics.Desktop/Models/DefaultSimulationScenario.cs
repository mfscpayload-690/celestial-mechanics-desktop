using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Models;

public static class DefaultSimulationScenario
{
    public static PhysicsBody[] CreateTwoBodyOrbit()
    {
        double mass = 1.0;
        double separation = 2.0;
        double r = separation / 2.0;
        double v = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / (4.0 * r));

        return new[]
        {
            new PhysicsBody(0, mass, new Vec3d(r, 0, 0), new Vec3d(0, 0, v), BodyType.Star)
            {
                Radius = 0.05,
                GravityStrength = 60,
                GravityRange = 8,
                IsActive = true,
                IsCollidable = true,
            },
            new PhysicsBody(1, mass, new Vec3d(-r, 0, 0), new Vec3d(0, 0, -v), BodyType.Star)
            {
                Radius = 0.05,
                GravityStrength = 60,
                GravityRange = 8,
                IsActive = true,
                IsCollidable = true,
            }
        };
    }
}
