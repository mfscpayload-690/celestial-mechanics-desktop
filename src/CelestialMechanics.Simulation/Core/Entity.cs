namespace CelestialMechanics.Simulation.Core;

/// <summary>
/// Allocation-efficient entity container for Phase 8 ECS.
/// Components are stored in a pre-allocated list. Lookup uses linear scan
/// (no reflection, no boxing, no dictionary overhead for typical 2–5 components).
/// </summary>
public sealed class Entity
{
    public Guid Id { get; }
    public string Tag { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    private readonly List<IComponent> _components;

    public Entity()
    {
        Id = Guid.NewGuid();
        _components = new List<IComponent>(4);
    }

    public Entity(Guid id)
    {
        Id = id;
        _components = new List<IComponent>(4);
    }

    /// <summary>
    /// Get the first component of type <typeparamref name="T"/>.
    /// Returns null if no matching component exists. No reflection.
    /// </summary>
    public T? GetComponent<T>() where T : class, IComponent
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (_components[i] is T match)
                return match;
        }
        return null;
    }

    /// <summary>
    /// Returns true if this entity has a component of type <typeparamref name="T"/>.
    /// </summary>
    public bool HasComponent<T>() where T : class, IComponent
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (_components[i] is T)
                return true;
        }
        return false;
    }

    public void AddComponent(IComponent component)
    {
        _components.Add(component);
    }

    public bool RemoveComponent<T>() where T : class, IComponent
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (_components[i] is T)
            {
                _components.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Read-only access to the component list for enumeration.
    /// </summary>
    public IReadOnlyList<IComponent> Components => _components;

    public void Update(double dt)
    {
        for (int i = 0; i < _components.Count; i++)
        {
            _components[i].Update(dt);
        }
    }
}
