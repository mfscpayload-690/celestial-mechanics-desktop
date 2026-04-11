namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Uniform-grid spatial hash broad-phase.
///
/// Uses a dynamic cell size based on the largest active radius to reduce
/// candidate pairs before narrow-phase sphere checks.
/// </summary>
public sealed class SpatialHashBroadPhase : IBroadPhase
{
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _cells = new();
    private readonly List<(int, int)> _pairs = new(256);
    private readonly HashSet<ulong> _pairKeys = new();

    public List<(int, int)> GetCandidatePairs(
        double[] posX,
        double[] posY,
        double[] posZ,
        double[] radius,
        bool[] isActive,
        int count)
    {
        _cells.Clear();
        _pairs.Clear();
        _pairKeys.Clear();

        double maxRadius = 0.0;
        for (int i = 0; i < count; i++)
        {
            if (!isActive[i]) continue;
            if (radius[i] > maxRadius) maxRadius = radius[i];
        }

        double cellSize = System.Math.Max(maxRadius * 2.0, 1e-6);
        double invCellSize = 1.0 / cellSize;

        for (int i = 0; i < count; i++)
        {
            if (!isActive[i]) continue;

            int cx = (int)System.Math.Floor(posX[i] * invCellSize);
            int cy = (int)System.Math.Floor(posY[i] * invCellSize);
            int cz = (int)System.Math.Floor(posZ[i] * invCellSize);
            var key = (cx, cy, cz);

            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<int>(8);
                _cells.Add(key, list);
            }

            list.Add(i);
        }

        foreach (var cell in _cells)
        {
            var (cx, cy, cz) = cell.Key;
            var home = cell.Value;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var neighborKey = (cx + dx, cy + dy, cz + dz);
                        if (!_cells.TryGetValue(neighborKey, out var neighbor))
                            continue;

                        for (int i = 0; i < home.Count; i++)
                        {
                            int a = home[i];
                            for (int j = 0; j < neighbor.Count; j++)
                            {
                                int b = neighbor[j];
                                if (a >= b) continue;

                                ulong key = ((ulong)(uint)a << 32) | (uint)b;
                                if (_pairKeys.Add(key))
                                    _pairs.Add((a, b));
                            }
                        }
                    }
                }
            }
        }

        _pairs.Sort(static (left, right) =>
        {
            int c = left.Item1.CompareTo(right.Item1);
            return c != 0 ? c : left.Item2.CompareTo(right.Item2);
        });

        return _pairs;
    }
}
