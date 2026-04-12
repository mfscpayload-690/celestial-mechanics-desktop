using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Simulation.Analysis;

public static class CentralBodyDetector
{
    public static int FindCentralBody(IReadOnlyList<PhysicsBody> bodies)
    {
        int bestId = -1;
        double maxMass = double.MinValue;

        foreach (var b in bodies)
        {
            if (b.Mass > maxMass)
            {
                maxMass = b.Mass;
                bestId = b.Id;
            }
        }

        return bestId;
    }
}