namespace CelestialMechanics.AppCore.Scene;

/// <summary>
/// Classifies what a <see cref="SceneNode"/> represents in the hierarchy.
/// </summary>
public enum NodeType
{
    /// <summary>Organizational folder with no physics counterpart.</summary>
    Folder,
    /// <summary>A single ECS entity linked via <see cref="SceneNode.LinkedEntityId"/>.</summary>
    Entity,
    /// <summary>A composite structure grouping multiple entities (galaxy, cluster, binary).</summary>
    Composite
}

/// <summary>
/// A node in the scene hierarchy tree.
///
/// SceneNodes are organizational only — they carry no physics state. The optional
/// <see cref="LinkedEntityId"/> bridges the scene tree to the ECS layer via
/// <see cref="CelestialMechanics.Simulation.Core.Entity.Id"/>.
/// </summary>
public sealed class SceneNode
{
    // ── Identity ────────────────────────────────────────────────────────────

    /// <summary>Stable, unique identity for this node. Set at construction and immutable.</summary>
    public Guid Id { get; }

    /// <summary>Human-readable display name. Mutable for rename operations.</summary>
    public string Name { get; set; }

    /// <summary>Structural role of this node in the hierarchy.</summary>
    public NodeType NodeType { get; set; }

    // ── ECS bridge ──────────────────────────────────────────────────────────

    /// <summary>
    /// Optional link to an ECS entity. When set, this node represents exactly one
    /// simulation entity. Organisational nodes (Folder, Composite root) leave this null.
    /// </summary>
    public Guid? LinkedEntityId { get; set; }

    // ── Tree structure ──────────────────────────────────────────────────────

    /// <summary>Parent node; null for the root node.</summary>
    public SceneNode? Parent { get; internal set; }

    private readonly List<SceneNode> _children = new();

    /// <summary>Read-only view of direct children.</summary>
    public IReadOnlyList<SceneNode> Children => _children;

    // ── Construction ─────────────────────────────────────────────────────────

    public SceneNode(string name, NodeType nodeType = NodeType.Folder)
    {
        Id = Guid.NewGuid();
        Name = name;
        NodeType = nodeType;
    }

    public SceneNode(Guid id, string name, NodeType nodeType = NodeType.Folder)
    {
        Id = id;
        Name = name;
        NodeType = nodeType;
    }

    // ── Internal tree mutation (called only by SceneGraph) ───────────────────

    internal void AddChild(SceneNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal bool RemoveChild(SceneNode child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            return true;
        }
        return false;
    }

    internal void DetachFromParent()
    {
        Parent?.RemoveChild(this);
    }

    // ── Traversal helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns this node and all descendants in depth-first pre-order.
    /// </summary>
    public IEnumerable<SceneNode> TraverseDepthFirst()
    {
        yield return this;
        foreach (var child in _children)
            foreach (var descendant in child.TraverseDepthFirst())
                yield return descendant;
    }

    public override string ToString() => $"[{NodeType}] {Name} ({Id:D})";
}
