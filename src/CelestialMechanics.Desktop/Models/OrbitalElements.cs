using CelestialMechanics.Math;

namespace CelestialMechanics.Desktop.Models;

public sealed class OrbitalElements
{
    public bool IsValid { get; init; }
    public string OrbitType { get; init; } = string.Empty;
    public double SemiMajorAxis { get; init; }
    public double Eccentricity { get; init; }
    public double Inclination { get; init; }
    public double LongitudeOfAscendingNode { get; init; }
    public double ArgumentOfPeriapsis { get; init; }
    public double TrueAnomaly { get; init; }
    public double Period { get; init; }
    public double PeriapsisDistance { get; init; }
    public double ApoapsisDistance { get; init; }
    public double SpecificOrbitalEnergy { get; init; }

    public static OrbitalElements FromStateVectors(Vec3d position, Vec3d velocity, double mu)
    {
        const double epsilon = 1e-12;

        double r = position.Length;
        double v = velocity.Length;
        if (r <= epsilon || mu <= epsilon)
        {
            return Invalid();
        }

        Vec3d h = position.Cross(velocity);
        double hMag = h.Length;
        if (hMag <= epsilon)
        {
            return Invalid();
        }

        Vec3d k = Vec3d.UnitZ;
        Vec3d n = k.Cross(h);
        double nMag = n.Length;

        Vec3d eVec = (velocity.Cross(h) / mu) - (position / r);
        double e = eVec.Length;

        double energy = 0.5 * v * v - mu / r;

        double a = double.PositiveInfinity;
        if (System.Math.Abs(energy) > epsilon)
        {
            a = -mu / (2.0 * energy);
        }

        double inclination = RadToDeg(System.Math.Acos(ClampCos(h.Z / hMag)));

        double lan = 0.0;
        if (nMag > epsilon)
        {
            lan = RadToDeg(System.Math.Acos(ClampCos(n.X / nMag)));
            if (n.Y < 0)
            {
                lan = 360.0 - lan;
            }
        }

        double argPeriapsis = 0.0;
        if (nMag > epsilon && e > epsilon)
        {
            argPeriapsis = RadToDeg(System.Math.Acos(ClampCos(n.Dot(eVec) / (nMag * e))));
            if (eVec.Z < 0)
            {
                argPeriapsis = 360.0 - argPeriapsis;
            }
        }

        double trueAnomaly = 0.0;
        if (e > epsilon)
        {
            trueAnomaly = RadToDeg(System.Math.Acos(ClampCos(eVec.Dot(position) / (e * r))));
            if (position.Dot(velocity) < 0)
            {
                trueAnomaly = 360.0 - trueAnomaly;
            }
        }

        string orbitType = e switch
        {
            < 1e-4 => "Circular",
            < 1.0 => "Elliptic",
            <= 1.0001 => "Parabolic",
            _ => "Hyperbolic",
        };

        double periapsis = e < 1.0 ? a * (1.0 - e) : hMag * hMag / (mu * (1.0 + e));
        double apoapsis = e < 1.0 ? a * (1.0 + e) : double.PositiveInfinity;

        double period = double.PositiveInfinity;
        if (e < 1.0 && a > epsilon)
        {
            period = 2.0 * System.Math.PI * System.Math.Sqrt((a * a * a) / mu);
        }

        return new OrbitalElements
        {
            IsValid = true,
            OrbitType = orbitType,
            SemiMajorAxis = a,
            Eccentricity = e,
            Inclination = inclination,
            LongitudeOfAscendingNode = lan,
            ArgumentOfPeriapsis = argPeriapsis,
            TrueAnomaly = trueAnomaly,
            Period = period,
            PeriapsisDistance = periapsis,
            ApoapsisDistance = apoapsis,
            SpecificOrbitalEnergy = energy,
        };
    }

    private static OrbitalElements Invalid() => new() { IsValid = false };

    private static double ClampCos(double value) => System.Math.Clamp(value, -1.0, 1.0);

    private static double RadToDeg(double radians) => radians * (180.0 / System.Math.PI);
}
