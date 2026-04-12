using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Analysis;

namespace CelestialMechanics.Simulation.Tests;

public class EventDetectorTests
{
    [Fact]
    public void DetectPrimaryEvent_ReturnsCollisionImminent_WhenBodiesAreClose()
    {
        var selected = CreateBody(1, 1.0, 0.05, new Vec3d(0.10, 0.0, 0.0), Vec3d.Zero);
        var central = CreateBody(0, 5.0, 0.08, Vec3d.Zero, Vec3d.Zero);

        string? evt = EventDetector.DetectPrimaryEvent(selected, central, orbitData: null);

        Assert.Equal("Collision Imminent", evt);
    }

    [Fact]
    public void DetectPrimaryEvent_ReturnsEscapeTrajectory_WhenSpeedExceedsEscapeVelocity()
    {
        var selected = CreateBody(1, 1.0, 0.01, new Vec3d(1.0, 0.0, 0.0), new Vec3d(0.0, 0.0, 2.2));
        var central = CreateBody(0, 1.0, 0.01, Vec3d.Zero, Vec3d.Zero);

        string? evt = EventDetector.DetectPrimaryEvent(selected, central, orbitData: null);

        Assert.Equal("Escape Trajectory", evt);
    }

    [Fact]
    public void DetectPrimaryEvent_ReturnsOrbitDecay_WhenPeriapsisShrinksAndMotionIsInward()
    {
        var selected = CreateBody(1, 0.1, 0.02, new Vec3d(1.0, 0.0, 0.0), new Vec3d(-0.2, 0.0, 0.0));
        var central = CreateBody(0, 10.0, 0.10, Vec3d.Zero, Vec3d.Zero);
        var orbit = new OrbitData
        {
            Type = OrbitType.Elliptical,
            Periapsis = 0.3,
            Apoapsis = 1.5,
            Eccentricity = 0.5,
            SemiMajorAxis = 0.9,
            Period = 2.0,
        };

        string? evt = EventDetector.DetectPrimaryEvent(selected, central, orbit);

        Assert.Equal("Orbit Decay", evt);
    }

    [Fact]
    public void DetectPrimaryEvent_ReturnsNull_WhenNoScientificEventDetected()
    {
        var selected = CreateBody(1, 1.0, 0.02, new Vec3d(5.0, 0.0, 0.0), new Vec3d(0.0, 0.0, 0.1));
        var central = CreateBody(0, 5.0, 0.08, Vec3d.Zero, Vec3d.Zero);
        var orbit = new OrbitData
        {
            Type = OrbitType.Elliptical,
            Periapsis = 2.0,
            Apoapsis = 8.0,
            Eccentricity = 0.4,
            SemiMajorAxis = 5.0,
            Period = 10.0,
        };

        string? evt = EventDetector.DetectPrimaryEvent(selected, central, orbit);

        Assert.Null(evt);
    }

    private static PhysicsBody CreateBody(int id, double mass, double radius, Vec3d position, Vec3d velocity)
    {
        return new PhysicsBody(id, mass, position, velocity, BodyType.Planet)
        {
            Radius = radius,
            IsActive = true,
            IsCollidable = true,
        };
    }
}