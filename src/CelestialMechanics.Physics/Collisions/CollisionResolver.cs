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

    private CollisionMode _mode = CollisionMode.MergeOnly;
    private double _defaultRestitution = 0.15;
    private double _fragmentationThreshold = 0.6;
    private double _fragmentationMassLossCap = 0.3;
    private double _captureVelocityFactor = 0.9;

    /// <summary>Number of merges that occurred in the last resolution call.</summary>
    public int LastCollisionCount => _collisionsThisStep;
    public IReadOnlyList<CollisionBurstEvent> LastBurstEvents => _burstEvents;

    public void Configure(
        CollisionMode mode,
        double defaultRestitution,
        double fragmentationSpecificEnergyThreshold,
        double fragmentationMassLossCap,
        double captureVelocityFactor)
    {
        _mode = mode;
        _defaultRestitution = System.Math.Clamp(defaultRestitution, 0.0, 1.0);
        _fragmentationThreshold = System.Math.Max(1e-8, fragmentationSpecificEnergyThreshold);
        _fragmentationMassLossCap = System.Math.Clamp(fragmentationMassLossCap, 0.0, 0.9);
        _captureVelocityFactor = System.Math.Max(0.0, captureVelocityFactor);
    }

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
            if (ma <= 0.0 || mb <= 0.0)
                continue;

            double ax = soa.PosX[a];
            double ay = soa.PosY[a];
            double az = soa.PosZ[a];
            double bx = soa.PosX[b];
            double by = soa.PosY[b];
            double bz = soa.PosZ[b];

            double nx = bx - ax;
            double ny = by - ay;
            double nz = bz - az;
            double dist2 = nx * nx + ny * ny + nz * nz;
            double dist = System.Math.Sqrt(dist2);

            if (dist > 1e-12)
            {
                double inv = 1.0 / dist;
                nx *= inv;
                ny *= inv;
                nz *= inv;
            }
            else
            {
                nx = 1.0;
                ny = 0.0;
                nz = 0.0;
                dist = 0.0;
            }

            double rvx = soa.VelX[b] - soa.VelX[a];
            double rvy = soa.VelY[b] - soa.VelY[a];
            double rvz = soa.VelZ[b] - soa.VelZ[a];
            double relNormalSpeed = rvx * nx + rvy * ny + rvz * nz;
            double relSpeed = System.Math.Sqrt(rvx * rvx + rvy * rvy + rvz * rvz);

            CollisionOutcome outcome = SelectOutcome(soa, a, b, relSpeed);

            if (_mode == CollisionMode.BounceOnly)
                outcome = CollisionOutcome.Bounce;
            else if (_mode == CollisionMode.MergeOnly)
                outcome = CollisionOutcome.Merge;

            if (outcome == CollisionOutcome.Accretion)
                EnsureAccretorSurvivor(soa, ref a, ref b);

            ma = soa.Mass[a];
            mb = soa.Mass[b];

            double keBefore = 0.5 * ma * (soa.VelX[a] * soa.VelX[a] + soa.VelY[a] * soa.VelY[a] + soa.VelZ[a] * soa.VelZ[a]) +
                              0.5 * mb * (soa.VelX[b] * soa.VelX[b] + soa.VelY[b] * soa.VelY[b] + soa.VelZ[b] * soa.VelZ[b]);

            switch (outcome)
            {
                case CollisionOutcome.Merge:
                case CollisionOutcome.Accretion:
                {
                    ResolveMerge(
                        soa, bodies, a, b,
                        dt, time,
                        accretionDisk,
                        promoteCompactRemnants,
                        outcome);
                    break;
                }

                case CollisionOutcome.Fragmentation:
                {
                    ResolveImpulse(soa, a, b, nx, ny, nz, relNormalSpeed, GetRestitution(soa, a, b) * 0.35);
                    PositionalCorrection(soa, a, b, evt.OverlapDepth, nx, ny, nz);

                    double specificEnergy = ComputeSpecificImpactEnergy(ma, mb, relSpeed);
                    double severity = specificEnergy / _fragmentationThreshold;
                    double ejectFrac = System.Math.Clamp(0.05 + 0.15 * severity, 0.02, _fragmentationMassLossCap);
                    double ejectedMass = (ma + mb) * ejectFrac;
                    double keepFrac = 1.0 - ejectFrac;

                    soa.Mass[a] = System.Math.Max(1e-12, ma * keepFrac);
                    soa.Mass[b] = System.Math.Max(1e-12, mb * keepFrac);
                    var ta = (BodyType)soa.BodyTypeIndex[a];
                    var tb = (BodyType)soa.BodyTypeIndex[b];
                    soa.Radius[a] = DensityModel.ComputeBodyRadius(soa.Mass[a], soa.Density[a], ta);
                    soa.Radius[b] = DensityModel.ComputeBodyRadius(soa.Mass[b], soa.Density[b], tb);

                    SyncBody(soa, bodies, a);
                    SyncBody(soa, bodies, b);

                    double keAfter = 0.5 * soa.Mass[a] * (soa.VelX[a] * soa.VelX[a] + soa.VelY[a] * soa.VelY[a] + soa.VelZ[a] * soa.VelZ[a]) +
                                     0.5 * soa.Mass[b] * (soa.VelX[b] * soa.VelX[b] + soa.VelY[b] * soa.VelY[b] + soa.VelZ[b] * soa.VelZ[b]);

                    EmitBurst(soa, a, b, System.Math.Max(0.0, keBefore - keAfter), ma + mb, ejectedMass, outcome);
                    break;
                }

                default:
                {
                    ResolveImpulse(soa, a, b, nx, ny, nz, relNormalSpeed, GetRestitution(soa, a, b));
                    PositionalCorrection(soa, a, b, evt.OverlapDepth, nx, ny, nz);

                    SyncBody(soa, bodies, a);
                    SyncBody(soa, bodies, b);

                    double keAfter = 0.5 * ma * (soa.VelX[a] * soa.VelX[a] + soa.VelY[a] * soa.VelY[a] + soa.VelZ[a] * soa.VelZ[a]) +
                                     0.5 * mb * (soa.VelX[b] * soa.VelX[b] + soa.VelY[b] * soa.VelY[b] + soa.VelZ[b] * soa.VelZ[b]);
                    EmitBurst(soa, a, b, System.Math.Max(0.0, keBefore - keAfter), ma + mb, 0.0, outcome);
                    break;
                }
            }

            _collisionsThisStep++;
        }
    }

    private CollisionOutcome SelectOutcome(BodySoA soa, int a, int b, double relSpeed)
    {
        var typeA = (BodyType)soa.BodyTypeIndex[a];
        var typeB = (BodyType)soa.BodyTypeIndex[b];

        bool compactA = IsCompactType(typeA);
        bool compactB = IsCompactType(typeB);

        if (compactA || compactB)
            return CollisionOutcome.Accretion;

        double ma = soa.Mass[a];
        double mb = soa.Mass[b];
        double sumR = soa.Radius[a] + soa.Radius[b];
        double vEscape = System.Math.Sqrt(2.0 * (ma + mb) / System.Math.Max(1e-9, sumR));

        double specificEnergy = ComputeSpecificImpactEnergy(ma, mb, relSpeed);
        if (specificEnergy >= _fragmentationThreshold)
            return CollisionOutcome.Fragmentation;

        if (relSpeed <= _captureVelocityFactor * vEscape)
            return CollisionOutcome.Merge;

        return CollisionOutcome.Bounce;
    }

    private void ResolveMerge(
        BodySoA soa,
        PhysicsBody[] bodies,
        int a,
        int b,
        double dt,
        double time,
        AccretionDiskSystem? accretionDisk,
        bool promoteCompactRemnants,
        CollisionOutcome outcome)
    {
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

        double va2 = soa.VelX[a] * soa.VelX[a] + soa.VelY[a] * soa.VelY[a] + soa.VelZ[a] * soa.VelZ[a];
        double vb2 = soa.VelX[b] * soa.VelX[b] + soa.VelY[b] * soa.VelY[b] + soa.VelZ[b] * soa.VelZ[b];

        double vx = (ma * soa.VelX[a] + mb * soa.VelX[b]) / mTotal;
        double vy = (ma * soa.VelY[a] + mb * soa.VelY[b]) / mTotal;
        double vz = (ma * soa.VelZ[a] + mb * soa.VelZ[b]) / mTotal;
        double vMerged2 = vx * vx + vy * vy + vz * vz;

        double keBefore = 0.5 * ma * va2 + 0.5 * mb * vb2;
        double keAfter = 0.5 * mTotal * vMerged2;
        double releasedEnergy = System.Math.Max(0.0, keBefore - keAfter);

        MergePolicy.Merge(soa, a, b);

        if (promoteCompactRemnants)
            PromoteCompactRemnant(soa, bodies, a);

        MergePolicy.SyncToAoS(bodies, soa, a, b);

        EmitBurst(soa, a, b, releasedEnergy, mTotal, 0.0, outcome);

        var survivorType = (BodyType)soa.BodyTypeIndex[a];
        if (accretionDisk != null && IsCompactType(survivorType))
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
    }

    private static void ResolveImpulse(
        BodySoA soa,
        int a,
        int b,
        double nx,
        double ny,
        double nz,
        double relativeNormalSpeed,
        double restitution)
    {
        if (relativeNormalSpeed >= 0.0)
            return;

        double ma = soa.Mass[a];
        double mb = soa.Mass[b];
        if (ma <= 0.0 || mb <= 0.0)
            return;

        double invMa = 1.0 / ma;
        double invMb = 1.0 / mb;
        double j = -(1.0 + restitution) * relativeNormalSpeed / (invMa + invMb);

        soa.VelX[a] -= j * invMa * nx;
        soa.VelY[a] -= j * invMa * ny;
        soa.VelZ[a] -= j * invMa * nz;

        soa.VelX[b] += j * invMb * nx;
        soa.VelY[b] += j * invMb * ny;
        soa.VelZ[b] += j * invMb * nz;
    }

    private static void PositionalCorrection(
        BodySoA soa,
        int a,
        int b,
        double overlapDepth,
        double nx,
        double ny,
        double nz)
    {
        if (overlapDepth <= 0.0)
            return;

        const double slop = 1e-4;
        const double percent = 0.6;

        double ma = soa.Mass[a];
        double mb = soa.Mass[b];
        if (ma <= 0.0 || mb <= 0.0)
            return;

        double invMa = 1.0 / ma;
        double invMb = 1.0 / mb;
        double correctionMag = percent * System.Math.Max(overlapDepth - slop, 0.0) / (invMa + invMb);

        soa.PosX[a] -= correctionMag * invMa * nx;
        soa.PosY[a] -= correctionMag * invMa * ny;
        soa.PosZ[a] -= correctionMag * invMa * nz;

        soa.PosX[b] += correctionMag * invMb * nx;
        soa.PosY[b] += correctionMag * invMb * ny;
        soa.PosZ[b] += correctionMag * invMb * nz;
    }

    private double GetRestitution(BodySoA soa, int a, int b)
    {
        double ra = GetRestitutionForType((BodyType)soa.BodyTypeIndex[a]);
        double rb = GetRestitutionForType((BodyType)soa.BodyTypeIndex[b]);
        return System.Math.Clamp(0.5 * (ra + rb), 0.0, 1.0);
    }

    private double GetRestitutionForType(BodyType type)
    {
        return type switch
        {
            BodyType.BlackHole => 0.0,
            BodyType.NeutronStar => 0.02,
            BodyType.Star => 0.05,
            BodyType.GasGiant => 0.08,
            BodyType.Planet or BodyType.RockyPlanet => 0.2,
            BodyType.Asteroid => 0.32,
            BodyType.Comet => 0.38,
            BodyType.Moon => 0.18,
            _ => _defaultRestitution
        };
    }

    private static double ComputeSpecificImpactEnergy(double m1, double m2, double relSpeed)
    {
        double totalMass = m1 + m2;
        if (totalMass <= 0.0)
            return 0.0;

        double mu = (m1 * m2) / totalMass;
        return 0.5 * mu * relSpeed * relSpeed / totalMass;
    }

    private void EmitBurst(
        BodySoA soa,
        int a,
        int b,
        double releasedEnergy,
        double combinedMass,
        double ejectedMass,
        CollisionOutcome outcome)
    {
        double px = (soa.PosX[a] + soa.PosX[b]) * 0.5;
        double py = (soa.PosY[a] + soa.PosY[b]) * 0.5;
        double pz = (soa.PosZ[a] + soa.PosZ[b]) * 0.5;

        _burstEvents.Add(new CollisionBurstEvent
        {
            Position = new CelestialMechanics.Math.Vec3d(px, py, pz),
            ReleasedEnergy = releasedEnergy,
            CombinedMass = combinedMass,
            EjectedMass = ejectedMass,
            Outcome = outcome,
            PrimaryBodyIndex = a,
            SecondaryBodyIndex = b
        });
    }

    private static void SyncBody(BodySoA soa, PhysicsBody[] bodies, int i)
    {
        bodies[i].Position = new CelestialMechanics.Math.Vec3d(soa.PosX[i], soa.PosY[i], soa.PosZ[i]);
        bodies[i].Velocity = new CelestialMechanics.Math.Vec3d(soa.VelX[i], soa.VelY[i], soa.VelZ[i]);
        bodies[i].Mass = soa.Mass[i];
        bodies[i].Radius = soa.Radius[i];
        bodies[i].Type = (BodyType)soa.BodyTypeIndex[i];
        bodies[i].IsActive = soa.IsActive[i];
        bodies[i].IsCollidable = soa.IsCollidable[i];
    }

    private static bool IsCompactType(BodyType type)
    {
        return type == BodyType.BlackHole || type == BodyType.NeutronStar;
    }

    private static void EnsureAccretorSurvivor(BodySoA soa, ref int a, ref int b)
    {
        bool compactA = IsCompactType((BodyType)soa.BodyTypeIndex[a]);
        bool compactB = IsCompactType((BodyType)soa.BodyTypeIndex[b]);

        if (!compactA && compactB)
            (a, b) = (b, a);
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
