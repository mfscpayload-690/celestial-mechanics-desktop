using CelestialMechanics.Physics.Types;
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

    // Public read-only access
    public EngineState State => _state;
    public SimulationState CurrentState => _currentState;
    public SimulationState PreviousState => _previousState;
    public double InterpolationAlpha { get; private set; }
    public PhysicsBody[] Bodies => _bodies;
    public double CurrentTime { get; private set; }
    public PhysicsConfig Config => _config;

    public SimulationEngine(PhysicsConfig? config = null)
    {
        _config = config ?? new PhysicsConfig();
        _fixedDt = _config.TimeStep;
        _bodies = Array.Empty<PhysicsBody>();
        _solver = new NBodySolver();
        // Add default Newtonian gravity and Verlet integrator
        _solver.AddForce(new NewtonianGravity
        {
            SofteningEpsilon = _config.SofteningEpsilon,
            RangeScale = _config.GravityRangeScale
        });
        _solver.SetIntegrator(new VerletIntegrator());

        // Wire SoA path from config. SoA is only available for the Verlet
        // integrator; Euler and RK4 fall back to the AoS path automatically
        // (see SetIntegrator below).
        _solver.ConfigureSoA(
            enabled:                  _config.UseSoAPath,
            softening:                _config.SofteningEpsilon,
            deterministic:            _config.DeterministicMode,
            useParallel:              _config.UseParallelComputation,
            useBarnesHut:             _config.UseBarnesHut,
            theta:                    _config.Theta,
            enableCollisions:         _config.EnableCollisions,
            useSimd:                  _config.UseSimd,
            enablePostNewtonian:      _config.EnablePostNewtonian,
            enableAccretionDisks:     _config.EnableAccretionDisks,
            enableGravitationalWaves: _config.EnableGravitationalWaves,
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
        _solver.Reset();
    }

    public void StepOnce()
    {
        // Single physics step (for step mode)
        double dt = ComputeDt();
        _previousState = _currentState;
        _currentState = _solver.Step(_bodies, dt);
        CurrentTime += dt;
    }

    public void Update(double frameTime)
    {
        if (_state != EngineState.Running) return;

        _accumulator += frameTime;

        double dt = ComputeDt();
        while (_accumulator >= dt)
        {
            _previousState = _currentState;
            _currentState = _solver.Step(_bodies, dt);
            _accumulator -= dt;
            CurrentTime += dt;

            // Recompute dt after each step (accelerations may have changed)
            if (_config.UseAdaptiveTimestep && !_config.DeterministicMode)
                dt = ComputeDt();
        }

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
            return _fixedDt;

        // Find maximum acceleration magnitude across all active bodies
        double maxAcc2 = 0.0;
        for (int i = 0; i < _bodies.Length; i++)
        {
            if (!_bodies[i].IsActive) continue;
            ref var acc = ref _bodies[i].Acceleration;
            double a2 = acc.X * acc.X + acc.Y * acc.Y + acc.Z * acc.Z;
            if (a2 > maxAcc2) maxAcc2 = a2;
        }

        if (maxAcc2 < 1e-30)
            return _config.MaxDt;

        // Safety factor η = 0.1 (conservative; dt = 0.1 / √max_acc)
        const double eta = 0.1;
        double dtAdaptive = eta / System.Math.Sqrt(System.Math.Sqrt(maxAcc2));

        return System.Math.Clamp(dtAdaptive, _config.MinDt, _config.MaxDt);
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

        // SoA Verlet is only available for the symplectic Verlet integrator.
        // Euler and RK4 fall back to the AoS path automatically; switching to
        // Verlet re-enables whatever the config requested.
        bool soaCapable = name == "Verlet";
        _solver.ConfigureSoA(
            enabled:                  soaCapable && _config.UseSoAPath,
            softening:                _config.SofteningEpsilon,
            deterministic:            _config.DeterministicMode,
            useParallel:              _config.UseParallelComputation,
            useBarnesHut:             _config.UseBarnesHut,
            theta:                    _config.Theta,
            enableCollisions:         _config.EnableCollisions,
            useSimd:                  _config.UseSimd,
            enablePostNewtonian:      _config.EnablePostNewtonian,
            enableAccretionDisks:     _config.EnableAccretionDisks,
            enableGravitationalWaves: _config.EnableGravitationalWaves,
            maxAccretionParticles:    _config.MaxAccretionParticles,
            enableJets:               _config.EnableJetEmission,
            jetThreshold:             _config.AccretionJetThreshold,
            gwObserverDistance:       _config.GravitationalWaveObserverDistance
        );
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
}
