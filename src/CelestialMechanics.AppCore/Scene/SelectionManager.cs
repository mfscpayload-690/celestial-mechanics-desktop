namespace CelestialMechanics.AppCore.Scene;

/// <summary>
/// Manages the current entity selection state for the application.
/// Supports both single-select and multi-select modes.
/// Pure logic — no UI dependencies. Thread-safe.
///
/// Selection is validated against the <see cref="SceneGraph"/> when a graph is provided,
/// preventing stale GUIDs from remaining selected after entity removal.
/// </summary>
public sealed class SelectionManager
{
    private readonly object _syncRoot = new();
    private readonly List<Guid> _multiSelection = new();
    private Guid? _primarySelection;
    private SceneGraph? _graph;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised whenever the selection changes. The argument contains the full
    /// current selection list (snapshot, safe to iterate on any thread).
    /// </summary>
    public event Action<IReadOnlyList<Guid>>? OnSelectionChanged;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Optionally bind a <see cref="SceneGraph"/> to enable existence validation.
    /// When bound, selecting a node that is not in the graph is silently ignored.
    /// </summary>
    public void BindSceneGraph(SceneGraph graph)
    {
        lock (_syncRoot) { _graph = graph; }
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>Primary (most-recently-selected) entity, or null if nothing is selected.</summary>
    public Guid? SelectedEntity
    {
        get { lock (_syncRoot) { return _primarySelection; } }
    }

    /// <summary>All selected entity IDs (including the primary). Read-only snapshot.</summary>
    public IReadOnlyList<Guid> MultiSelection
    {
        get { lock (_syncRoot) { return _multiSelection.AsReadOnly(); } }
    }

    public bool IsSelected(Guid id)
    {
        lock (_syncRoot) { return _multiSelection.Contains(id); }
    }

    public bool HasSelection
    {
        get { lock (_syncRoot) { return _multiSelection.Count > 0; } }
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears the current selection and selects <paramref name="id"/> exclusively.
    /// No-op if the entity is not present in the bound SceneGraph.
    /// </summary>
    public void Select(Guid id)
    {
        if (!ExistsInGraph(id)) return;

        List<Guid> snapshot;
        lock (_syncRoot)
        {
            _multiSelection.Clear();
            _multiSelection.Add(id);
            _primarySelection = id;
            snapshot = new List<Guid>(_multiSelection);
        }
        OnSelectionChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Adds <paramref name="id"/> to the multi-selection without clearing existing entries.
    /// Updates <see cref="SelectedEntity"/> to the most recently added entity.
    /// </summary>
    public void AddToSelection(Guid id)
    {
        if (!ExistsInGraph(id)) return;

        List<Guid> snapshot;
        lock (_syncRoot)
        {
            if (!_multiSelection.Contains(id))
                _multiSelection.Add(id);
            _primarySelection = id;
            snapshot = new List<Guid>(_multiSelection);
        }
        OnSelectionChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Toggles <paramref name="id"/> in/out of the multi-selection.
    /// </summary>
    public void ToggleSelect(Guid id)
    {
        if (!ExistsInGraph(id)) return;

        List<Guid> snapshot;
        bool changed;
        lock (_syncRoot)
        {
            if (_multiSelection.Contains(id))
            {
                _multiSelection.Remove(id);
                if (_primarySelection == id)
                    _primarySelection = _multiSelection.Count > 0 ? _multiSelection[^1] : null;
                changed = true;
            }
            else
            {
                _multiSelection.Add(id);
                _primarySelection = id;
                changed = true;
            }
            snapshot = new List<Guid>(_multiSelection);
        }
        if (changed) OnSelectionChanged?.Invoke(snapshot);
    }

    /// <summary>Removes <paramref name="id"/> from the selection if present.</summary>
    public void Deselect(Guid id)
    {
        List<Guid>? snapshot = null;
        lock (_syncRoot)
        {
            if (_multiSelection.Remove(id))
            {
                if (_primarySelection == id)
                    _primarySelection = _multiSelection.Count > 0 ? _multiSelection[^1] : null;
                snapshot = new List<Guid>(_multiSelection);
            }
        }
        if (snapshot != null) OnSelectionChanged?.Invoke(snapshot);
    }

    /// <summary>Clears all selections.</summary>
    public void Clear()
    {
        bool had;
        lock (_syncRoot)
        {
            had = _multiSelection.Count > 0;
            _multiSelection.Clear();
            _primarySelection = null;
        }
        if (had) OnSelectionChanged?.Invoke(Array.Empty<Guid>());
    }

    /// <summary>
    /// Removes any selected IDs that no longer exist in the bound SceneGraph.
    /// Should be called after scene mutations (node removal).
    /// </summary>
    public void PurgeStaleSelections()
    {
        if (_graph == null) return;

        List<Guid>? snapshot = null;
        lock (_syncRoot)
        {
            int before = _multiSelection.Count;
            _multiSelection.RemoveAll(id => _graph.GetNode(id) == null);
            if (_primarySelection.HasValue && !_multiSelection.Contains(_primarySelection.Value))
                _primarySelection = _multiSelection.Count > 0 ? _multiSelection[^1] : null;

            if (_multiSelection.Count != before)
                snapshot = new List<Guid>(_multiSelection);
        }
        if (snapshot != null) OnSelectionChanged?.Invoke(snapshot);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private bool ExistsInGraph(Guid id)
    {
        SceneGraph? graph;
        lock (_syncRoot) { graph = _graph; }
        if (graph == null) return true; // no graph bound → accept all
        return graph.GetNode(id) != null;
    }
}
