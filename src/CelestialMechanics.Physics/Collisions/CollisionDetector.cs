using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// O(n²) brute-force sphere-sphere collision detector.
///
/// Operates on SoA arrays after the integration step.
/// Only considers bodies where IsActive && IsCollidable.
///
/// Detection criterion: distance(i,j) < Radius[i] + Radius[j]
///
/// The collision list is preallocated and reused across steps
/// to avoid per-step heap allocations.
/// </summary>
public sealed class CollisionDetector
{
    private readonly List<CollisionEvent> _events = new(32);

    /// <summary>
    /// Detect all sphere-sphere collisions in the current state.
    /// Returns collisions sorted by overlap depth (deepest first)
    /// to prioritise the most significant merges.
    /// </summary>
    public List<CollisionEvent> Detect(BodySoA bodies)
    {
        _events.Clear();
        int n = bodies.Count;

        double[] px = bodies.PosX;
        double[] py = bodies.PosY;
        double[] pz = bodies.PosZ;
        double[] r  = bodies.Radius;
        bool[] act  = bodies.IsActive;
        bool[] col  = bodies.IsCollidable;

        for (int i = 0; i < n; i++)
        {
            if (!act[i] || !col[i]) continue;

            double xi = px[i], yi = py[i], zi = pz[i];
            double ri = r[i];

            for (int j = i + 1; j < n; j++)
            {
                if (!act[j] || !col[j]) continue;

                double dx = xi - px[j];
                double dy = yi - py[j];
                double dz = zi - pz[j];

                double dist2 = dx * dx + dy * dy + dz * dz;
                double sumR = ri + r[j];
                double sumR2 = sumR * sumR;

                if (dist2 < sumR2)
                {
                    double dist = System.Math.Sqrt(dist2);
                    double overlap = sumR - dist;

                    // Ensure A is the heavier body (survivor)
                    int a = i, b = j;
                    if (bodies.Mass[j] > bodies.Mass[i])
                    {
                        a = j;
                        b = i;
                    }

                    _events.Add(new CollisionEvent(a, b, overlap));
                }
            }
        }

        // Sort deepest overlaps first for deterministic resolution order
        _events.Sort((x, y) => y.OverlapDepth.CompareTo(x.OverlapDepth));

        return _events;
    }
}
