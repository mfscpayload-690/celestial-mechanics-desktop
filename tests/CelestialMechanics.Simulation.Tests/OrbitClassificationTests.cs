using CelestialMechanics.Simulation;

namespace CelestialMechanics.Simulation.Tests;

public class OrbitClassificationTests
{
    [Fact]
    public void ClassifyOrbitType_Circular_WhenEccentricityNearZero()
    {
        Assert.Equal(OrbitType.Circular, OrbitCalculator.ClassifyOrbitType(0.001));
    }

    [Fact]
    public void ClassifyOrbitType_Elliptical_WhenEccentricityBelowOne()
    {
        Assert.Equal(OrbitType.Elliptical, OrbitCalculator.ClassifyOrbitType(0.42));
    }

    [Fact]
    public void ClassifyOrbitType_Parabolic_WhenNearOne()
    {
        Assert.Equal(OrbitType.Elliptical, OrbitCalculator.ClassifyOrbitType(0.98));
        Assert.Equal(OrbitType.Parabolic, OrbitCalculator.ClassifyOrbitType(1.02));
    }

    [Fact]
    public void ClassifyOrbitType_Hyperbolic_WhenWellAboveOne()
    {
        Assert.Equal(OrbitType.Hyperbolic, OrbitCalculator.ClassifyOrbitType(1.2));
    }
}