namespace CelestialMechanics.Simulation.Analysis;

public static class StabilityClassifier
{
    private const double StableThreshold = 1e-5;
    private const double MarginalThreshold = 1e-3;

    public static string Classify(double energyDrift)
    {
        double drift = System.Math.Abs(energyDrift);
        if (double.IsNaN(drift) || double.IsInfinity(drift))
            return "Unstable";

        if (drift < StableThreshold)
            return "Stable";
        if (drift < MarginalThreshold)
            return "Marginal";
        return "Unstable";
    }

    public static string Classify(double energyDrift, double momentumDrift)
    {
        double combined = System.Math.Max(System.Math.Abs(energyDrift), System.Math.Abs(momentumDrift));
        return Classify(combined);
    }
}