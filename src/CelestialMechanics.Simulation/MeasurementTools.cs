using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation;

public static class MeasurementTools
{
    public static double Distance(in PhysicsBody a, in PhysicsBody b)
    {
        return (a.Position - b.Position).Length;
    }

    public static double RelativeSpeed(in PhysicsBody a, in PhysicsBody b)
    {
        return (a.Velocity - b.Velocity).Length;
    }

    public static double EstimateOrbitalPeriod(double radius, double centralMass, double orbitingMass = 0.0)
    {
        if (radius <= 1e-12 || centralMass <= 0.0)
            return double.PositiveInfinity;

        double mu = PhysicalConstants.G_Sim * (centralMass + System.Math.Max(orbitingMass, 0.0));
        return 2.0 * System.Math.PI * System.Math.Sqrt((radius * radius * radius) / mu);
    }
}
