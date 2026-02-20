using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Collisions;

/// <summary>
/// Default merge policy for collision resolution.
///
/// Conservation laws:
///   Mass:     m_total = m1 + m2
///   Momentum: v_total = (m1·v1 + m2·v2) / m_total
///   Position: center of mass
///   Density:  mass-weighted average
///
/// Energy is NOT artificially conserved — kinetic energy loss is physically
/// acceptable (radiation, heat dissipation).
///
/// Special rules:
///   BlackHole: always absorbs, radius = Schwarzschild radius.
/// </summary>
public static class MergePolicy
{
    /// <summary>
    /// Apply a momentum-conserving merge of body B into body A on the SoA arrays.
    /// Body A (survivor) receives combined mass, momentum, and position.
    /// Body B is marked inactive.
    /// </summary>
    /// <param name="soa">SoA buffer containing both bodies.</param>
    /// <param name="a">Index of the survivor (heavier body).</param>
    /// <param name="b">Index of the absorbed body.</param>
    public static void Merge(BodySoA soa, int a, int b)
    {
        double ma = soa.Mass[a];
        double mb = soa.Mass[b];
        double mTotal = ma + mb;

        if (mTotal <= 0.0) return;

        double invTotal = 1.0 / mTotal;

        // ── Momentum-conserving velocity ────────────────────────────────────
        soa.VelX[a] = (ma * soa.VelX[a] + mb * soa.VelX[b]) * invTotal;
        soa.VelY[a] = (ma * soa.VelY[a] + mb * soa.VelY[b]) * invTotal;
        soa.VelZ[a] = (ma * soa.VelZ[a] + mb * soa.VelZ[b]) * invTotal;

        // ── Center-of-mass position ────────────────────────────────────────
        soa.PosX[a] = (ma * soa.PosX[a] + mb * soa.PosX[b]) * invTotal;
        soa.PosY[a] = (ma * soa.PosY[a] + mb * soa.PosY[b]) * invTotal;
        soa.PosZ[a] = (ma * soa.PosZ[a] + mb * soa.PosZ[b]) * invTotal;

        // ── Mass conservation ──────────────────────────────────────────────
        soa.Mass[a] = mTotal;

        // ── Density: mass-weighted average ─────────────────────────────────
        soa.Density[a] = (ma * soa.Density[a] + mb * soa.Density[b]) * invTotal;

        // ── Body type promotion: black hole absorbs everything ─────────────
        BodyType typeA = (BodyType)soa.BodyTypeIndex[a];
        BodyType typeB = (BodyType)soa.BodyTypeIndex[b];

        if (typeB == BodyType.BlackHole)
            soa.BodyTypeIndex[a] = (int)BodyType.BlackHole;

        BodyType survivorType = (BodyType)soa.BodyTypeIndex[a];

        // ── Recompute radius ───────────────────────────────────────────────
        soa.Radius[a] = DensityModel.ComputeBodyRadius(mTotal, soa.Density[a], survivorType);

        // ── Mark absorbed body inactive ────────────────────────────────────
        soa.IsActive[b] = false;
        soa.IsCollidable[b] = false;

        // Zero out absorbed body's acceleration to prevent ghost forces
        soa.AccX[b] = 0.0;
        soa.AccY[b] = 0.0;
        soa.AccZ[b] = 0.0;
        soa.OldAccX[b] = 0.0;
        soa.OldAccY[b] = 0.0;
        soa.OldAccZ[b] = 0.0;
    }

    /// <summary>
    /// Apply merge on the AoS PhysicsBody array to keep it in sync.
    /// Must be called after the SoA merge.
    /// </summary>
    public static void SyncToAoS(PhysicsBody[] bodies, BodySoA soa, int a, int b)
    {
        bodies[a].Mass = soa.Mass[a];
        bodies[a].Density = soa.Density[a];
        bodies[a].Radius = soa.Radius[a];
        bodies[a].Position = new CelestialMechanics.Math.Vec3d(soa.PosX[a], soa.PosY[a], soa.PosZ[a]);
        bodies[a].Velocity = new CelestialMechanics.Math.Vec3d(soa.VelX[a], soa.VelY[a], soa.VelZ[a]);
        bodies[a].Type = (BodyType)soa.BodyTypeIndex[a];

        bodies[b].IsActive = false;
        bodies[b].IsCollidable = false;
    }
}
