using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Astrophysics;
using CelestialMechanics.Physics.Extensions;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;

namespace CelestialMechanics.Simulation;

public enum EngineState { Stopped, Running, Paused }

public class SimulationEngine
{
    private PhysicsBody[] _bodies;
    private NBodySolver _solver;
    private double _accumulator;
    private double _fixedDt;
    private SimulationState _currentState = new();
    private SimulationState _previousState = new();
    private EngineState _state = EngineState.Stopped;
    private PhysicsConfig _config;
    private double _lastAdaptiveDt;

    // Public read-only access
    public EngineState State => _state;
    public SimulationState CurrentState => _currentState;
    public SimulationState PreviousState => _previousState;
    public double InterpolationAlpha { get; private set; }
    public PhysicsBody[] Bodies => _bodies;
    public int ActiveAccretionParticleCount => _solver.GetActiveAccretionParticleCount();
    public double CurrentTime { get; private set; }
    public PhysicsConfig Config => _config;
    public string LastSolverBackend => _solver.LastBackendName;

    public ReadOnlySpan<DiskParticle> GetAccretionParticles()
    {
        return _solver.GetAccretionParticles();
    }

    public SimulationEngine(PhysicsConfig? config = null)
    {
        _config = config ?? new PhysicsConfig();
        _fixedDt = _config.TimeStep;
        _lastAdaptiveDt = _fixedDt;
        _bodies = Array.Empty<PhysicsBody>();
        _solver = new NBodySolver();
        // Add default Newtonian gravity and Verlet integrator
        _solver.AddForce(new NewtonianGravity
        {
            SofteningEpsilon = _config.SofteningEpsilon,
            RangeScale = _config.GravityRangeScale
        });
        _solver.SetIntegrator(new VerletIntegrator());

        Reconfigure();
    }

    public void Reconfigure()
    {
        _fixedDt = _config.TimeStep;
        bool soaCapable = _solver.CurrentIntegrator is VerletIntegrator;

        _solver.ConfigureSoA(
            enabled:                  soaCapable && _config.UseSoAPath,
            softening:                _config.SofteningEpsilon,
            deterministic:            _config.DeterministicMode,
            useParallel:              _config.UseParallelComputation,
            useBarnesHut:             _config.UseBarnesHut,
            theta:                    _config.Theta,
            enableCollisions:         _config.EnableCollisions,
            useSimd:                  _config.UseSimd,
            enableShellTheorem:       _config.EnableShellTheorem,
            collisionMode:            _config.CollisionMode,
            collisionRestitution:     _config.CollisionRestitution,
            fragmentationSpecificEnergyThreshold: _config.FragmentationSpecificEnergyThreshold,
            fragmentationMassLossCap: _config.FragmentationMassLossCap,
            captureVelocityFactor:    _config.CaptureVelocityFactor,
            enableCollisionBroadPhase: _config.EnableCollisionBroadPhase,
            collisionBroadPhaseThreshold: _config.CollisionBroadPhaseThreshold,
            enablePostNewtonian:      _config.EnablePostNewtonian,
            enableAccretionDisks:     _config.EnableAccretionDisks,
            enableGravitationalWaves: _config.EnableGravitationalWaves,
            enableBlackHolePhysics:   _config.EnableBlackHolePhysics,
            enableThermalRadiation:   _config.EnableThermalRadiation,
            maxAccretionParticles:    _config.MaxAccretionParticles,
            enableJets:               _config.EnableJetEmission,
            jetThreshold:             _config.AccretionJetThreshold,
            gwObserverDistance:       _config.GravitationalWaveObserverDistance
        );
    }

    public void SetBodies(PhysicsBody[] bodies)
    {
        _bodies = bodies;
    }

    public void AddBody(PhysicsBody body)
    {
        var list = new List<PhysicsBody>(_bodies) { body };
        _bodies = list.ToArray();
    }

    public void RemoveBody(int id)
    {
        _bodies = _bodies.Where(b => b.Id != id).ToArray();
    }

    public bool TriggerSupernova(int bodyId, int ejectaCount = 24)
    {
        int index = -1;
        for (int i = 0; i < _bodies.Length; i++)
        {
            if (_bodies[i].Id == bodyId && _bodies[i].IsActive)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        ref var progenitor = ref _bodies[index];
        if (progenitor.Type != BodyType.Star && progenitor.Type != BodyType.NeutronStar)
            return false;

        double originalMass = progenitor.Mass;
        double remnantMass = System.Math.Max(0.1, originalMass * 0.22);
        remnantMass = System.Math.Min(remnantMass, originalMass * 0.9);
        double ejectaMass = originalMass - remnantMass;

        if (ejectaMass <= 1e-10)
            return false;

        var list = new List<PhysicsBody>(_bodies.Length + System.Math.Max(8, ejectaCount));
        list.AddRange(_bodies);

        var remnant = list[index];
        remnant.Mass = remnantMass;
        remnant.Type = remnantMass >= 2.8 ? BodyType.BlackHole : BodyType.NeutronStar;
        remnant.Density = DensityModel.GetDefaultDensity(remnant.Type);
        remnant.Temperature = remnant.Type == BodyType.BlackHole ? 3.0e4 : 1.2e6;
        remnant.RecalculateRadius();

        double explosionFactor = remnant.Type == BodyType.BlackHole ? 1.8 : 4.0;
        var profile = StellarExplosionModel.Compute(
            progenitorMassSolar: originalMass,
            progenitorRadiusAu: System.Math.Max(progenitor.Radius, 1e-9),
            ejectaMassSolar: ejectaMass,
            k: explosionFactor,
            decayTauSeconds: 6.0 * 86400.0);
        remnant.Luminosity = remnant.Type == BodyType.BlackHole
            ? 0.0
            : profile.LuminosityAt(0.0) * 0.08;
        list[index] = remnant;

        int spawnCount = System.Math.Clamp(ejectaCount, 8, 96);
        int nextId = list.Count == 0 ? 0 : list.Max(b => b.Id) + 1;
        double massPerEjecta = ejectaMass / spawnCount;
        double launchSpeed = CelestialMechanics.Math.UnitConversion.VelocityToSim(profile.ExpansionVelocityMps);

        for (int i = 0; i < spawnCount; i++)
        {
            var dir = FibonacciDirection(i, spawnCount);
            double speedScale = 0.55 + 0.45 * ((i % 7) / 6.0);
            var velocity = progenitor.Velocity + dir * (launchSpeed * speedScale);
            var position = progenitor.Position + dir * System.Math.Max(progenitor.Radius * 0.45, 0.01);

            var ejecta = new PhysicsBody(nextId++, massPerEjecta, position, velocity, BodyType.Asteroid)
            {
                Radius = System.Math.Max(0.0025, progenitor.Radius * 0.08),
                GravityStrength = progenitor.GravityStrength,
                GravityRange = progenitor.GravityRange,
                Temperature = 2.5e6,
                HeatCapacity = 1.2e3,
                Luminosity = profile.LuminosityAt(0.0) / spawnCount,
                IsActive = true,
                IsCollidable = true
            };

            list.Add(ejecta);
        }

        _bodies = list.ToArray();
        return true;
    }

    public void Start()
    {
        _state = EngineState.Running;
    }

    public void Pause()
    {
        _state = EngineState.Paused;
    }

    public void Stop()
    {
        _state = EngineState.Stopped;
        Reset();
    }

    public void Reset()
    {
        _accumulator = 0;
        CurrentTime = 0;
        _lastAdaptiveDt = _fixedDt;
        _solver.Reset();
    }

    public void StepOnce()
    {
        if (_state == EngineState.Running)
            _state = EngineState.Paused;

        // Single physics step (for step mode)
        double dt = ComputeDt();

        int substeps = DetermineCollisionSubsteps(dt);
        double subDt = dt / substeps;

        for (int s = 0; s < substeps; s++)
        {
            _previousState = _currentState;
            _currentState = _solver.Step(_bodies, subDt);
            CurrentTime += subDt;
        }
    }

    public void Update(double frameTime)
    {
        if (_state != EngineState.Running) return;

        _accumulator += frameTime;

        double dt = ComputeDt();
        int maxSubsteps = System.Math.Max(1, _config.MaxSubstepsPerFrame);
        double substepBoost = System.Math.Max(1.0, _config.TimeFlowSubstepBoost);
        int dynamicSubstepBudget = System.Math.Clamp(
            (int)System.Math.Ceiling(maxSubsteps * substepBoost),
            maxSubsteps,
            1024);
        int substeps = 0;
        while (_accumulator >= dt && substeps < dynamicSubstepBudget)
        {
            int collisionSubsteps = DetermineCollisionSubsteps(dt);
            double subDt = dt / collisionSubsteps;

            for (int s = 0; s < collisionSubsteps; s++)
            {
                _previousState = _currentState;
                _currentState = _solver.Step(_bodies, subDt);
                CurrentTime += subDt;
            }

            _accumulator -= dt;
            substeps++;

            // Recompute dt after each step (accelerations may have changed)
            if (_config.UseAdaptiveTimestep && !_config.DeterministicMode)
                dt = ComputeDt();
        }

        // Prevent runaway catch-up loops from stalling rendering.
        if (_accumulator >= dt)
            _accumulator = System.Math.Min(_accumulator, dt * System.Math.Max(2.0, dynamicSubstepBudget));

        InterpolationAlpha = dt > 0 ? _accumulator / dt : 0.0;
    }

    /// <summary>
    /// Compute the effective timestep. In deterministic mode or when adaptive
    /// is disabled, returns the fixed dt. Otherwise uses an acceleration-based
    /// heuristic: dt = η / √(max_acc), clamped to [MinDt, MaxDt].
    /// </summary>
    private double ComputeDt()
    {
        if (_config.DeterministicMode || !_config.UseAdaptiveTimestep)
        {
            _lastAdaptiveDt = _fixedDt;
            return _fixedDt;
        }

        // Find maximum acceleration/speed and smallest active radius.
        // Compact remnants (especially black holes) can have extremely tiny physical
        // radii, which would otherwise force dt near MinDt and visually "freeze"
        // the scene in interactive mode.
        double maxAcc2 = 0.0;
        double maxSpeed = 0.0;
        double minRadiusAny = double.MaxValue;
        double minRadiusNonCompact = double.MaxValue;
        int activeCount = 0;

        for (int i = 0; i < _bodies.Length; i++)
        {
            if (!_bodies[i].IsActive) continue;
            activeCount++;

            ref var acc = ref _bodies[i].Acceleration;
            double a2 = acc.X * acc.X + acc.Y * acc.Y + acc.Z * acc.Z;
            if (a2 > maxAcc2) maxAcc2 = a2;

            double v = _bodies[i].Velocity.Length;
            if (v > maxSpeed) maxSpeed = v;

            double r = _bodies[i].Radius;
            if (r > 1e-12)
            {
                if (r < minRadiusAny)
                    minRadiusAny = r;

                bool isCompact = _bodies[i].Type == BodyType.BlackHole ||
                                 _bodies[i].Type == BodyType.NeutronStar;
                if (!isCompact && r < minRadiusNonCompact)
                    minRadiusNonCompact = r;
            }
        }

        if (activeCount == 0)
            return _config.MaxDt;

        double dtAcc = _config.MaxDt;
        if (maxAcc2 >= 1e-30)
        {
            // Safety factor η = 0.1 (conservative).
            const double eta = 0.1;
            double maxAcc = System.Math.Sqrt(maxAcc2);
            dtAcc = eta / System.Math.Sqrt(maxAcc);
        }

        double dtCollision = _config.MaxDt;
        if (_config.EnableCollisions &&
            maxSpeed > 1e-12 &&
            minRadiusAny < double.MaxValue)
        {
            // Prefer non-compact body scales for interactive collision limiting.
            double collisionRadius = minRadiusNonCompact < double.MaxValue
                ? minRadiusNonCompact
                : minRadiusAny;

            // Never let limiter resolution drop below softening scale.
            collisionRadius = System.Math.Max(collisionRadius, _config.SofteningEpsilon * 4.0);

            // Keep per-step travel to a fraction of the smallest radius.
            dtCollision = 0.3 * collisionRadius / maxSpeed;
        }

        double dtClamped = System.Math.Clamp(
            System.Math.Min(dtAcc, dtCollision),
            _config.MinDt,
            _config.MaxDt);

        // Damp abrupt dt oscillations between frames for smoother integration.
        if (_lastAdaptiveDt > 0.0)
        {
            double minStep = _lastAdaptiveDt * 0.5;
            double maxStep = _lastAdaptiveDt * 1.25;
            dtClamped = System.Math.Clamp(dtClamped, minStep, maxStep);
            dtClamped = System.Math.Clamp(dtClamped, _config.MinDt, _config.MaxDt);
        }

        _lastAdaptiveDt = dtClamped;
        return dtClamped;
    }

    private int DetermineCollisionSubsteps(double dt)
    {
        if (!_config.EnableCollisions ||
            !_config.EnableCollisionSubstepping ||
            dt <= 0.0)
        {
            return 1;
        }

        double maxSpeed = 0.0;
        double minRadiusAny = double.MaxValue;
        double minRadiusNonCompact = double.MaxValue;

        for (int i = 0; i < _bodies.Length; i++)
        {
            if (!_bodies[i].IsActive || !_bodies[i].IsCollidable)
                continue;

            double speed = _bodies[i].Velocity.Length;
            if (speed > maxSpeed) maxSpeed = speed;

            double r = _bodies[i].Radius;
            if (r > 1e-12 && r < minRadiusAny)
                minRadiusAny = r;

            bool isCompact = _bodies[i].Type == BodyType.BlackHole ||
                             _bodies[i].Type == BodyType.NeutronStar;
            if (!isCompact && r > 1e-12 && r < minRadiusNonCompact)
                minRadiusNonCompact = r;
        }

        if (maxSpeed <= 1e-12 || minRadiusAny == double.MaxValue)
            return 1;

        double radiusRef = minRadiusNonCompact < double.MaxValue
            ? minRadiusNonCompact
            : minRadiusAny;

        radiusRef = System.Math.Max(radiusRef, _config.SofteningEpsilon * 4.0);

        double maxTravelPerSubstep = radiusRef * System.Math.Clamp(_config.CollisionSubstepSafetyFactor, 0.05, 1.0);
        if (maxTravelPerSubstep <= 1e-12)
            return 1;

        double substepsNeeded = (maxSpeed * dt) / maxTravelPerSubstep;
        int steps = (int)System.Math.Ceiling(substepsNeeded);
        return System.Math.Clamp(steps, 1, System.Math.Max(1, _config.MaxCollisionSubsteps));
    }

    public void SetIntegrator(string name)
    {
        IIntegrator integrator = name switch
        {
            "Euler" => new EulerIntegrator(),
            "Verlet" => new VerletIntegrator(),
            "RK4" => new RK4Integrator(),
            _ => throw new ArgumentException($"Unknown integrator: {name}", nameof(name))
        };
        _solver.SetIntegrator(integrator);
        Reconfigure();
    }

    public string GetIntegratorName()
    {
        return _solver.CurrentIntegrator switch
        {
            EulerIntegrator => "Euler",
            VerletIntegrator => "Verlet",
            RK4Integrator => "RK4",
            _ => _solver.CurrentIntegrator?.GetType().Name ?? "None"
        };
    }

    private static CelestialMechanics.Math.Vec3d FibonacciDirection(int index, int count)
    {
        double goldenAngle = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        double y = 1.0 - ((index + 0.5) / count) * 2.0;
        double radius = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - y * y));
        double theta = goldenAngle * index;

        return new CelestialMechanics.Math.Vec3d(
            System.Math.Cos(theta) * radius,
            y,
            System.Math.Sin(theta) * radius);
    }
}
