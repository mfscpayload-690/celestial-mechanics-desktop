using System.Collections.Concurrent;

namespace CelestialMechanics.AppCore.Scene;

/// <summary>
/// Thread-safe, flat-indexed scene hierarchy.
///
/// Organisational only — reads/writes to the tree do NOT affect physics.
/// A virtual root node (invisible, Name="Scene Root") acts as the universal parent.
/// All public mutating operations synchronise via <see cref="ReaderWriterLockSlim"/>,
/// allowing concurrent RO access from the render thread while the simulation thread
/// performs single-writer updates.
/// </summary>
public sealed class SceneGraph : IDisposable
{
    // ── Internal state ───────────────────────────────────────────────────────

    private readonly Dictionary<Guid, SceneNode> _index = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    /// <summary>
    /// Virtual root — always present, never exposed as a user-facing selection target.
    /// All top-level nodes are children of this sentinel.
    /// </summary>
    public SceneNode Root { get; }

    // ── Events ───────────────────────────────────────────────────────────────

    public event Action<SceneNode>? NodeAdded;
    public event Action<Guid>?       NodeRemoved;
    public event Action<SceneNode, Guid>? NodeMoved; // (node, previousParentId)

    // ── Construction ─────────────────────────────────────────────────────────

    public SceneGraph()
    {
        Root = new SceneNode(Guid.Empty, "Scene Root", NodeType.Folder);
        _index[Root.Id] = Root;
    }

    // ── Query ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the node with the given <paramref name="nodeId"/>, or null if not found.
    /// </summary>
    public SceneNode? GetNode(Guid nodeId)
    {
        _lock.EnterReadLock();
        try
        {
            _index.TryGetValue(nodeId, out var node);
            return node;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Total number of nodes including root.</summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _index.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    // ── Mutation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="node"/> as a child of <paramref name="parentId"/>.
    /// Pass <see cref="Guid.Empty"/> to attach directly under the scene root.
    /// </summary>
    /// <exception cref="ArgumentException">If parentId is not found.</exception>
    /// <exception cref="InvalidOperationException">If node.Id already exists.</exception>
    public void AddNode(Guid parentId, SceneNode node)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_index.ContainsKey(node.Id))
                throw new InvalidOperationException($"A node with Id {node.Id} already exists.");

            var parent = ResolveParent(parentId);
            parent.AddChild(node);
            IndexSubtree(node);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        NodeAdded?.Invoke(node);
    }

    /// <summary>
    /// Removes the node with <paramref name="nodeId"/> and its entire subtree.
    /// The root node cannot be removed.
    /// </summary>
    public bool RemoveNode(Guid nodeId)
    {
        if (nodeId == Root.Id)
            throw new InvalidOperationException("Cannot remove the scene root.");

        SceneNode? removed;
        _lock.EnterWriteLock();
        try
        {
            if (!_index.TryGetValue(nodeId, out removed))
                return false;

            removed.DetachFromParent();
            UnindexSubtree(removed);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        NodeRemoved?.Invoke(nodeId);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="nodeId"/> under <paramref name="newParentId"/>.
    /// Moving a node to a descendant of itself is an error.
    /// </summary>
    public void MoveNode(Guid nodeId, Guid newParentId)
    {
        if (nodeId == Root.Id)
            throw new InvalidOperationException("Cannot move the root node.");

        Guid previousParentId;
        SceneNode targetNode;

        _lock.EnterWriteLock();
        try
        {
            if (!_index.TryGetValue(nodeId, out var node))
                throw new KeyNotFoundException($"Node {nodeId} not found.");

            var newParent = ResolveParent(newParentId);

            // Guard against cycles: ensure newParent is not a descendant of node
            if (IsDescendantOf(newParent, node))
                throw new InvalidOperationException("Cannot move a node into its own subtree.");

            previousParentId = node.Parent?.Id ?? Root.Id;
            node.DetachFromParent();
            newParent.AddChild(node);
            targetNode = node;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        NodeMoved?.Invoke(targetNode, previousParentId);
    }

    // ── Traversal ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all nodes (excluding root) in depth-first pre-order.
    /// Captures a snapshot under read lock to allow safe enumeration.
    /// </summary>
    public IEnumerable<SceneNode> TraverseDepthFirst()
    {
        List<SceneNode> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = Root.TraverseDepthFirst().Skip(1).ToList(); // skip root sentinel
        }
        finally
        {
            _lock.ExitReadLock();
        }
        return snapshot;
    }

    /// <summary>
    /// Returns all <see cref="SceneNode.LinkedEntityId"/> values for nodes
    /// that have a direct entity binding (non-null LinkedEntityId).
    /// </summary>
    public IEnumerable<Guid> FlattenToEntityList()
    {
        _lock.EnterReadLock();
        try
        {
            return _index.Values
                .Where(n => n.LinkedEntityId.HasValue)
                .Select(n => n.LinkedEntityId!.Value)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private SceneNode ResolveParent(Guid parentId)
    {
        if (parentId == Guid.Empty) return Root;
        if (_index.TryGetValue(parentId, out var parent)) return parent;
        throw new ArgumentException($"Parent node {parentId} not found.");
    }

    private void IndexSubtree(SceneNode node)
    {
        // DFS without yield to avoid iterator overhead inside write lock
        var stack = new Stack<SceneNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            _index[current.Id] = current;
            foreach (var child in current.Children)
                stack.Push(child);
        }
    }

    private void UnindexSubtree(SceneNode node)
    {
        var stack = new Stack<SceneNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            _index.Remove(current.Id);
            foreach (var child in current.Children)
                stack.Push(child);
        }
    }

    private static bool IsDescendantOf(SceneNode candidate, SceneNode ancestor)
    {
        var current = candidate.Parent;
        while (current != null)
        {
            if (current.Id == ancestor.Id) return true;
            current = current.Parent;
        }
        return false;
    }

    public void Dispose() => _lock.Dispose();
}
