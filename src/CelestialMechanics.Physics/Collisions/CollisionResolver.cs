using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Resolves detected collisions by applying the merge policy.
///
/// Resolution order:
///   1. Receive sorted collision events (deepest overlap first)
///   2. For each event, check both bodies are still active (may have
///      been merged by a prior event in the same step)
///   3. Apply MergePolicy.Merge on SoA arrays
///   4. Sync changes to AoS PhysicsBody array
///
/// Thread safety: collision resolution is always single-threaded.
/// Structural mutations (marking bodies inactive) never happen during
/// the parallel force computation.
/// </summary>
public sealed class CollisionResolver
{
    private int _collisionsThisStep;
    private readonly List<CollisionBurstEvent> _burstEvents = new(8);

    /// <summary>Number of merges that occurred in the last resolution call.</summary>
    public int LastCollisionCount => _collisionsThisStep;
    public IReadOnlyList<CollisionBurstEvent> LastBurstEvents => _burstEvents;

    /// <summary>
    /// Resolve all collisions by merging bodies according to MergePolicy.
    /// </summary>
    /// <param name="events">Collision events (sorted deepest-first).</param>
    /// <param name="soa">SoA body buffer.</param>
    /// <param name="bodies">AoS body array to keep in sync.</param>
    public void Resolve(
        List<CollisionEvent> events,
        BodySoA soa,
        PhysicsBody[] bodies,
        double dt = 0.0,
        double time = 0.0,
        AccretionDiskSystem? accretionDisk = null,
        bool promoteCompactRemnants = false)
    {
        _collisionsThisStep = 0;
        _burstEvents.Clear();

        foreach (var evt in events)
        {
            int a = evt.BodyIndexA;
            int b = evt.BodyIndexB;

            // Skip if either body was already merged in this step
            if (!soa.IsActive[a] || !soa.IsActive[b])
                continue;

            double ma = soa.Mass[a];
            double mb = soa.Mass[b];
            double mTotal = ma + mb;

            double preCompactX = soa.PosX[a];
            double preCompactY = soa.PosY[a];
            double preCompactZ = soa.PosZ[a];
            double absorbedX = soa.PosX[b];
            double absorbedY = soa.PosY[b];
            double absorbedZ = soa.PosZ[b];
            double absorbedVx = soa.VelX[b];
            double absorbedVy = soa.VelY[b];
            double absorbedVz = soa.VelZ[b];

            if (mTotal > 0.0)
            {
                double va2 = soa.VelX[a] * soa.VelX[a] + soa.VelY[a] * soa.VelY[a] + soa.VelZ[a] * soa.VelZ[a];
                double vb2 = soa.VelX[b] * soa.VelX[b] + soa.VelY[b] * soa.VelY[b] + soa.VelZ[b] * soa.VelZ[b];

                double vx = (ma * soa.VelX[a] + mb * soa.VelX[b]) / mTotal;
                double vy = (ma * soa.VelY[a] + mb * soa.VelY[b]) / mTotal;
                double vz = (ma * soa.VelZ[a] + mb * soa.VelZ[b]) / mTotal;
                double vMerged2 = vx * vx + vy * vy + vz * vz;

                double keBefore = 0.5 * ma * va2 + 0.5 * mb * vb2;
                double keAfter = 0.5 * mTotal * vMerged2;
                double releasedEnergy = System.Math.Max(0.0, keBefore - keAfter);

                double px = (ma * soa.PosX[a] + mb * soa.PosX[b]) / mTotal;
                double py = (ma * soa.PosY[a] + mb * soa.PosY[b]) / mTotal;
                double pz = (ma * soa.PosZ[a] + mb * soa.PosZ[b]) / mTotal;

                _burstEvents.Add(new CollisionBurstEvent
                {
                    Position = new CelestialMechanics.Math.Vec3d(px, py, pz),
                    ReleasedEnergy = releasedEnergy,
                    CombinedMass = mTotal
                });
            }

            MergePolicy.Merge(soa, a, b);

            if (promoteCompactRemnants)
                PromoteCompactRemnant(soa, bodies, a);

            MergePolicy.SyncToAoS(bodies, soa, a, b);

            var survivorType = (BodyType)soa.BodyTypeIndex[a];
            if (accretionDisk != null &&
                (survivorType == BodyType.BlackHole || survivorType == BodyType.NeutronStar))
            {
                accretionDisk.OnMatterAbsorbed(
                    compactBodyIndex: a,
                    absorbedMass: mb,
                    cpx: preCompactX,
                    cpy: preCompactY,
                    cpz: preCompactZ,
                    apx: absorbedX,
                    apy: absorbedY,
                    apz: absorbedZ,
                    avx: absorbedVx,
                    avy: absorbedVy,
                    avz: absorbedVz,
                    dt: dt,
                    time: time);
            }

            _collisionsThisStep++;
        }
    }

    private static void PromoteCompactRemnant(BodySoA soa, PhysicsBody[] bodies, int survivorIndex)
    {
        const double neutronStarThreshold = 1.6;
        const double blackHoleThreshold = 3.0;

        var currentType = (BodyType)soa.BodyTypeIndex[survivorIndex];
        bool stellarFamily = currentType == BodyType.Star ||
                             currentType == BodyType.NeutronStar ||
                             currentType == BodyType.BlackHole;

        if (!stellarFamily)
            return;

        double remnantMass = soa.Mass[survivorIndex];
        BodyType targetType = currentType;

        if (remnantMass >= blackHoleThreshold)
            targetType = BodyType.BlackHole;
        else if (remnantMass >= neutronStarThreshold)
            targetType = BodyType.NeutronStar;

        if (targetType == currentType)
            return;

        double density = DensityModel.GetDefaultDensity(targetType);
        double radius = DensityModel.ComputeBodyRadius(remnantMass, density, targetType);

        soa.BodyTypeIndex[survivorIndex] = (int)targetType;
        soa.Density[survivorIndex] = density;
        soa.Radius[survivorIndex] = radius;

        bodies[survivorIndex].Type = targetType;
        bodies[survivorIndex].Density = density;
        bodies[survivorIndex].Radius = radius;
    }
}
