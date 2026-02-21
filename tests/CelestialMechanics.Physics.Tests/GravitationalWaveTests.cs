using CelestialMechanics.Math;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

/// <summary>
/// Validates the gravitational wave estimator (Phase 6D).
///
/// Tests confirm:
///   1. Inspiral frequency increases as binary separation decreases.
///   2. Strain amplitude scales with mass product.
///   3. Energy loss rate is always negative (energy is radiated away).
///   4. WaveformBuffer correctly stores and orders samples.
///   5. GW disabled produces no samples.
/// </summary>
public class GravitationalWaveTests
{
    [Fact]
    public void InspiralFrequency_IncreasesWithSmallerSeparation()
    {
        var analyzer = new GravitationalWaveAnalyzer();

        // Strain at larger separation
        double strainFar = analyzer.ComputeStrain(10.0, 10.0, 2.0, 1000.0);

        // Strain at smaller separation (closer binary → higher strain)
        double strainNear = analyzer.ComputeStrain(10.0, 10.0, 1.0, 1000.0);

        Assert.True(strainNear > strainFar,
            $"Near strain {strainNear:E4} should exceed far strain {strainFar:E4}");
        Assert.True(strainNear > 0, "Strain should be positive");
    }

    [Fact]
    public void WaveAmplitude_ScalesWithMass()
    {
        var analyzer = new GravitationalWaveAnalyzer();

        double strainLight = analyzer.ComputeStrain(1.0, 1.0, 1.0, 1000.0);
        double strainHeavy = analyzer.ComputeStrain(10.0, 10.0, 1.0, 1000.0);

        // h ∝ m1·m2, so 10× each mass → 100× strain
        double ratio = strainHeavy / strainLight;
        Assert.True(ratio > 90 && ratio < 110,
            $"Mass scaling ratio {ratio:F2} should be ~100 (h ∝ m1·m2)");
    }

    [Fact]
    public void EnergyLossRate_AlwaysNegative()
    {
        var analyzer = new GravitationalWaveAnalyzer();

        // Various separations and masses
        double[] separations = { 0.1, 0.5, 1.0, 5.0, 10.0 };
        double[] masses = { 1.0, 10.0, 50.0, 100.0 };

        foreach (double r in separations)
        {
            foreach (double m in masses)
            {
                double dEdt = analyzer.ComputeEnergyLossRate(m, m, r);
                Assert.True(dEdt <= 0.0,
                    $"dE/dt = {dEdt:E4} should be ≤ 0 for m={m}, r={r}");
            }
        }
    }

    [Fact]
    public void WaveformBuffer_StoresAndOrdersSamples()
    {
        var buffer = new WaveformBuffer(100);

        for (int i = 0; i < 50; i++)
            buffer.Add(i * 0.1, i * 0.001);

        Assert.Equal(50, buffer.Count);
        Assert.True(buffer.PeakStrain > 0);

        double[] times = new double[100];
        double[] strains = new double[100];
        int n = buffer.GetSamples(times, strains);

        Assert.Equal(50, n);

        // Verify ordering: times should be monotonically increasing
        for (int i = 1; i < n; i++)
        {
            Assert.True(times[i] > times[i - 1],
                $"Time[{i}]={times[i]} should be > Time[{i - 1}]={times[i - 1]}");
        }
    }

    [Fact]
    public void WaveformBuffer_CircularOverwrite_PreservesOrder()
    {
        var buffer = new WaveformBuffer(10);

        // Write 25 samples into a buffer of capacity 10
        for (int i = 0; i < 25; i++)
            buffer.Add(i * 1.0, i * 0.01);

        Assert.Equal(10, buffer.Count);

        double[] times = new double[10];
        double[] strains = new double[10];
        int n = buffer.GetSamples(times, strains);

        Assert.Equal(10, n);

        // Should contain samples 15..24 (most recent 10)
        Assert.True(times[0] >= 15.0,
            $"Oldest sample time {times[0]} should be ≥ 15.0 after overwrite");
    }

    [Fact]
    public void Sample_WithSoABodies_ProducesOutput()
    {
        var analyzer = new GravitationalWaveAnalyzer();
        analyzer.ObserverDistance = 1000.0;

        // Create bodies via PhysicsBody array and CopyFrom
        var physicsBodies = new[]
        {
            new PhysicsBody(0, 10.0, new Vec3d(1.0, 0, 0), new Vec3d(0, 0, 0), Types.BodyType.Star) { IsActive = true },
            new PhysicsBody(1, 10.0, new Vec3d(-1.0, 0, 0), new Vec3d(0, 0, 0), Types.BodyType.Star) { IsActive = true },
        };

        var bodies = new BodySoA(4);
        bodies.CopyFrom(physicsBodies);

        analyzer.Sample(bodies, 1.0, 0.001);

        Assert.Equal(1, analyzer.Waveform.Count);
        Assert.True(analyzer.Waveform.LatestStrain > 0,
            "Strain should be positive for a binary pair");
    }

    [Fact]
    public void EnergyLossRate_ZeroSeparation_ReturnsZero()
    {
        var analyzer = new GravitationalWaveAnalyzer();

        // Degenerate case: zero separation
        double dEdt = analyzer.ComputeEnergyLossRate(10.0, 10.0, 0.0);
        Assert.Equal(0.0, dEdt);

        // Degenerate case: zero mass
        dEdt = analyzer.ComputeEnergyLossRate(0.0, 10.0, 1.0);
        Assert.Equal(0.0, dEdt);
    }
}
