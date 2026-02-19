using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// O(n^2) pairwise N-body solver. Delegates integration to an IIntegrator
/// and force computation to registered IForceCalculator instances.
/// Tracks energy drift relative to initial total energy.
/// </summary>
public class NBodySolver
{
    private readonly List<IForceCalculator> _forces = new();
    private IIntegrator _integrator;
    private readonly EnergyCalculator _energy = new();

    public IIntegrator CurrentIntegrator => _integrator;
    private double _currentTime;
    private double _initialEnergy;
    private bool _initialEnergySet;

    public NBodySolver()
    {
        _integrator = new VerletIntegrator();
        _currentTime = 0.0;
        _initialEnergy = 0.0;
        _initialEnergySet = false;
    }

    public void AddForce(IForceCalculator force)
    {
        _forces.Add(force);
    }

    public void SetIntegrator(IIntegrator integrator)
    {
        _integrator = integrator;
    }

    /// <summary>
    /// Advance the simulation by one timestep. Calls the integrator,
    /// computes energy diagnostics, and returns a snapshot of the simulation state.
    /// </summary>
    public SimulationState Step(PhysicsBody[] bodies, double dt)
    {
        _integrator.Step(bodies, dt, _forces.ToArray());
        _currentTime += dt;

        double ke = _energy.ComputeKE(bodies);
        double pe = _energy.ComputePE(bodies, _forces);
        double totalEnergy = ke + pe;
        Vec3d momentum = _energy.ComputeMomentum(bodies);

        if (!_initialEnergySet)
        {
            _initialEnergy = totalEnergy;
            _initialEnergySet = true;
        }

        double energyDrift = System.Math.Abs(_initialEnergy) > 1e-15
            ? (totalEnergy - _initialEnergy) / System.Math.Abs(_initialEnergy)
            : 0.0;

        return new SimulationState
        {
            Time = _currentTime,
            BodyCount = bodies.Length,
            KineticEnergy = ke,
            PotentialEnergy = pe,
            TotalMomentum = momentum,
            EnergyDrift = energyDrift
        };
    }

    /// <summary>
    /// Reset the solver to initial state. Clears time and initial energy reference.
    /// </summary>
    public void Reset()
    {
        _currentTime = 0.0;
        _initialEnergy = 0.0;
        _initialEnergySet = false;
    }
}
