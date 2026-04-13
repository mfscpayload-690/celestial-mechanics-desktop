using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Models;

public sealed class BodySubtype
{
    public BodySubtype(BodyType baseType, string name, double mass, double radius)
    {
        BaseType = baseType;
        Name = name;
        Mass = mass;
        Radius = radius;
    }

    public BodyType BaseType { get; }
    public string Name { get; }
    public double Mass { get; }
    public double Radius { get; }

    public override string ToString() => Name;
}
