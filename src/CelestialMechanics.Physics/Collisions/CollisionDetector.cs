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
    private readonly IBroadPhase _broadPhase = new SpatialHashBroadPhase();

    private bool _useBroadPhase = true;
    private int _broadPhaseThreshold = 96;

    public void Configure(bool useBroadPhase, int broadPhaseThreshold)
    {
        _useBroadPhase = useBroadPhase;
        _broadPhaseThreshold = System.Math.Max(16, broadPhaseThreshold);
    }

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

        if (_useBroadPhase && n >= _broadPhaseThreshold)
        {
            var candidates = _broadPhase.GetCandidatePairs(px, py, pz, r, act, n);
            for (int k = 0; k < candidates.Count; k++)
            {
                var (i, j) = candidates[k];
                TryAddEvent(i, j, bodies, px, py, pz, r, act, col);
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                if (!act[i] || !col[i]) continue;
                for (int j = i + 1; j < n; j++)
                    TryAddEvent(i, j, bodies, px, py, pz, r, act, col);
            }
        }

        // Sort deepest overlaps first for deterministic resolution order
        _events.Sort((x, y) => y.OverlapDepth.CompareTo(x.OverlapDepth));

        return _events;
    }

    private void TryAddEvent(
        int i,
        int j,
        BodySoA bodies,
        double[] px,
        double[] py,
        double[] pz,
        double[] r,
        bool[] act,
        bool[] col)
    {
        if (!act[i] || !col[i] || !act[j] || !col[j])
            return;

        double dx = px[i] - px[j];
        double dy = py[i] - py[j];
        double dz = pz[i] - pz[j];

        double dist2 = dx * dx + dy * dy + dz * dz;
        double sumR = r[i] + r[j];
        double sumR2 = sumR * sumR;

        if (dist2 >= sumR2)
            return;

        double dist = System.Math.Sqrt(dist2);
        double overlap = sumR - dist;

        double nx = 0.0, ny = 0.0, nz = 0.0;
        if (dist > 1e-12)
        {
            nx = dx / dist;
            ny = dy / dist;
            nz = dz / dist;
        }

        // Relative normal speed (a->b normal), negative means closing.
        double rvx = bodies.VelX[j] - bodies.VelX[i];
        double rvy = bodies.VelY[j] - bodies.VelY[i];
        double rvz = bodies.VelZ[j] - bodies.VelZ[i];
        double relNormalSpeed = rvx * nx + rvy * ny + rvz * nz;

        int a = i;
        int b = j;
        if (bodies.Mass[j] > bodies.Mass[i])
        {
            a = j;
            b = i;
            nx = -nx;
            ny = -ny;
            nz = -nz;
            relNormalSpeed = -relNormalSpeed;
        }

        _events.Add(new CollisionEvent(
            BodyIndexA: a,
            BodyIndexB: b,
            OverlapDepth: overlap,
            Distance: dist,
            NormalX: nx,
            NormalY: ny,
            NormalZ: nz,
            RelativeNormalSpeed: relNormalSpeed));
    }
}
