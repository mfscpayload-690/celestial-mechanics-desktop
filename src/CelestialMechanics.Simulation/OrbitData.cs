namespace CelestialMechanics.Simulation;

public enum OrbitType
{
    Circular,
    Elliptical,
    Parabolic,
    Hyperbolic,
    Chaotic
}

public class OrbitData
{
    public double Apoapsis;
    public double Periapsis;
    public double Eccentricity;
    public double SemiMajorAxis;
    public double Period;
    public OrbitType Type;
}