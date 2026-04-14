using CelestialMechanics.Math;
using CelestialMechanics.Physics.Astrophysics;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Applies black-hole-specific interactions after integration:
/// event-horizon absorption and tidal stripping/disruption.
/// </summary>
public sealed class BlackHoleInteractionSystem
{
    private readonly List<CollisionBurstEvent> _events = new(8);

    public double TidalStrengthScale { get; set; } = 1.0;
    public double AccretionRadiusMultiplier { get; set; } = 12.0;

    public IReadOnlyList<CollisionBurstEvent> Process(
        BodySoA soa,
        PhysicsBody[] bodies,
        double dt,
        double time,
        AccretionDiskSystem? accretionDisk)
    {
        _events.Clear();

        int n = soa.Count;
        for (int i = 0; i < n; i++)
        {
            if (!soa.IsActive[i] || (BodyType)soa.BodyTypeIndex[i] != BodyType.BlackHole)
                continue;

            for (int j = 0; j < n; j++)
            {
                if (i == j || !soa.IsActive[j])
                    continue;

                BodyType targetType = (BodyType)soa.BodyTypeIndex[j];
                if (targetType == BodyType.BlackHole)
                    continue;

                double dx = soa.PosX[j] - soa.PosX[i];
                double dy = soa.PosY[j] - soa.PosY[i];
                double dz = soa.PosZ[j] - soa.PosZ[i];
                double distAu = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distAu <= 1e-15)
                    continue;

                double bhMass = soa.Mass[i];
                double targetMass = soa.Mass[j];
                if (bhMass <= 0.0 || targetMass <= 0.0)
                    continue;

                double rsAu = SchwarzschildRadius.ComputeSimUnits(bhMass);

                if (distAu < rsAu)
                {
                    AbsorbIntoBlackHole(soa, bodies, i, j, targetMass);
                    EmitBurst(soa, i, j, targetMass, 0.0, CollisionOutcome.Accretion, eventHorizonAbsorption: true);
                    continue;
                }

                double tidal = ComputeTidalField(bhMass, soa.Radius[j], distAu);
                double structuralStrength = GetStructuralStrength(targetType) * TidalStrengthScale;

                if (tidal <= structuralStrength)
                    continue;

                double severity = tidal / System.Math.Max(structuralStrength, 1e-20);
                double strippedFraction = System.Math.Clamp(0.08 + 0.22 * System.Math.Log10(1.0 + severity), 0.05, 0.92);
                double strippedMass = targetMass * strippedFraction;
                if (strippedMass <= 0.0)
                    continue;

                bool disrupted = strippedFraction > 0.85 || severity > 15.0;
                bool accretesToDisk = ShouldFeedDisk(soa, i, j, distAu, rsAu, AccretionRadiusMultiplier);

                double cpx = soa.PosX[i];
                double cpy = soa.PosY[i];
                double cpz = soa.PosZ[i];
                double apx = soa.PosX[j];
                double apy = soa.PosY[j];
                double apz = soa.PosZ[j];
                double avx = soa.VelX[j];
                double avy = soa.VelY[j];
                double avz = soa.VelZ[j];

                double bhMassNew = bhMass + strippedMass;
                double px = bhMass * soa.VelX[i] + strippedMass * soa.VelX[j];
                double py = bhMass * soa.VelY[i] + strippedMass * soa.VelY[j];
                double pz = bhMass * soa.VelZ[i] + strippedMass * soa.VelZ[j];
                soa.Mass[i] = bhMassNew;
                soa.VelX[i] = px / System.Math.Max(bhMassNew, 1e-15);
                soa.VelY[i] = py / System.Math.Max(bhMassNew, 1e-15);
                soa.VelZ[i] = pz / System.Math.Max(bhMassNew, 1e-15);
                soa.Radius[i] = DensityModel.ComputeBodyRadius(soa.Mass[i], soa.Density[i], BodyType.BlackHole);

                if (disrupted)
                {
                    soa.IsActive[j] = false;
                    soa.IsCollidable[j] = false;
                    strippedMass = targetMass;
                }
                else
                {
                    soa.Mass[j] = System.Math.Max(1e-14, targetMass - strippedMass);
                    soa.Radius[j] = DensityModel.ComputeBodyRadius(soa.Mass[j], soa.Density[j], targetType);
                }

                if (accretionDisk != null && accretesToDisk)
                {
                    accretionDisk.OnMatterAbsorbed(
                        compactBodyIndex: i,
                        absorbedMass: strippedMass,
                        cpx: cpx,
                        cpy: cpy,
                        cpz: cpz,
                        apx: apx,
                        apy: apy,
                        apz: apz,
                        avx: avx,
                        avy: avy,
                        avz: avz,
                        dt: dt,
                        time: time,
                        compactMass: bhMassNew);
                }

                SyncBody(soa, bodies, i);
                SyncBody(soa, bodies, j);

                EmitBurst(
                    soa,
                    i,
                    j,
                    strippedMass,
                    tidal,
                    disrupted ? CollisionOutcome.CatastrophicDisruption : CollisionOutcome.Fragmentation,
                    eventHorizonAbsorption: false);
            }
        }

        return _events;
    }

    private static bool ShouldFeedDisk(BodySoA soa, int bhIndex, int bodyIndex, double distanceAu, double rsAu, double radiusMultiplier)
    {
        double rx = soa.PosX[bodyIndex] - soa.PosX[bhIndex];
        double ry = soa.PosY[bodyIndex] - soa.PosY[bhIndex];
        double rz = soa.PosZ[bodyIndex] - soa.PosZ[bhIndex];

        double rvx = soa.VelX[bodyIndex] - soa.VelX[bhIndex];
        double rvy = soa.VelY[bodyIndex] - soa.VelY[bhIndex];
        double rvz = soa.VelZ[bodyIndex] - soa.VelZ[bhIndex];

        Vec3d h = Vec3d.Cross(new Vec3d(rx, ry, rz), new Vec3d(rvx, rvy, rvz));
        double angularMomentumPerMass = h.Length;
        double circularThreshold = System.Math.Sqrt(PhysicalConstants.G_Sim * System.Math.Max(soa.Mass[bhIndex], 1e-12) * System.Math.Max(distanceAu, 1e-12));

        double accretionRadius = System.Math.Max(rsAu * System.Math.Max(radiusMultiplier, 1.0), soa.Radius[bodyIndex] * 1.5);
        return distanceAu <= accretionRadius && angularMomentumPerMass >= 0.6 * circularThreshold;
    }

    private static double ComputeTidalField(double blackHoleMassSolar, double bodyRadiusAu, double distanceAu)
    {
        double mKg = Units.MassToKg(System.Math.Max(blackHoleMassSolar, 1e-12));
        double rBodyM = Units.DistanceToMeters(System.Math.Max(bodyRadiusAu, 1e-12));
        double distM = Units.DistanceToMeters(System.Math.Max(distanceAu, 1e-12));
        return (2.0 * Units.G * mKg * rBodyM) / System.Math.Pow(distM, 3.0);
    }

    private static double GetStructuralStrength(BodyType type)
    {
        return type switch
        {
            BodyType.Star => 5.0e2,
            BodyType.GasGiant => 1.5e3,
            BodyType.Comet => 5.0e4,
            BodyType.Asteroid => 2.0e6,
            BodyType.Planet or BodyType.RockyPlanet => 1.0e7,
            BodyType.Moon => 8.0e6,
            BodyType.NeutronStar => 1.0e16,
            _ => 1.0e6,
        };
    }

    private static void AbsorbIntoBlackHole(BodySoA soa, PhysicsBody[] bodies, int bhIndex, int bodyIndex, double absorbedMass)
    {
        double m0 = soa.Mass[bhIndex];
        double m1 = System.Math.Max(absorbedMass, 0.0);
        double total = m0 + m1;
        if (total <= 0.0)
            return;

        soa.PosX[bhIndex] = (m0 * soa.PosX[bhIndex] + m1 * soa.PosX[bodyIndex]) / total;
        soa.PosY[bhIndex] = (m0 * soa.PosY[bhIndex] + m1 * soa.PosY[bodyIndex]) / total;
        soa.PosZ[bhIndex] = (m0 * soa.PosZ[bhIndex] + m1 * soa.PosZ[bodyIndex]) / total;
        soa.VelX[bhIndex] = (m0 * soa.VelX[bhIndex] + m1 * soa.VelX[bodyIndex]) / total;
        soa.VelY[bhIndex] = (m0 * soa.VelY[bhIndex] + m1 * soa.VelY[bodyIndex]) / total;
        soa.VelZ[bhIndex] = (m0 * soa.VelZ[bhIndex] + m1 * soa.VelZ[bodyIndex]) / total;
        soa.Mass[bhIndex] = total;
        soa.Radius[bhIndex] = DensityModel.ComputeBodyRadius(total, soa.Density[bhIndex], BodyType.BlackHole);

        soa.IsActive[bodyIndex] = false;
        soa.IsCollidable[bodyIndex] = false;
        soa.AccX[bodyIndex] = 0.0;
        soa.AccY[bodyIndex] = 0.0;
        soa.AccZ[bodyIndex] = 0.0;
        soa.OldAccX[bodyIndex] = 0.0;
        soa.OldAccY[bodyIndex] = 0.0;
        soa.OldAccZ[bodyIndex] = 0.0;

        SyncBody(soa, bodies, bhIndex);
        SyncBody(soa, bodies, bodyIndex);
    }

    private void EmitBurst(BodySoA soa, int primary, int secondary, double absorbedMassSolar, double severity, CollisionOutcome outcome, bool eventHorizonAbsorption)
    {
        _events.Add(new CollisionBurstEvent
        {
            Position = new Vec3d(
                0.5 * (soa.PosX[primary] + soa.PosX[secondary]),
                0.5 * (soa.PosY[primary] + soa.PosY[secondary]),
                0.5 * (soa.PosZ[primary] + soa.PosZ[secondary])),
            ReleasedEnergy = severity,
            CombinedMass = soa.Mass[primary] + (soa.IsActive[secondary] ? soa.Mass[secondary] : 0.0),
            EjectedMass = absorbedMassSolar,
            Outcome = outcome,
            PrimaryBodyIndex = primary,
            SecondaryBodyIndex = secondary,
            EventHorizonAbsorption = eventHorizonAbsorption,
            Luminosity = eventHorizonAbsorption ? 0.0 : severity * 1e20
        });
    }

    private static void SyncBody(BodySoA soa, PhysicsBody[] bodies, int i)
    {
        if (i < 0 || i >= bodies.Length)
            return;

        bodies[i].Position = new Vec3d(soa.PosX[i], soa.PosY[i], soa.PosZ[i]);
        bodies[i].Velocity = new Vec3d(soa.VelX[i], soa.VelY[i], soa.VelZ[i]);
        bodies[i].Mass = soa.Mass[i];
        bodies[i].Radius = soa.Radius[i];
        bodies[i].Type = (BodyType)soa.BodyTypeIndex[i];
        bodies[i].IsActive = soa.IsActive[i];
        bodies[i].IsCollidable = soa.IsCollidable[i];
    }
}