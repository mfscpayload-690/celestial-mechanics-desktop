using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;
using CelestialMechanics.Simulation.Events;
using CelestialMechanics.Simulation.PhysicsExtensions;
using CelestialMechanics.Simulation.Systems;

namespace CelestialMechanics.Simulation.Core;

/// <summary>
/// Central orchestrator for the simulation framework.
///
/// Maintains entities, time management, space metric scaling, event triggers,
/// and catastrophic astrophysical events (supernovae, mergers, shockwaves).
/// Interfaces with the existing NBodySolver/Barnes–Hut backend for gravity computation
/// without modifying the physics internals.
///
/// Deterministic update pipeline:
///   1. dt = TimeManager.GetEffectiveDelta(baseDt)
///   2. Flush pending entity changes
///   3. Apply expansion scaling (SpaceMetricManager)
///   4. Sync Entity→PhysicsBody[]
///   5. Compute gravitational forces via NBodySolver (Barnes–Hut + PN)
///   6. Sync PhysicsBody[]→Entity
///   7. Update all components (stellar evolution, explosions, etc.)
///   8. Evaluate triggers and execute event actions
///   9. Process catastrophic events (supernovae, shockwaves, mergers)
///  10. Flush spawned/removed entities
///  11. Advance SimulationTime
/// </summary>
public sealed class SimulationManager
{
    private readonly List<Entity> _entities = new();
    private readonly List<Entity> _pendingAdd = new();
    private readonly List<Guid> _pendingRemove = new();
    private readonly List<(ITriggerCondition Condition, IEventAction Action)> _eventRules = new();
    private readonly NBodySolver _solver;
    private PhysicsBody[] _bodies = Array.Empty<PhysicsBody>();
    private bool _bodiesDirty = true;
    private int _nextBodyId;

    public TimeManager Time { get; }
    public SpaceMetricManager SpaceMetric { get; }
    public EventBus EventBus { get; }
    public PhysicsConfig Config { get; }

    /// <summary>Catastrophic event subsystem (supernovae, mergers, shockwaves).</summary>
    public CatastrophicEventSystem CatastrophicEvents { get; }

    /// <summary>Read-only access to the entity list.</summary>
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>Current simulation body count (entities with PhysicsComponent).</summary>
    public int BodyCount => _bodies.Length;

    public SimulationManager() : this(new PhysicsConfig()) { }

    public SimulationManager(PhysicsConfig config)
    {
        Config = config;
        Time = new TimeManager();
        SpaceMetric = new SpaceMetricManager();
        EventBus = new EventBus();

        // Initialize catastrophic event system with default pool sizes
        CatastrophicEvents = new CatastrophicEventSystem();
        CatastrophicEvents.EventBus = EventBus;
        CatastrophicEvents.OnEntityAdded = e => AddEntity(e);
        CatastrophicEvents.OnEntityRemoved = e => RemoveEntity(e);

        _solver = new NBodySolver();
        _solver.AddForce(new NewtonianGravity
        {
            SofteningEpsilon = config.SofteningEpsilon,
            RangeScale = config.GravityRangeScale
        });
        _solver.SetIntegrator(new VerletIntegrator());
        _solver.ConfigureSoA(
            enabled: config.UseSoAPath,
            softening: config.SofteningEpsilon,
            deterministic: config.DeterministicMode,
            useParallel: config.UseParallelComputation,
            useBarnesHut: config.UseBarnesHut,
            theta: config.Theta,
            enableCollisions: config.EnableCollisions,
            useSimd: config.UseSimd,
            enablePostNewtonian: config.EnablePostNewtonian,
            enableAccretionDisks: config.EnableAccretionDisks,
            enableGravitationalWaves: config.EnableGravitationalWaves,
            maxAccretionParticles: config.MaxAccretionParticles,
            enableJets: config.EnableJetEmission,
            jetThreshold: config.AccretionJetThreshold,
            gwObserverDistance: config.GravitationalWaveObserverDistance
        );
    }

    // ── Entity management ───────────────────────────────────────────────────

    public void AddEntity(Entity e)
    {
        _pendingAdd.Add(e);
        _bodiesDirty = true;
    }

    public void RemoveEntity(Entity e)
    {
        _pendingRemove.Add(e.Id);
        _bodiesDirty = true;
    }

    public void RegisterEventRule(ITriggerCondition condition, IEventAction action)
    {
        _eventRules.Add((condition, action));
    }

    // ── Step ────────────────────────────────────────────────────────────────

    public void Step(double baseDt)
    {
        // 1. Effective delta time
        double dt = Time.GetEffectiveDelta(baseDt);
        if (dt <= 0.0) return;

        // 2. Process pending adds/removes
        FlushPendingChanges();

        // 3. Apply expansion scaling (before physics)
        if (SpaceMetric.ExpansionEnabled)
        {
            SpaceMetric.Update(dt);
            ApplyExpansionScaling();
        }

        // 4–6. Sync to PhysicsBody[], run NBodySolver (gravity + PN), sync back
        if (_bodiesDirty)
            RebuildBodiesArray();

        SyncEntitiesToBodies();
        _solver.Step(_bodies, dt);
        SyncBodiesToEntities();

        // 7. Update all entity components (stellar evolution, explosions, etc.)
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].IsActive)
                _entities[i].Update(dt);
        }

        // 8. Evaluate triggers and execute event actions
        EvaluateAndExecuteEvents();

        // 9. Process catastrophic events (supernovae → shockwaves → mergers)
        CatastrophicEvents.Process(_entities, Time.SimulationTime, dt);

        // 10. Flush entities spawned/removed by catastrophic events
        FlushPendingChanges();

        // 11. Advance simulation time
        Time.AdvanceTime(dt);
    }

    // ── Private pipeline steps ──────────────────────────────────────────────

    /// <summary>
    /// Processes all pending entity additions and removals.
    /// Call this before inspecting <see cref="Entities"/> if you have just added/removed entities
    /// and haven't called <see cref="Step"/> yet.
    /// </summary>
    public void FlushPendingChanges()
    {
        if (_pendingAdd.Count > 0)
        {
            for (int i = 0; i < _pendingAdd.Count; i++)
                _entities.Add(_pendingAdd[i]);
            _pendingAdd.Clear();
            _bodiesDirty = true;
        }

        if (_pendingRemove.Count > 0)
        {
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                if (_pendingRemove.Contains(_entities[i].Id))
                    _entities.RemoveAt(i);
            }
            _pendingRemove.Clear();
            _bodiesDirty = true;
        }

        if (_bodiesDirty)
            RebuildBodiesArray();
    }

    private void RebuildBodiesArray()
    {
        // Count entities with PhysicsComponent
        int count = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].IsActive && _entities[i].HasComponent<PhysicsComponent>())
                count++;
        }

        // Only reallocate if size changed
        if (_bodies.Length != count)
            _bodies = new PhysicsBody[count];

        int idx = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.IsActive) continue;

            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null) continue;

            pc.BodyIndex = idx;
            _bodies[idx] = new PhysicsBody(idx, pc.Mass, pc.Position, pc.Velocity, BodyType.Custom)
            {
                Velocity = pc.Velocity,
                Acceleration = pc.Acceleration,
                Radius = pc.Radius,
                Density = pc.Density,
                IsActive = true,
                IsCollidable = pc.IsCollidable
            };
            idx++;
        }

        _bodiesDirty = false;
        _nextBodyId = count;
    }

    private void SyncEntitiesToBodies()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.IsActive) continue;

            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null || pc.BodyIndex < 0 || pc.BodyIndex >= _bodies.Length) continue;

            int idx = pc.BodyIndex;
            _bodies[idx].Mass = pc.Mass;
            _bodies[idx].Position = pc.Position;
            _bodies[idx].Velocity = pc.Velocity;
            _bodies[idx].Acceleration = pc.Acceleration;
            _bodies[idx].Radius = pc.Radius;
            _bodies[idx].IsActive = entity.IsActive;
        }
    }

    private void SyncBodiesToEntities()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.IsActive) continue;

            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null || pc.BodyIndex < 0 || pc.BodyIndex >= _bodies.Length) continue;

            int idx = pc.BodyIndex;
            pc.Position = _bodies[idx].Position;
            pc.Velocity = _bodies[idx].Velocity;
            pc.Acceleration = _bodies[idx].Acceleration;
            pc.Mass = _bodies[idx].Mass;
            pc.Radius = _bodies[idx].Radius;
        }
    }

    private void ApplyExpansionScaling()
    {
        double ratio = SpaceMetric.GetScaleRatio();
        if (System.Math.Abs(ratio - 1.0) < 1e-15) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.IsActive) continue;

            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null) continue;

            // Scale position relative to origin
            pc.Position = pc.Position * ratio;
        }
    }

    private void EvaluateAndExecuteEvents()
    {
        if (_eventRules.Count == 0) return;

        // Collect triggered (entity, action) pairs before executing
        // to ensure deterministic evaluation order
        var triggered = new List<(Entity Entity, IEventAction Action)>();

        for (int r = 0; r < _eventRules.Count; r++)
        {
            var (condition, action) = _eventRules[r];
            for (int e = 0; e < _entities.Count; e++)
            {
                var entity = _entities[e];
                if (!entity.IsActive) continue;

                if (condition.Evaluate(entity))
                {
                    triggered.Add((entity, action));
                }
            }
        }

        // Execute all triggered actions
        for (int i = 0; i < triggered.Count; i++)
        {
            triggered[i].Action.Execute(triggered[i].Entity);
        }
    }

    /// <summary>Reset the simulation to initial state.</summary>
    public void Reset()
    {
        _entities.Clear();
        _pendingAdd.Clear();
        _pendingRemove.Clear();
        _bodies = Array.Empty<PhysicsBody>();
        _bodiesDirty = true;
        Time.Reset();
        SpaceMetric.Reset();
        CatastrophicEvents.Reset();
        _solver.Reset();
    }
}
