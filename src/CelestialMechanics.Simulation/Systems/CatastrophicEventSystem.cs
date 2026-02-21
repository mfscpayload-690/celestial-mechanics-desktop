using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;
using CelestialMechanics.Simulation.PhysicsExtensions;

namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Central orchestrator for catastrophic astrophysical events.
///
/// Coordinates:
///   1. Supernova detection and physics (core collapse → ejecta shell → remnant)
///   2. Ejecta pooling (pre-allocated, zero per-frame heap allocation)
///   3. Shockwave creation (delegates to ShockwaveSystem)
///   4. Remnant formation (delegates to RemnantFormationSystem)
///   5. Gravitational wave burst emission
///   6. Energy budget tracking
///
/// Supernova pipeline per qualifying star:
///   a. Freeze core mass at collapse time
///   b. Compute binding energy: E_bind ≈ 3·G·M²/(5·R)
///   c. Explosion energy: E_explosion = fraction × M·c² (simplified)
///   d. Remnant mass: core mass (clamped to TOV decision)
///   e. Ejecta mass: progenitor mass − remnant mass
///   f. Spawn ejecta shell via pool (Fibonacci-sphere directions)
///   g. Momentum-conserving remnant velocity correction
///   h. Create shockwave at explosion site
///   i. Form compact remnant (NS or BH) via RemnantFormationSystem
///   j. Emit GW burst spike
///
/// Constraints:
///   - No per-frame heap allocations (ejecta pool is pre-allocated)
///   - All velocities clamped to 0.3c
///   - NaN/Infinity inputs produce Vec3d.Zero
///   - Deterministic evaluation order (entity index order)
/// </summary>
public sealed class CatastrophicEventSystem
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>Fraction of rest-mass energy converted to explosion kinetic energy.</summary>
    public double ExplosionEnergyFraction { get; set; } = 1e-4;

    /// <summary>Maximum ejecta particles per supernova from the pool.</summary>
    public int MaxEjectaPerEvent { get; set; } = 100;

    /// <summary>Minimum offset distance for ejecta spawn (prevents zero-separation).</summary>
    public double MinEjectaOffset { get; set; } = 0.01;

    /// <summary>Speed variation factor for ejecta (0 = uniform, 1 = full range).</summary>
    public double EjectaSpeedVariation { get; set; } = 0.5;

    /// <summary>Lifetime of ejecta debris before deactivation (sim time units).</summary>
    public double EjectaLifetime { get; set; } = 100.0;

    /// <summary>GW burst prefactor: h ∝ prefactor · (mass · energy^0.5) / observer_distance.</summary>
    private static readonly double GwBurstPrefactor = 4.0 / PhysicalConstants.C_Sim4;

    /// <summary>Observer distance for GW amplitude computation.</summary>
    public double GwObserverDistance { get; set; } = 1000.0;

    // ── Sub-systems ──────────────────────────────────────────────────────────

    public ShockwaveSystem Shockwaves { get; }
    public MergerResolutionSystem Mergers { get; }
    public EnergyBudgetTracker EnergyTracker { get; }

    /// <summary>Optional EventBus for publishing catastrophic event notifications.</summary>
    public EventBus? EventBus { get; set; }

    /// <summary>Callback for adding spawned entities (ejecta, remnants) to SimulationManager.</summary>
    public Action<Entity>? OnEntityAdded { get; set; }

    /// <summary>Callback for removing consumed entities from SimulationManager.</summary>
    public Action<Entity>? OnEntityRemoved { get; set; }

    // ── Ejecta pool ──────────────────────────────────────────────────────────

    private readonly Entity[] _ejectaPool;
    private readonly bool[] _ejectaInUse;
    private int _poolCapacity;

    /// <summary>Total pool capacity for ejecta entities.</summary>
    public int EjectaPoolCapacity => _poolCapacity;

    /// <summary>Number of ejecta entities currently active.</summary>
    public int ActiveEjectaCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _poolCapacity; i++)
                if (_ejectaInUse[i]) count++;
            return count;
        }
    }

    // ── Event history ────────────────────────────────────────────────────────

    private readonly List<SupernovaEvent> _supernovaHistory = new();
    private readonly List<MergerEvent> _mergerHistory = new();
    private readonly List<CollapseEvent> _collapseHistory = new();

    public IReadOnlyList<SupernovaEvent> SupernovaHistory => _supernovaHistory;
    public IReadOnlyList<MergerEvent> MergerHistory => _mergerHistory;
    public IReadOnlyList<CollapseEvent> CollapseHistory => _collapseHistory;

    // ── Scratch buffers for ejecta velocity computation (pre-allocated) ──────

    private readonly double[] _scratchVx;
    private readonly double[] _scratchVy;
    private readonly double[] _scratchVz;

    private readonly Random _rng;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create the catastrophic event system with a pre-allocated ejecta pool.
    /// </summary>
    /// <param name="ejectaPoolSize">Maximum total concurrent ejecta entities.</param>
    /// <param name="shockwaveCapacity">Maximum concurrent shockwaves.</param>
    /// <param name="seed">RNG seed for deterministic ejecta generation.</param>
    public CatastrophicEventSystem(int ejectaPoolSize = 1024, int shockwaveCapacity = 64, int seed = 42)
    {
        _poolCapacity = ejectaPoolSize;
        _ejectaPool = new Entity[ejectaPoolSize];
        _ejectaInUse = new bool[ejectaPoolSize];
        _rng = new Random(seed);

        // Pre-allocate all ejecta entities with components
        for (int i = 0; i < ejectaPoolSize; i++)
        {
            var entity = new Entity();
            entity.Tag = "Ejecta";
            entity.IsActive = false;
            entity.AddComponent(new PhysicsComponent());
            entity.AddComponent(new ExplosionComponent { IsDebris = true, DebrisLifetime = EjectaLifetime });
            _ejectaPool[i] = entity;
            _ejectaInUse[i] = false;
        }

        // Pre-allocate scratch buffers for max ejecta per event
        _scratchVx = new double[MaxEjectaPerEvent > 0 ? MaxEjectaPerEvent : 100];
        _scratchVy = new double[MaxEjectaPerEvent > 0 ? MaxEjectaPerEvent : 100];
        _scratchVz = new double[MaxEjectaPerEvent > 0 ? MaxEjectaPerEvent : 100];

        Shockwaves = new ShockwaveSystem(shockwaveCapacity);
        Mergers = new MergerResolutionSystem();
        EnergyTracker = new EnergyBudgetTracker();
    }

    // ── Main processing entry point ─────────────────────────────────────────

    /// <summary>
    /// Process all catastrophic events for the current simulation step.
    /// Called after component updates and trigger evaluation.
    ///
    /// Pipeline:
    ///   1. Scan for supernova-eligible stars → process each
    ///   2. Recycle expired ejecta back to pool
    ///   3. Apply shockwave impulses
    ///   4. Process compact object mergers
    ///   5. Measure energy budget
    /// </summary>
    public void Process(IReadOnlyList<Entity> entities, double currentTime, double dt)
    {
        // 1. Detect and process supernovae
        ProcessSupernovae(entities, currentTime);

        // 2. Recycle expired ejecta
        RecycleExpiredEjecta();

        // 3. Shockwave propagation
        Shockwaves.Update(entities, currentTime, dt);

        // 4. Compact object mergers
        Mergers.EventBus = EventBus;
        Mergers.OnEntityRemoved = OnEntityRemoved;
        Mergers.OnEntityAdded = OnEntityAdded;
        var mergerEvents = Mergers.Process(entities, currentTime);
        for (int i = 0; i < mergerEvents.Count; i++)
        {
            var me = mergerEvents[i];
            _mergerHistory.Add(me);

            // Record GW energy loss from merger (simplified: E_gw ≈ η·M·c²)
            double mergerGwEnergy = 0.01 * me.RemnantMass * PhysicalConstants.C_Sim2;
            EnergyTracker.RecordGravitationalWaveLoss(mergerGwEnergy);
            EnergyTracker.RecordMergerLoss(mergerGwEnergy);
        }

        // 5. Measure energy budget
        EnergyTracker.Measure(entities);
    }

    // ── Supernova processing ─────────────────────────────────────────────────

    private void ProcessSupernovae(IReadOnlyList<Entity> entities, double currentTime)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.IsActive) continue;

            var stellar = entity.GetComponent<StellarEvolutionComponent>();
            if (stellar == null || !stellar.HasCollapsed) continue;

            var physics = entity.GetComponent<PhysicsComponent>();
            if (physics == null) continue;

            // Check if this entity has already been processed as a supernova
            // (has an ExplosionComponent that is already exploding)
            var explosion = entity.GetComponent<ExplosionComponent>();
            if (explosion != null && explosion.IsExploding) continue;

            // ── Execute supernova pipeline ──
            ExecuteSupernova(entity, physics, stellar, currentTime);
        }
    }

    private void ExecuteSupernova(Entity entity, PhysicsComponent physics,
        StellarEvolutionComponent stellar, double currentTime)
    {
        // a. Freeze core mass
        double coreMass = stellar.CoreMass;
        double progenitorMass = physics.Mass;
        Vec3d progenitorPosition = physics.Position;
        Vec3d progenitorVelocity = physics.Velocity;
        Vec3d originalMomentum = progenitorVelocity * progenitorMass;

        // b. Remnant mass = core mass (will be classified as NS or BH by RemnantFormationSystem)
        double remnantMass = coreMass;
        if (remnantMass <= 0.0 || remnantMass >= progenitorMass)
        {
            remnantMass = System.Math.Min(coreMass, progenitorMass * 0.5);
        }
        if (remnantMass <= 0.0) return;

        // c. Ejecta mass
        double ejectaMass = progenitorMass - remnantMass;
        if (ejectaMass <= 0.0) return;

        // d. Compute explosion energy: E = fraction × ejectaMass × c²
        double explosionEnergy = ExplosionEnergyFraction * ejectaMass * PhysicalConstants.C_Sim2;

        // e. Determine ejecta count (proportional to ejected mass, capped)
        int ejectaCount = System.Math.Min(MaxEjectaPerEvent,
            System.Math.Max(4, (int)(ejectaMass * 10)));

        // Ensure scratch buffers are large enough
        int scratchLen = _scratchVx.Length;
        if (ejectaCount > scratchLen) ejectaCount = scratchLen;

        double massPerEjecta = ejectaMass / ejectaCount;

        // f. Compute ejecta velocities using Fibonacci sphere + energy conservation
        MomentumConservationUtility.ComputeEjectaVelocities(
            ejectaCount, explosionEnergy, ejectaMass,
            progenitorVelocity, EjectaSpeedVariation,
            _scratchVx, _scratchVy, _scratchVz, _rng);

        // g. Spawn ejecta from pool
        Vec3d totalEjectaMomentum = Vec3d.Zero;
        int spawnedCount = 0;

        for (int i = 0; i < ejectaCount; i++)
        {
            int slot = AcquireEjectaSlot();
            if (slot < 0) break; // Pool exhausted

            var ejecta = _ejectaPool[slot];
            ejecta.IsActive = true;
            ejecta.Tag = "Ejecta";

            // Fibonacci sphere direction for offset
            double phi = System.Math.Acos(1.0 - 2.0 * (i + 0.5) / ejectaCount);
            double theta = System.Math.PI * (1.0 + System.Math.Sqrt(5.0)) * i;
            double sinPhi = System.Math.Sin(phi);
            double dx = sinPhi * System.Math.Cos(theta);
            double dy = sinPhi * System.Math.Sin(theta);
            double dz = System.Math.Cos(phi);

            Vec3d ejectaVel = new Vec3d(_scratchVx[i], _scratchVy[i], _scratchVz[i]);
            ejectaVel = MomentumConservationUtility.ClampVelocity(ejectaVel);

            Vec3d ejectaPos = progenitorPosition + new Vec3d(
                dx * MinEjectaOffset,
                dy * MinEjectaOffset,
                dz * MinEjectaOffset);

            // Update existing components in-place (no allocation)
            var pc = ejecta.GetComponent<PhysicsComponent>()!;
            pc.Mass = massPerEjecta;
            pc.Position = ejectaPos;
            pc.Velocity = ejectaVel;
            pc.Radius = 0.001;
            pc.Density = 1.0;
            pc.IsCollidable = false;

            var ec = ejecta.GetComponent<ExplosionComponent>()!;
            ec.IsDebris = true;
            ec.IsExploding = false;
            ec.TimeSinceExplosion = 0.0;
            ec.DebrisLifetime = EjectaLifetime;

            totalEjectaMomentum += ejectaVel * massPerEjecta;
            spawnedCount++;

            OnEntityAdded?.Invoke(ejecta);
        }

        // h. Momentum-conserving remnant velocity
        Vec3d remnantVelocity = MomentumConservationUtility.ComputeRemnantVelocity(
            originalMomentum, totalEjectaMomentum, remnantMass);

        // i. Update progenitor → remnant in-place
        physics.Mass = remnantMass;
        physics.Velocity = remnantVelocity;

        // j. Form compact remnant (NS or BH)
        var collapseEvent = RemnantFormationSystem.FormRemnant(entity, remnantMass, currentTime);
        if (collapseEvent != null)
            _collapseHistory.Add(collapseEvent);

        // k. Mark as exploding
        var explosionComp = entity.GetComponent<ExplosionComponent>();
        if (explosionComp == null)
        {
            explosionComp = new ExplosionComponent();
            entity.AddComponent(explosionComp);
        }
        explosionComp.IsExploding = true;
        explosionComp.TimeSinceExplosion = 0.0;

        // l. Create shockwave at explosion site
        Shockwaves.CreateShockwave(progenitorPosition, explosionEnergy, currentTime);

        // m. GW burst amplitude
        double gwAmplitude = 0.0;
        if (GwObserverDistance > 0.0)
        {
            // h ∝ (mass × v²) / (c⁴ × d_observer), simplified quadrupole
            gwAmplitude = GwBurstPrefactor * remnantMass * explosionEnergy /
                (GwObserverDistance * GwObserverDistance);
        }

        // n. Record energy injection
        EnergyTracker.RecordExplosionEnergy(explosionEnergy);

        // o. Record GW energy loss (small fraction of explosion energy)
        double gwEnergyLoss = 1e-6 * explosionEnergy;
        EnergyTracker.RecordGravitationalWaveLoss(gwEnergyLoss);

        // p. Build supernova event record
        bool formedBlackHole = remnantMass >= RemnantFormationSystem.TovLimit;
        var snEvent = new SupernovaEvent
        {
            ProgenitorId = entity.Id,
            Position = progenitorPosition,
            ProgenitorMass = progenitorMass,
            CoreMass = coreMass,
            EjectaMass = ejectaMass,
            RemnantMass = remnantMass,
            ExplosionEnergy = explosionEnergy,
            EjectaCount = spawnedCount,
            Time = currentTime,
            FormedBlackHole = formedBlackHole
        };
        _supernovaHistory.Add(snEvent);

        // q. Publish on EventBus
        EventBus?.Publish(new SimulationEvent
        {
            Type = "Supernova",
            Message = $"SN: {progenitorMass:F2}→{remnantMass:F2} M☉ " +
                      $"{(formedBlackHole ? "BH" : "NS")}, " +
                      $"ejecta={spawnedCount}, E={explosionEnergy:E2}, GW h={gwAmplitude:E2}",
            Time = currentTime
        });
    }

    // ── Ejecta pool management ───────────────────────────────────────────────

    /// <summary>
    /// Acquire a free ejecta slot from the pool.
    /// Returns the slot index, or -1 if pool is exhausted.
    /// </summary>
    private int AcquireEjectaSlot()
    {
        for (int i = 0; i < _poolCapacity; i++)
        {
            if (!_ejectaInUse[i])
            {
                _ejectaInUse[i] = true;
                return i;
            }
        }
        return -1; // Pool exhausted
    }

    /// <summary>
    /// Return an ejecta entity to the pool for reuse.
    /// </summary>
    private void ReleaseEjectaSlot(int index)
    {
        if (index < 0 || index >= _poolCapacity) return;
        _ejectaPool[index].IsActive = false;
        _ejectaInUse[index] = false;
        OnEntityRemoved?.Invoke(_ejectaPool[index]);
    }

    /// <summary>
    /// Scan all active ejecta and recycle those past their debris lifetime.
    /// </summary>
    private void RecycleExpiredEjecta()
    {
        for (int i = 0; i < _poolCapacity; i++)
        {
            if (!_ejectaInUse[i]) continue;

            var ec = _ejectaPool[i].GetComponent<ExplosionComponent>();
            if (ec == null) continue;

            if (ec.TimeSinceExplosion >= ec.DebrisLifetime)
            {
                ReleaseEjectaSlot(i);
            }
        }
    }

    /// <summary>
    /// Get the ejecta entity at the specified pool index (for testing/inspection).
    /// </summary>
    public Entity? GetEjectaEntity(int poolIndex)
    {
        if (poolIndex < 0 || poolIndex >= _poolCapacity) return null;
        return _ejectaPool[poolIndex];
    }

    /// <summary>
    /// Check if a specific pool slot is in use.
    /// </summary>
    public bool IsEjectaSlotInUse(int poolIndex)
    {
        if (poolIndex < 0 || poolIndex >= _poolCapacity) return false;
        return _ejectaInUse[poolIndex];
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    /// <summary>Reset all subsystems and return all ejecta to pool.</summary>
    public void Reset()
    {
        for (int i = 0; i < _poolCapacity; i++)
        {
            _ejectaPool[i].IsActive = false;
            _ejectaInUse[i] = false;
        }

        Shockwaves.Reset();
        EnergyTracker.Reset();
        _supernovaHistory.Clear();
        _mergerHistory.Clear();
        _collapseHistory.Clear();
    }
}
