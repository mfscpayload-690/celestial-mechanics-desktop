using CelestialMechanics.Simulation.Analysis;

namespace CelestialMechanics.Simulation.Tests;

public class StabilityClassifierTests
{
    [Fact]
    public void Classify_ReturnsStable_ForVeryLowDrift()
    {
        Assert.Equal("Stable", StabilityClassifier.Classify(5e-6));
    }

    [Fact]
    public void Classify_ReturnsMarginal_ForIntermediateDrift()
    {
        Assert.Equal("Marginal", StabilityClassifier.Classify(5e-4));
    }

    [Fact]
    public void Classify_ReturnsUnstable_ForHighDrift()
    {
        Assert.Equal("Unstable", StabilityClassifier.Classify(1e-2));
    }

    [Fact]
    public void Classify_UsesWorstOfEnergyAndMomentumDrift()
    {
        Assert.Equal("Unstable", StabilityClassifier.Classify(2e-6, 2e-2));
        Assert.Equal("Marginal", StabilityClassifier.Classify(2e-6, 8e-4));
    }
}