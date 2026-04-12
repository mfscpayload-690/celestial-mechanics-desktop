using CelestialMechanics.Math;
using CelestialMechanics.Physics.Collisions;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// O(n²) pairwise N-body solver. Supports two execution paths:
///
/// AoS PATH (legacy, backward-compatible)
/// ----------------------------------------
/// Delegates integration to <see cref="IIntegrator"/> and force computation to
/// registered <see cref="IForceCalculator"/> instances. Used by existing tests
/// and by the Euler / RK4 integrators which have no SoA equivalents.
///
/// SoA PATH (high-performance)
/// ---------------------------
/// Uses a <see cref="ISoAIntegrator"/> backed by an <see cref="IPhysicsComputeBackend"/>
/// to integrate directly on the cache-efficient <see cref="BodySoA"/> buffer.
/// Enabled via <see cref="ConfigureSoA"/>.
///
/// Backend selection:
///   UseBarnesHut = true  → <see cref="BarnesHutBackend"/> (O(n log n))
///   DeterministicMode = true  → <see cref="CpuSingleThreadBackend"/> (reproducible)
///   DeterministicMode = false + UseParallelComputation = true
///                             → <see cref="CpuParallelBackend"/> (max throughput)
///
/// Energy and momentum diagnostics are computed from the AoS <c>PhysicsBody[]</c>
/// array; the SoA path writes back via <see cref="BodySoA.CopyTo"/> before
/// diagnostics run — O(n) overhead vs. the O(n²) hot path.
/// </summary>
public class NBodySolver
{
    // ── AoS path ───────────────────────────────────────────────────────────────
    private readonly List<IForceCalculator> _forces = new();
    private IIntegrator _integrator;
    private IForceCalculator[]? _forcesCache;  // avoids per-step ToArray() alloc

    // ── SoA path ───────────────────────────────────────────────────────────────
    private BodySoA? _soaBodies;
    private ISoAIntegrator _soaIntegrator;
    private readonly IPhysicsComputeBackend _singleThreadBackend;
    private readonly IPhysicsComputeBackend _parallelBackend;
    private bool _useSoA;
    private bool _deterministicMode;
    private bool _useParallel;
    private double _softening;

    // ── Barnes-Hut path (Phase 3) ──────────────────────────────────────────────
    private BarnesHutBackend? _barnesHutSingleBackend;
    private BarnesHutBackend? _barnesHutParallelBackend;
    private bool _useBarnesHut;
    private double _theta = 0.5;

    // ── Collision system (Phase 4) ─────────────────────────────────────────────
    private readonly CollisionDetector _collisionDetector = new();
    private readonly CollisionResolver _collisionResolver = new();
    private bool _enableCollisions;
    private CollisionMode _collisionMode = CollisionMode.MergeOnly;
    private double _collisionRestitution = 0.15;
    private double _fragmentationSpecificEnergyThreshold = 0.6;
    private double _fragmentationMassLossCap = 0.3;
    private double _captureVelocityFactor = 0.9;
    private bool _enableCollisionBroadPhase = true;
    private int _collisionBroadPhaseThreshold = 96;

    // ── SIMD (Phase 5) ─────────────────────────────────────────────────────
    private readonly SimdSingleThreadBackend _simdBackend = new();
    private bool _useSimd;
    private bool _enableShellTheorem;

    // ── Post-Newtonian (Phase 6A) ──────────────────────────────────────────
    private readonly PostNewtonian1Correction _pnCorrection = new();
    private bool _enablePostNewtonian;

    // ── Accretion Disk (Phase 6C) ─────────────────────────────────────────
    private AccretionDiskSystem? _accretionDisk;
    private bool _enableAccretionDisks;

    // ── Gravitational Waves (Phase 6D) ───────────────────────────────────
    private GravitationalWaveAnalyzer? _gwAnalyzer;
    private bool _enableGravitationalWaves;

    // ── Shared ─────────────────────────────────────────────────────────────────
    private readonly EnergyCalculator _energy = new();
    public IIntegrator CurrentIntegrator => _integrator;
    public string LastBackendName { get; private set; } = "AoS:VerletIntegrator";

    public ReadOnlySpan<DiskParticle> GetAccretionParticles()
    {
        if (_accretionDisk == null)
            return ReadOnlySpan<DiskParticle>.Empty;

        return _accretionDisk.Particles;
    }

    public int GetActiveAccretionParticleCount()
    {
        return _accretionDisk?.ActiveCount ?? 0;
    }

    private double _currentTime;
    private double _initialEnergy;
    private bool _initialEnergySet;

    public NBodySolver()
    {
        _integrator          = new VerletIntegrator();
        _soaIntegrator       = new SoAVerletIntegrator();
        _singleThreadBackend = new CpuSingleThreadBackend();
        _parallelBackend     = new CpuParallelBackend();

        // SoA disabled by default so existing tests continue to work unchanged.
        _useSoA            = false;
        _deterministicMode = true;
        _useParallel       = false;
        _useBarnesHut      = false;
        _enableCollisions    = false;
        _useSimd             = false;
        _enableShellTheorem  = false;
        _enablePostNewtonian = false;
        _enableAccretionDisks = false;
        _enableGravitationalWaves = false;
        _softening           = 1e-4;

        _currentTime      = 0.0;
        _initialEnergy    = 0.0;
        _initialEnergySet = false;
    }

    // ── Configuration ──────────────────────────────────────────────────────────

    public void AddForce(IForceCalculator force)
    {
        if (force is NewtonianGravity ng)
            ng.EnableShellTheorem = _enableShellTheorem;

        _forces.Add(force);
        _forcesCache = null;
    }

    public void SetIntegrator(IIntegrator integrator)
    {
        _integrator = integrator;
    }

    /// <summary>
    /// Enable or reconfigure the SoA execution path.
    /// </summary>
    /// <param name="enabled">true to use SoA Verlet + backend.</param>
    /// <param name="softening">Gravitational softening ε (simulation length units).</param>
    /// <param name="deterministic">
    ///   true  → single-thread backend, bit-reproducible across runs.<br/>
    ///   false → respects <paramref name="useParallel"/>.
    /// </param>
    /// <param name="useParallel">
    ///   When <paramref name="deterministic"/> is false, true selects
    ///   <see cref="CpuParallelBackend"/> (<c>Parallel.For</c> over bodies).
    /// </param>
    /// <param name="useBarnesHut">
    ///   When true, selects the Barnes-Hut O(n log n) backend instead of
    ///   the O(n²) brute-force backend.
    /// </param>
    /// <param name="theta">
    ///   Barnes-Hut opening angle parameter θ. Only used when
    ///   <paramref name="useBarnesHut"/> is true. Default 0.5.
    /// </param>
    public void ConfigureSoA(bool enabled, double softening,
                             bool deterministic = true, bool useParallel = false,
                             bool useBarnesHut = false, double theta = 0.5,
                             bool enableCollisions = false, bool useSimd = false,
                             bool enableShellTheorem = false,
                             CollisionMode collisionMode = CollisionMode.MergeOnly,
                             double collisionRestitution = 0.15,
                             double fragmentationSpecificEnergyThreshold = 0.6,
                             double fragmentationMassLossCap = 0.3,
                             double captureVelocityFactor = 0.9,
                             bool enableCollisionBroadPhase = true,
                             int collisionBroadPhaseThreshold = 96,
                             bool enablePostNewtonian = false,
                             bool enableAccretionDisks = false,
                             bool enableGravitationalWaves = false,
                             int maxAccretionParticles = 5000,
                             bool enableJets = false,
                             double jetThreshold = 0.1,
                             double gwObserverDistance = 1000.0)
    {
        _useSoA                   = enabled;
        _softening                = softening;
        _deterministicMode        = deterministic;
        _useParallel              = useParallel;
        _useBarnesHut             = useBarnesHut;
        _theta                    = theta;
        _enableCollisions         = enableCollisions;
        _useSimd                  = useSimd;
        _enableShellTheorem       = enableShellTheorem;
        _collisionMode            = collisionMode;
        _collisionRestitution     = collisionRestitution;
        _fragmentationSpecificEnergyThreshold = fragmentationSpecificEnergyThreshold;
        _fragmentationMassLossCap = fragmentationMassLossCap;
        _captureVelocityFactor    = captureVelocityFactor;
        _enableCollisionBroadPhase = enableCollisionBroadPhase;
        _collisionBroadPhaseThreshold = collisionBroadPhaseThreshold;
        _enablePostNewtonian      = enablePostNewtonian;
        _enableAccretionDisks     = enableAccretionDisks;
        _enableGravitationalWaves = enableGravitationalWaves;

        _collisionDetector.Configure(_enableCollisionBroadPhase, _collisionBroadPhaseThreshold);
        _collisionResolver.Configure(
            _collisionMode,
            _collisionRestitution,
            _fragmentationSpecificEnergyThreshold,
            _fragmentationMassLossCap,
            _captureVelocityFactor);

        for (int i = 0; i < _forces.Count; i++)
        {
            if (_forces[i] is NewtonianGravity ng)
                ng.EnableShellTheorem = _enableShellTheorem;
        }

        // Lazily create accretion disk system
        if (enableAccretionDisks && _accretionDisk == null)
        {
            _accretionDisk = new AccretionDiskSystem(maxAccretionParticles);
            _accretionDisk.EnableJets = enableJets;
            _accretionDisk.JetThreshold = jetThreshold;
        }
        else if (_accretionDisk != null)
        {
            if (enableAccretionDisks && _accretionDisk.MaxParticles != maxAccretionParticles)
            {
                _accretionDisk = new AccretionDiskSystem(maxAccretionParticles);
            }

            _accretionDisk.EnableJets = enableJets;
            _accretionDisk.JetThreshold = jetThreshold;
        }

        // Lazily create GW analyzer
        if (enableGravitationalWaves && _gwAnalyzer == null)
        {
            _gwAnalyzer = new GravitationalWaveAnalyzer();
            _gwAnalyzer.ObserverDistance = gwObserverDistance;
        }
        else if (_gwAnalyzer != null)
        {
            _gwAnalyzer.ObserverDistance = gwObserverDistance;
        }

        // Lazily create Barnes-Hut backends only when needed
        if (useBarnesHut)
        {
            if (_barnesHutSingleBackend == null)
            {
                _barnesHutSingleBackend = new BarnesHutBackend
                {
                    Theta = theta,
                    UseParallel = false
                };
            }
            else
            {
                _barnesHutSingleBackend.Theta = theta;
            }

            if (_barnesHutParallelBackend == null)
            {
                _barnesHutParallelBackend = new BarnesHutBackend
                {
                    Theta = theta,
                    UseParallel = true
                };
            }
            else
            {
                _barnesHutParallelBackend.Theta = theta;
            }
        }
    }

    // ── Step ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance the simulation by one timestep, then return an immutable
    /// diagnostics snapshot.
    /// </summary>
    public SimulationState Step(PhysicsBody[] bodies, double dt)
    {
        if (_useSoA)
            StepSoA(bodies, dt);
        else
            StepAoS(bodies, dt);

        // ── Accretion disk update (Phase 6C) ───────────────────────────────
        if (_enableAccretionDisks && _accretionDisk != null)
            _accretionDisk.Update(bodies, dt, _currentTime);

        _currentTime += dt;

        IForceCalculator[] fc = GetForcesCache();
        double ke          = _energy.ComputeKE(bodies);
        double pe          = _energy.ComputePE(bodies, fc);
        double totalEnergy = ke + pe;
        Vec3d  momentum    = _energy.ComputeMomentum(bodies);

        if (!_initialEnergySet)
        {
            _initialEnergy    = totalEnergy;
            _initialEnergySet = true;
        }

        double energyDrift = System.Math.Abs(_initialEnergy) > 1e-15
            ? (totalEnergy - _initialEnergy) / System.Math.Abs(_initialEnergy)
            : 0.0;

        int activeCount = 0;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i].IsActive) activeCount++;

        return new SimulationState
        {
            Time             = _currentTime,
            BodyCount        = bodies.Length,
            ActiveBodyCount  = activeCount,
            KineticEnergy    = ke,
            PotentialEnergy  = pe,
            TotalMomentum    = momentum,
            EnergyDrift      = energyDrift,
            CollisionCount   = _collisionResolver.LastCollisionCount,
            CollisionBursts  = _collisionResolver.LastBurstEvents.Count > 0
                ? _collisionResolver.LastBurstEvents.ToArray()
                : Array.Empty<CollisionBurstEvent>(),
            CurrentDt        = dt
        };
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void StepSoA(PhysicsBody[] bodies, double dt)
    {
        if (_soaBodies == null || _soaBodies.Capacity < bodies.Length)
            _soaBodies = new BodySoA(NextPowerOfTwo(System.Math.Max(bodies.Length, 16)));

        _soaBodies.CopyFrom(bodies);
        var backend = SelectBackend();
        LastBackendName = $"SoA:{backend.GetType().Name}";
        _soaIntegrator.Step(_soaBodies, backend, _softening, dt);

        // ── Collision detection and resolution (after integration) ────────
        if (_enableCollisions)
        {
            var events = _collisionDetector.Detect(_soaBodies);

            // Always resolve once per step so per-step collision counters and
            // burst buffers are reset even when there are zero contacts.
            _collisionResolver.Resolve(
                events,
                _soaBodies,
                bodies,
                dt,
                _currentTime,
                _enableAccretionDisks ? _accretionDisk : null,
                promoteCompactRemnants: _enableAccretionDisks);
        }

        _soaBodies.CopyTo(bodies);

        // ── Gravitational wave sampling (Phase 6D) ─────────────────────────
        if (_enableGravitationalWaves && _gwAnalyzer != null)
            _gwAnalyzer.Sample(_soaBodies, _currentTime, dt);
    }

    private void StepAoS(PhysicsBody[] bodies, double dt)
    {
        LastBackendName = $"AoS:{_integrator.GetType().Name}";
        _integrator.Step(bodies, dt, GetForcesCache());
    }

    /// <summary>
    /// Select the appropriate force computation backend based on configuration.
    ///
    /// Priority:
    ///   1. UseBarnesHut = true → Barnes-Hut backend (single or parallel)
    ///   2. DeterministicMode = true → CpuSingleThreadBackend
    ///   3. UseParallel = true → CpuParallelBackend
    ///   4. Default → CpuSingleThreadBackend
    /// </summary>
    private IPhysicsComputeBackend SelectBackend()
    {
        IPhysicsComputeBackend backend;

        if (_useBarnesHut)
        {
            // Deterministic mode trumps parallel flag
            if (_deterministicMode)
                backend = _barnesHutSingleBackend!;
            else
                backend = _useParallel ? _barnesHutParallelBackend! : _barnesHutSingleBackend!;
        }
        else if (_useSimd)
        {
            backend = _simdBackend;
        }
        else if (_deterministicMode)
        {
            backend = _singleThreadBackend;
        }
        else
        {
            backend = _useParallel ? _parallelBackend : _singleThreadBackend;
        }

        if (backend is IGravityModelAwareBackend gravityAware)
            gravityAware.EnableShellTheorem = _enableShellTheorem;

        // Wrap with Post-Newtonian corrections if enabled (Phase 6A)
        if (_enablePostNewtonian)
            return new PostNewtonianBackend(backend, _pnCorrection);

        return backend;
    }

    private IForceCalculator[] GetForcesCache()
    {
        return _forcesCache ??= _forces.ToArray();
    }

    private static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>Reset time and initial-energy reference.</summary>
    public void Reset()
    {
        _currentTime      = 0.0;
        _initialEnergy    = 0.0;
        _initialEnergySet = false;
    }
}
