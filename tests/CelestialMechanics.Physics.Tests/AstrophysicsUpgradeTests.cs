using CelestialMechanics.Math;
using CelestialMechanics.Physics.Astrophysics;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using Xunit;

namespace CelestialMechanics.Physics.Tests;

public class AstrophysicsUpgradeTests
{
    [Fact]
    public void Units_ExposeCanonicalSiConstants()
    {
        Assert.Equal(6.67430e-11, Units.G, 15);
        Assert.Equal(299792458.0, Units.C, 8);
        Assert.Equal(2.0, Units.RenderScale(99.0), 10);
    }

    [Fact]
    public void CollisionEnergyModel_SeparatesMergeFragmentCatastrophic()
    {
        var merge = CollisionEnergyModel.Evaluate(
            m1Solar: 1.0,
            m2Solar: 1.0,
            effectiveRadiusAu: 0.2,
            relativeSpeedSim: 0.001,
            maxMassLossFraction: 0.3);
        Assert.True(merge.IsMerge);

        var fragment = CollisionEnergyModel.Evaluate(
            m1Solar: 1.0,
            m2Solar: 1.0,
            effectiveRadiusAu: 0.2,
            relativeSpeedSim: 8.0,
            maxMassLossFraction: 0.4);
        Assert.True(fragment.IsFragmentation || fragment.IsCatastrophic);

        var catastrophic = CollisionEnergyModel.Evaluate(
            m1Solar: 1.0,
            m2Solar: 1.0,
            effectiveRadiusAu: 0.2,
            relativeSpeedSim: 20.0,
            maxMassLossFraction: 0.6);
        Assert.True(catastrophic.IsCatastrophic);
    }

    [Fact]
    public void ThermalRadiation_UpdatesBodyTemperatures()
    {
        var bodies = new[]
        {
            new PhysicsBody(0, 1.0, new Vec3d(0, 0, 0), Vec3d.Zero, BodyType.Star),
            new PhysicsBody(1, 0.001, new Vec3d(0.03, 0, 0), Vec3d.Zero, BodyType.RockyPlanet)
        };

        double initialPlanetT = bodies[1].Temperature;
        var thermal = new ThermalRadiationSystem();
        thermal.Update(bodies, dtSim: 0.01);

        Assert.True(bodies[1].Temperature != initialPlanetT);
        Assert.True(bodies[0].Luminosity > 0.0);
    }

    [Fact]
    public void BlackHoleEventHorizon_AbsorbsBodyWithinRs()
    {
        var bodies = new[]
        {
            new PhysicsBody(0, 1.0e8, Vec3d.Zero, Vec3d.Zero, BodyType.BlackHole),
            new PhysicsBody(1, 1.0, new Vec3d(0.5, 0, 0), Vec3d.Zero, BodyType.Star)
        };

        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });
        solver.ConfigureSoA(
            enabled: true,
            softening: 1e-4,
            deterministic: true,
            enableCollisions: false,
            enableBlackHolePhysics: true,
            enableThermalRadiation: false);

        var state = solver.Step(bodies, 1e-4);

        Assert.Equal(1, state.ActiveBodyCount);
        Assert.Contains(state.CollisionBursts, b => b.EventHorizonAbsorption);
    }
}
