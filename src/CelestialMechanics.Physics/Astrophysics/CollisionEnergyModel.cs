namespace CelestialMechanics.Physics.Astrophysics;

public readonly record struct CollisionEnergyResult(
    double RelativeSpeedMps,
    double ReducedMassKg,
    double CollisionEnergyJ,
    double BindingEnergyJ,
    double EjectaMassKg,
    double ExpansionVelocityMps,
    double EnergyRatio,
    bool IsMerge,
    bool IsFragmentation,
    bool IsCatastrophic);

/// <summary>
/// Energy-based collision classifier in SI units.
/// </summary>
public static class CollisionEnergyModel
{
    public static CollisionEnergyResult Evaluate(
        double m1Solar,
        double m2Solar,
        double effectiveRadiusAu,
        double relativeSpeedSim,
        double maxMassLossFraction)
    {
        double m1Kg = Units.MassToKg(System.Math.Max(m1Solar, 1e-15));
        double m2Kg = Units.MassToKg(System.Math.Max(m2Solar, 1e-15));
        double totalMassKg = m1Kg + m2Kg;
        double radiusMeters = Units.DistanceToMeters(System.Math.Max(effectiveRadiusAu, 1e-12));
        double relativeSpeedMps = Units.VelocityToMetersPerSecond(System.Math.Max(relativeSpeedSim, 0.0));

        double reducedMass = (m1Kg * m2Kg) / System.Math.Max(totalMassKg, 1e-15);
        double collisionEnergy = 0.5 * reducedMass * relativeSpeedMps * relativeSpeedMps;
        double bindingEnergy = (3.0 / 5.0) * Units.G * totalMassKg * totalMassKg / System.Math.Max(radiusMeters, 1.0);
        double ratio = bindingEnergy > 0.0 ? collisionEnergy / bindingEnergy : 0.0;

        bool merge = collisionEnergy < 0.5 * bindingEnergy;
        bool fragmentation = !merge && collisionEnergy < bindingEnergy;
        bool catastrophic = !merge && !fragmentation;

        double ejectaFraction = 0.0;
        if (fragmentation)
        {
            double normalized = System.Math.Clamp((ratio - 0.5) / 0.5, 0.0, 1.0);
            ejectaFraction = System.Math.Clamp(0.08 + normalized * 0.32, 0.05, maxMassLossFraction);
        }
        else if (catastrophic)
        {
            double normalized = System.Math.Clamp(ratio - 1.0, 0.0, 12.0) / 12.0;
            double catastrophicCap = System.Math.Max(maxMassLossFraction, 0.55);
            ejectaFraction = System.Math.Clamp(0.5 + normalized * 0.4, 0.45, catastrophicCap);
        }

        double ejectaMassKg = totalMassKg * ejectaFraction;
        double expansionVelocity = 0.0;
        if (ejectaMassKg > 0.0 && collisionEnergy > 0.0)
            expansionVelocity = System.Math.Sqrt(2.0 * collisionEnergy / ejectaMassKg);

        return new CollisionEnergyResult(
            RelativeSpeedMps: relativeSpeedMps,
            ReducedMassKg: reducedMass,
            CollisionEnergyJ: collisionEnergy,
            BindingEnergyJ: bindingEnergy,
            EjectaMassKg: ejectaMassKg,
            ExpansionVelocityMps: expansionVelocity,
            EnergyRatio: ratio,
            IsMerge: merge,
            IsFragmentation: fragmentation,
            IsCatastrophic: catastrophic);
    }
}