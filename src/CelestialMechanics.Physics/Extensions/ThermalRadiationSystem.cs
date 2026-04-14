using CelestialMechanics.Physics.Astrophysics;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Updates body temperature/luminosity using SI radiative transfer.
/// </summary>
public sealed class ThermalRadiationSystem
{
    private const double Sigma = 5.670374419e-8;
    private const double SolarLuminosityW = 3.828e26;

    public double DefaultAbsorptivity { get; set; } = 0.7;
    public bool Enabled { get; set; } = true;

    public void Update(PhysicsBody[] bodies, double dtSim)
    {
        if (!Enabled || bodies.Length == 0 || dtSim <= 0.0)
            return;

        double dtSeconds = Units.TimeToSeconds(dtSim);
        if (dtSeconds <= 0.0)
            return;

        Span<double> sourceLuminosity = bodies.Length <= 512
            ? stackalloc double[bodies.Length]
            : new double[bodies.Length];

        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive)
            {
                sourceLuminosity[i] = 0.0;
                continue;
            }

            sourceLuminosity[i] = ResolveIntrinsicLuminosity(bodies[i]);
        }

        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsActive)
                continue;

            ref var body = ref bodies[i];
            double massKg = Units.MassToKg(System.Math.Max(body.Mass, 1e-15));
            double specificHeat = System.Math.Max(body.HeatCapacity, 1.0);
            double heatCapacityTotal = massKg * specificHeat;
            if (heatCapacityTotal <= 0.0)
                continue;

            double effectiveRadiusM = GetEffectiveThermalRadiusMeters(body);
            double area = 4.0 * System.Math.PI * effectiveRadiusM * effectiveRadiusM;
            double crossSection = System.Math.PI * effectiveRadiusM * effectiveRadiusM;

            double absorbedPower = 0.0;
            double absorptivity = GetAbsorptivity(body.Type);

            for (int j = 0; j < bodies.Length; j++)
            {
                if (j == i || !bodies[j].IsActive)
                    continue;

                double sourceL = sourceLuminosity[j];
                if (sourceL <= 0.0)
                    continue;

                double dx = bodies[j].Position.X - body.Position.X;
                double dy = bodies[j].Position.Y - body.Position.Y;
                double dz = bodies[j].Position.Z - body.Position.Z;
                double distanceM = Units.DistanceToMeters(System.Math.Sqrt(dx * dx + dy * dy + dz * dz));
                if (distanceM <= 1.0)
                    continue;

                double flux = sourceL / (4.0 * System.Math.PI * distanceM * distanceM);
                absorbedPower += flux * crossSection * absorptivity;
            }

            double emittedPower = Sigma * area * System.Math.Pow(System.Math.Max(body.Temperature, 2.7), 4.0);
            double deltaT = (absorbedPower - emittedPower) * dtSeconds / heatCapacityTotal;
            deltaT = System.Math.Clamp(deltaT, -2.0e4, 2.0e4);
            body.Temperature = System.Math.Max(2.7, body.Temperature + deltaT);
            body.Luminosity = emittedPower;
        }
    }

    private static double ResolveIntrinsicLuminosity(in PhysicsBody body)
    {
        if (body.Luminosity > 0.0)
            return body.Luminosity;

        return body.Type switch
        {
            BodyType.Star => SolarLuminosityW * System.Math.Pow(System.Math.Max(body.Mass, 0.08), 3.5),
            BodyType.NeutronStar => 0.2 * SolarLuminosityW * System.Math.Pow(System.Math.Max(body.Temperature, 1.0) / 1.0e6, 4.0),
            BodyType.BlackHole => 0.0,
            _ => Sigma * 4.0 * System.Math.PI * System.Math.Pow(GetEffectiveThermalRadiusMeters(body), 2.0) * System.Math.Pow(System.Math.Max(body.Temperature, 2.7), 4.0)
        };
    }

    private static double GetEffectiveThermalRadiusMeters(in PhysicsBody body)
    {
        return body.Type switch
        {
            BodyType.Star => 6.957e8 * System.Math.Pow(System.Math.Max(body.Mass, 0.08), 0.8),
            BodyType.NeutronStar => 1.2e4,
            BodyType.BlackHole => System.Math.Max(1.0, Units.DistanceToMeters(System.Math.Max(body.Radius, 1e-12))),
            _ => System.Math.Max(1.0, Units.DistanceToMeters(System.Math.Max(body.Radius, 1e-12)))
        };
    }

    private double GetAbsorptivity(BodyType type)
    {
        return type switch
        {
            BodyType.BlackHole => 1.0,
            BodyType.Comet => 0.55,
            BodyType.GasGiant => 0.75,
            _ => DefaultAbsorptivity
        };
    }
}