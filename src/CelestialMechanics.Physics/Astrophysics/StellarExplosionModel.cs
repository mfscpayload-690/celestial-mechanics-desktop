namespace CelestialMechanics.Physics.Astrophysics;

public readonly record struct StellarExplosionProfile(
    double BindingEnergyJ,
    double ExplosionEnergyJ,
    double ExpansionVelocityMps,
    double EjectaMassKg,
    double PeakLuminosityW,
    double DecayTauSeconds)
{
    public double RadiusMeters(double timeSeconds) => System.Math.Max(0.0, ExpansionVelocityMps * timeSeconds);

    public double LuminosityAt(double timeSeconds)
    {
        if (DecayTauSeconds <= 0.0)
            return PeakLuminosityW;

        return PeakLuminosityW * System.Math.Exp(-timeSeconds / DecayTauSeconds);
    }
}

/// <summary>
/// SI-based explosion energetics:
/// E_bind = (3/5) G M^2 / R
/// E_explosion = k E_bind
/// v = sqrt(2 E_explosion / M_ejecta)
/// </summary>
public static class StellarExplosionModel
{
    public static StellarExplosionProfile Compute(
        double progenitorMassSolar,
        double progenitorRadiusAu,
        double ejectaMassSolar,
        double k,
        double decayTauSeconds)
    {
        double massKg = Units.MassToKg(System.Math.Max(progenitorMassSolar, 1e-12));
        double radiusM = Units.DistanceToMeters(System.Math.Max(progenitorRadiusAu, 1e-12));
        double ejectaKg = Units.MassToKg(System.Math.Max(ejectaMassSolar, 1e-12));

        double bindingEnergy = (3.0 / 5.0) * Units.G * massKg * massKg / System.Math.Max(radiusM, 1.0);
        double factor = System.Math.Clamp(k, 1.0, 10.0);
        double explosionEnergy = factor * bindingEnergy;
        double velocity = System.Math.Sqrt(2.0 * explosionEnergy / System.Math.Max(ejectaKg, 1.0));

        double tau = System.Math.Max(decayTauSeconds, 1.0);
        double peakLuminosity = explosionEnergy / tau;

        return new StellarExplosionProfile(
            BindingEnergyJ: bindingEnergy,
            ExplosionEnergyJ: explosionEnergy,
            ExpansionVelocityMps: velocity,
            EjectaMassKg: ejectaKg,
            PeakLuminosityW: peakLuminosity,
            DecayTauSeconds: tau);
    }
}