using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation;

public static class OrbitCalculator
{
    public static OrbitType ClassifyOrbitType(double eccentricity)
    {
        if (eccentricity < 0.01) return OrbitType.Circular;
        else if (eccentricity < 1.0) return OrbitType.Elliptical;
        else if (System.Math.Abs(eccentricity - 1.0) < 0.05) return OrbitType.Parabolic;
        else if (eccentricity > 1.0) return OrbitType.Hyperbolic;
        else return OrbitType.Chaotic;
    }

    public static double CalculateCircularOrbitVelocity(double centralMass, double radius)
    {
        if (centralMass <= 0.0 || radius <= 1e-12)
            return 0.0;

        return System.Math.Sqrt(PhysicalConstants.G_Sim * centralMass / radius);
    }

    public static Vec3d CalculateOrbitalVelocityVector(
        in Vec3d centralPosition,
        in Vec3d orbitingPosition,
        in Vec3d centralVelocity,
        double centralMass,
        bool elliptical,
        double eccentricity = 0.0)
    {
        var radial = orbitingPosition - centralPosition;
        double radius = radial.Length;
        if (radius <= 1e-12 || centralMass <= 0.0)
            return centralVelocity;

        var radialDir = radial / radius;

        // Use an orbital plane with +Y as up; if near-degenerate, fall back to +X.
        var tangent = Vec3d.Cross(Vec3d.UnitY, radialDir);
        if (tangent.LengthSquared < 1e-10)
            tangent = Vec3d.Cross(Vec3d.UnitX, radialDir);

        tangent = tangent.Normalized();

        double speed;
        if (!elliptical)
        {
            speed = CalculateCircularOrbitVelocity(centralMass, radius);
        }
        else
        {
            double e = System.Math.Clamp(eccentricity, 0.0, 0.95);
            // Assume input position is near periapsis for simple ellipse initialization.
            double semiMajor = radius / System.Math.Max(1e-8, 1.0 - e);
            double mu = PhysicalConstants.G_Sim * centralMass;
            speed = System.Math.Sqrt(System.Math.Max(0.0, mu * (2.0 / radius - 1.0 / semiMajor)));
        }

        return centralVelocity + tangent * speed;
    }

    public static OrbitData ComputeOrbit(in PhysicsBody body, in PhysicsBody central)
    {
        var result = new OrbitData
        {
            Apoapsis = double.PositiveInfinity,
            Periapsis = double.PositiveInfinity,
            Eccentricity = 0.0,
            SemiMajorAxis = double.PositiveInfinity,
            Period = double.PositiveInfinity,
            Type = OrbitType.Chaotic,
        };

        var rVec = body.Position - central.Position;
        var vVec = body.Velocity - central.Velocity;
        double r = rVec.Length;

        if (r <= 1e-12)
            return result;

        double mu = PhysicalConstants.G_Sim * (System.Math.Max(body.Mass, 0.0) + System.Math.Max(central.Mass, 0.0));
        if (mu <= 1e-18)
            return result;

        double v2 = vVec.LengthSquared;
        double specificEnergy = 0.5 * v2 - (mu / r);

        var hVec = Vec3d.Cross(rVec, vVec);
        var eVec = (Vec3d.Cross(vVec, hVec) / mu) - (rVec / r);
        double e = System.Math.Max(0.0, eVec.Length);
        result.Eccentricity = e;
        result.Type = ClassifyOrbitType(e);

        if (System.Math.Abs(specificEnergy) > 1e-15)
            result.SemiMajorAxis = -mu / (2.0 * specificEnergy);

        if (double.IsFinite(result.SemiMajorAxis) && result.SemiMajorAxis > 0.0)
        {
            result.Periapsis = result.SemiMajorAxis * (1.0 - e);
            result.Apoapsis = e < 1.0
                ? result.SemiMajorAxis * (1.0 + e)
                : double.PositiveInfinity;

            if (e < 1.0)
            {
                result.Period = 2.0 * System.Math.PI * System.Math.Sqrt(
                    (result.SemiMajorAxis * result.SemiMajorAxis * result.SemiMajorAxis) / mu);
            }
        }
        else
        {
            double rp = hVec.LengthSquared / (mu * (1.0 + e));
            result.Periapsis = rp > 0.0 ? rp : double.PositiveInfinity;
            result.Apoapsis = double.PositiveInfinity;
            result.Period = double.PositiveInfinity;
        }

        return result;
    }
}
