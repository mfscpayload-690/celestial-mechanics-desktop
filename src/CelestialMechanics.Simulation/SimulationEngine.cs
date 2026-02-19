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
    private SimulationState _currentState;
    private SimulationState _previousState;
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
        _previousState = _currentState;
        _currentState = _solver.Step(_bodies, _fixedDt);
        CurrentTime += _fixedDt;
    }

    public void Update(double frameTime)
    {
        if (_state != EngineState.Running) return;

        _accumulator += frameTime;

        while (_accumulator >= _fixedDt)
        {
            _previousState = _currentState;
            _currentState = _solver.Step(_bodies, _fixedDt);
            _accumulator -= _fixedDt;
            CurrentTime += _fixedDt;
        }

        InterpolationAlpha = _accumulator / _fixedDt;
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
