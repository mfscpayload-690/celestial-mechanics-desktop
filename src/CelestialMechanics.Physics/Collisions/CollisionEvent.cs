namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Immutable record of a collision between two bodies detected in a single step.
/// </summary>
/// <param name="BodyIndexA">Index of the first body (heavier or equal).</param>
/// <param name="BodyIndexB">Index of the second body (lighter — will be absorbed).</param>
/// <param name="OverlapDepth">How deep the spheres overlap: (r1+r2) - distance. Always > 0.</param>
public readonly record struct CollisionEvent(
	int BodyIndexA,
	int BodyIndexB,
	double OverlapDepth,
	double Distance = 0.0,
	double NormalX = 0.0,
	double NormalY = 0.0,
	double NormalZ = 0.0,
	double RelativeNormalSpeed = 0.0);
