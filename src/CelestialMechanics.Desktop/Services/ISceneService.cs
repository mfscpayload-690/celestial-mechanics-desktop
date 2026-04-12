namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// Abstraction over SceneService for DI registration.
/// Manages the scene graph and selection state.
/// </summary>
public interface ISceneService
{
    /// <summary>Raised when the scene graph changes.</summary>
    event Action? SceneChanged;

    /// <summary>Raised when the selection changes.</summary>
    event Action<Guid?>? SelectionChanged;

    /// <summary>The currently selected node ID, or null.</summary>
    Guid? SelectedNodeId { get; }

    void Select(Guid nodeId);
    void ClearSelection();
    void RefreshFromSimulation();
}
