using CelestialMechanics.Math;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Manages particle-based accretion disk simulation for compact objects.
///
/// When matter is absorbed by a black hole or neutron star (via collision merge),
/// this system spawns disk particles with angular momentum derived from the
/// incoming body. Particles orbit, drift inward, heat up, and eventually
/// cross the ISCO (innermost stable circular orbit).
///
/// Accretion rate model:
///   ṁ = massAbsorbed / dt   (instantaneous)
///   ṁ decays exponentially: ṁ(t) = ṁ₀ · exp(-t/τ_decay)
///
/// Luminosity: L = η · ṁ · c²   (η = accretion efficiency)
///
/// Performance: runs on a separate update loop from N-body; no interference
/// with the O(n²) gravity calculation.
/// </summary>
public sealed class AccretionDiskSystem : IAccretionModel
{
    private DiskParticle[] _particles;
    private int _activeCount;
    private readonly Random _rng;

    // Per-compact-object accretion state
    private readonly Dictionary<int, AccretionState> _accretionStates = new();

    /// <summary>Maximum particles across all disks.</summary>
    public int MaxParticles { get; }

    /// <summary>Whether jet emission is enabled.</summary>
    public bool EnableJets { get; set; } = false;

    /// <summary>Accretion rate threshold for jet emission.</summary>
    public double JetThreshold { get; set; } = 0.1;

    /// <summary>Exponential decay timescale for accretion rate (sim time units).</summary>
    public double AccretionDecayTimescale { get; set; } = 10.0;

    /// <summary>Read-only access to particle array for rendering.</summary>
    public ReadOnlySpan<DiskParticle> Particles => _particles.AsSpan(0, _particles.Length);

    /// <summary>Number of currently active particles.</summary>
    public int ActiveCount => _activeCount;

    public AccretionDiskSystem(int maxParticles = 5000, int seed = 42)
    {
        MaxParticles = maxParticles;
        _particles = new DiskParticle[maxParticles];
        _activeCount = 0;
        _rng = new Random(seed);
    }

    /// <summary>
    /// Accretion state for a single compact object.
    /// </summary>
    private class AccretionState
    {
        public double AccretionRate;     // current ṁ
        public double PeakAccretionRate; // peak ṁ ever observed
        public double TotalAccreted;     // cumulative mass accreted
        public double SpinAxisX, SpinAxisY, SpinAxisZ; // spin axis (unit vector)
        public double LastFeedTime;      // last time matter was added
    }

    /// <inheritdoc/>
    public double ComputeAccretionRate(int compactBodyIndex,
        double[] mass, double[] posX, double[] posY, double[] posZ, int count)
    {
        if (_accretionStates.TryGetValue(compactBodyIndex, out var state))
            return state.AccretionRate;
        return 0.0;
    }

    /// <summary>
    /// Notify the system that a body was absorbed by a compact object.
    /// Spawns disk particles and updates accretion rate.
    /// </summary>
    /// <param name="compactBodyIndex">Index of the absorbing compact object.</param>
    /// <param name="absorbedMass">Mass of the absorbed body.</param>
    /// <param name="compactPos">Position of the compact object.</param>
    /// <param name="absorbedPos">Position of the absorbed body at impact.</param>
    /// <param name="absorbedVel">Velocity of the absorbed body relative to compact.</param>
    /// <param name="dt">Current timestep.</param>
    /// <param name="time">Current simulation time.</param>
    public void OnMatterAbsorbed(
        int compactBodyIndex, double absorbedMass,
        double cpx, double cpy, double cpz,
        double apx, double apy, double apz,
        double avx, double avy, double avz,
        double dt, double time)
    {
        if (!_accretionStates.TryGetValue(compactBodyIndex, out var state))
        {
            state = new AccretionState();
            _accretionStates[compactBodyIndex] = state;
        }

        // Update accretion rate
        double instantRate = dt > 0 ? absorbedMass / dt : 0.0;
        state.AccretionRate += instantRate;
        state.PeakAccretionRate = System.Math.Max(state.PeakAccretionRate, state.AccretionRate);
        state.TotalAccreted += absorbedMass;
        state.LastFeedTime = time;

        // Compute angular momentum direction for spin axis
        double dx = apx - cpx, dy = apy - cpy, dz = apz - cpz;
        // L = r × v
        double lx = dy * avz - dz * avy;
        double ly = dz * avx - dx * avz;
        double lz = dx * avy - dy * avx;
        double lMag = System.Math.Sqrt(lx * lx + ly * ly + lz * lz);

        if (lMag > 1e-15)
        {
            // Blend with existing spin axis (weighted by mass)
            double weight = absorbedMass / (state.TotalAccreted + 1e-30);
            state.SpinAxisX = state.SpinAxisX * (1 - weight) + (lx / lMag) * weight;
            state.SpinAxisY = state.SpinAxisY * (1 - weight) + (ly / lMag) * weight;
            state.SpinAxisZ = state.SpinAxisZ * (1 - weight) + (lz / lMag) * weight;

            // Re-normalize
            double sMag = System.Math.Sqrt(
                state.SpinAxisX * state.SpinAxisX +
                state.SpinAxisY * state.SpinAxisY +
                state.SpinAxisZ * state.SpinAxisZ);
            if (sMag > 1e-15)
            {
                state.SpinAxisX /= sMag;
                state.SpinAxisY /= sMag;
                state.SpinAxisZ /= sMag;
            }
        }
        else if (state.TotalAccreted <= absorbedMass)
        {
            // First accretion, default spin axis to Z
            state.SpinAxisX = 0; state.SpinAxisY = 0; state.SpinAxisZ = 1;
        }

        // Spawn disk particles
        SpawnDiskParticles(compactBodyIndex, cpx, cpy, cpz, dx, dy, dz, avx, avy, avz, absorbedMass, state);
    }

    private void SpawnDiskParticles(
        int parentIndex,
        double cx, double cy, double cz,
        double dx, double dy, double dz,
        double vx, double vy, double vz,
        double mass, AccretionState state)
    {
        // Number of particles proportional to absorbed mass (capped)
        int count = System.Math.Min(50, System.Math.Max(5, (int)(mass * 100)));

        double dist = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double innerRadius = dist * 0.1;  // Near ISCO
        double outerRadius = dist * 2.0;  // Outer disk

        for (int i = 0; i < count; i++)
        {
            int slot = FindFreeSlot();
            if (slot < 0) break; // All slots full

            double t = _rng.NextDouble();
            double r = innerRadius + t * (outerRadius - innerRadius);
            double angle = _rng.NextDouble() * 2.0 * System.Math.PI;

            // Construct position in the disk plane (perpendicular to spin axis)
            // Use Gram-Schmidt to get two orthonormal vectors in the disk plane
            GetDiskPlaneVectors(state.SpinAxisX, state.SpinAxisY, state.SpinAxisZ,
                out double e1x, out double e1y, out double e1z,
                out double e2x, out double e2y, out double e2z);

            double cosA = System.Math.Cos(angle);
            double sinA = System.Math.Sin(angle);

            ref var p = ref _particles[slot];
            p.PosX = cx + r * (cosA * e1x + sinA * e2x);
            p.PosY = cy + r * (cosA * e1y + sinA * e2y);
            p.PosZ = cz + r * (cosA * e1z + sinA * e2z);

            // Circular velocity: v_circ = sqrt(G·M/r), tangent in disk plane
            double vCirc = System.Math.Sqrt(PhysicalConstants.G_Sim * mass / System.Math.Max(r, 1e-10));
            p.VelX = vCirc * (-sinA * e1x + cosA * e2x);
            p.VelY = vCirc * (-sinA * e1y + cosA * e2y);
            p.VelZ = vCirc * (-sinA * e1z + cosA * e2z);

            // Temperature: T ∝ r^(-3/4) (Shakura-Sunyaev profile)
            double rNorm = r / System.Math.Max(innerRadius, 1e-10);
            p.Temperature = 1e6 * System.Math.Pow(rNorm, -0.75);

            p.Age = 0.0;
            p.MaxAge = 5.0 + _rng.NextDouble() * 10.0; // 5–15 sim time units
            p.ParentBodyIndex = parentIndex;
            p.IsActive = true;
            p.OrbitalRadius = r;
        }
    }

    /// <summary>
    /// Update all active disk particles: drift inward, age, update temperature.
    /// Called once per timestep, separate from the N-body loop.
    /// </summary>
    /// <param name="bodies">Current body state (for parent body positions).</param>
    /// <param name="dt">Current timestep.</param>
    /// <param name="time">Current simulation time.</param>
    public void Update(BodySoA bodies, double dt, double time)
    {
        double[] px = bodies.PosX, py = bodies.PosY, pz = bodies.PosZ;
        double[] m = bodies.Mass;
        bool[] act = bodies.IsActive;

        _activeCount = 0;

        // Decay accretion rates
        foreach (var kvp in _accretionStates)
        {
            var state = kvp.Value;
            double elapsed = time - state.LastFeedTime;
            if (elapsed > 0)
            {
                state.AccretionRate = state.PeakAccretionRate *
                    System.Math.Exp(-elapsed / AccretionDecayTimescale);
            }
        }

        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (!p.IsActive) continue;

            // Check if parent is still active
            int pi = p.ParentBodyIndex;
            if (pi >= bodies.Count || !act[pi])
            {
                p.IsActive = false;
                continue;
            }

            p.Age += dt;
            if (p.Age >= p.MaxAge)
            {
                p.IsActive = false;
                continue;
            }

            // Vector from particle to parent
            double dx = px[pi] - p.PosX;
            double dy = py[pi] - p.PosY;
            double dz = pz[pi] - p.PosZ;
            double dist = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist < 1e-10)
            {
                p.IsActive = false;
                continue;
            }

            double invDist = 1.0 / dist;

            // Gravity from parent body
            double acc = PhysicalConstants.G_Sim * m[pi] * invDist * invDist;

            // Add gravitational acceleration toward parent
            double nx = dx * invDist, ny = dy * invDist, nz = dz * invDist;
            p.VelX += acc * nx * dt;
            p.VelY += acc * ny * dt;
            p.VelZ += acc * nz * dt;

            // Radial inward drift (viscous dissipation)
            double driftFactor = 0.001; // Small viscous coupling
            p.VelX += driftFactor * nx * dt;
            p.VelY += driftFactor * ny * dt;
            p.VelZ += driftFactor * nz * dt;

            // Update position
            p.PosX += p.VelX * dt;
            p.PosY += p.VelY * dt;
            p.PosZ += p.VelZ * dt;

            // Update orbital radius
            p.OrbitalRadius = dist;

            // Update temperature based on radius (T ∝ r^(-3/4))
            double rs = PhysicalConstants.SchwarzschildFactorSim * m[pi];
            double rNorm = dist / System.Math.Max(rs * 3.0, 1e-10); // normalize to ISCO
            p.Temperature = 1e6 * System.Math.Pow(System.Math.Max(rNorm, 0.1), -0.75);

            // Remove if fallen past event horizon
            if (dist < rs)
            {
                p.IsActive = false;
                continue;
            }

            _activeCount++;
        }

        // Spawn jet particles if enabled and accretion rate is high enough
        if (EnableJets)
        {
            foreach (var kvp in _accretionStates)
            {
                if (kvp.Value.AccretionRate > JetThreshold)
                {
                    int pi = kvp.Key;
                    if (pi < bodies.Count && act[pi])
                    {
                        SpawnJetParticles(pi, px[pi], py[pi], pz[pi], kvp.Value, dt);
                    }
                }
            }
        }
    }

    private void SpawnJetParticles(int parentIndex, double cx, double cy, double cz,
        AccretionState state, double dt)
    {
        // Spawn 2 particles per step along spin axis (bipolar jet)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int slot = FindFreeSlot();
            if (slot < 0) return;

            double jetSpeed = PhysicalConstants.C_Sim * 0.1; // 10% c
            double offset = 0.01; // Small offset from centre

            ref var p = ref _particles[slot];
            p.PosX = cx + sign * state.SpinAxisX * offset;
            p.PosY = cy + sign * state.SpinAxisY * offset;
            p.PosZ = cz + sign * state.SpinAxisZ * offset;
            p.VelX = sign * state.SpinAxisX * jetSpeed;
            p.VelY = sign * state.SpinAxisY * jetSpeed;
            p.VelZ = sign * state.SpinAxisZ * jetSpeed;
            p.Temperature = 5e7; // Very hot (X-ray)
            p.Age = 0.0;
            p.MaxAge = 2.0; // Short-lived
            p.ParentBodyIndex = parentIndex;
            p.IsActive = true;
            p.OrbitalRadius = 0.0;
        }
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            if (!_particles[i].IsActive) return i;
        }
        return -1; // All slots occupied
    }

    private static void GetDiskPlaneVectors(
        double sx, double sy, double sz,
        out double e1x, out double e1y, out double e1z,
        out double e2x, out double e2y, out double e2z)
    {
        // Find a vector not parallel to spin axis
        double ax, ay, az;
        if (System.Math.Abs(sx) < 0.9)
        {
            ax = 1; ay = 0; az = 0;
        }
        else
        {
            ax = 0; ay = 1; az = 0;
        }

        // e1 = normalize(a × spin)
        e1x = ay * sz - az * sy;
        e1y = az * sx - ax * sz;
        e1z = ax * sy - ay * sx;
        double mag = System.Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z);
        if (mag > 1e-15) { e1x /= mag; e1y /= mag; e1z /= mag; }

        // e2 = spin × e1
        e2x = sy * e1z - sz * e1y;
        e2y = sz * e1x - sx * e1z;
        e2z = sx * e1y - sy * e1x;
        mag = System.Math.Sqrt(e2x * e2x + e2y * e2y + e2z * e2z);
        if (mag > 1e-15) { e2x /= mag; e2y /= mag; e2z /= mag; }
    }

    /// <summary>Reset all particles and accretion states.</summary>
    public void Reset()
    {
        for (int i = 0; i < _particles.Length; i++)
            _particles[i].IsActive = false;
        _activeCount = 0;
        _accretionStates.Clear();
    }
}
