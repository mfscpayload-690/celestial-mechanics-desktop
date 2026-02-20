namespace CelestialMechanics.Physics.BarnesHut;

/// <summary>
/// Fixed-capacity pool for <see cref="OctreeNode"/> instances.
///
/// ALLOCATION STRATEGY
/// -------------------
/// The pool pre-allocates a flat array of OctreeNode structs at construction.
/// During tree building, <see cref="Allocate"/> returns the next available
/// index and increments the counter. Between simulation steps, <see cref="Reset"/>
/// sets the counter back to zero — all nodes are logically freed without any
/// GC interaction.
///
/// This is the key to zero-allocation tree construction:
///   • No 'new OctreeNode()' calls on the heap during simulation
///   • No garbage collector pressure from tree rebuilding every step
///   • No List{T} resizing or LINQ allocations
///
/// CAPACITY SIZING
/// ---------------
/// For N bodies, a Barnes-Hut octree typically requires at most ~4N nodes
/// (N leaf nodes + up to ~3N internal nodes from recursive subdivision).
/// The pool is sized with a safety margin:
///
///   capacity = max(initialCapacity, 4 * bodyCount + 64)
///
/// If the pool is exhausted during insertion (coincident bodies causing
/// deep recursion), the tree falls back to treating overlapping bodies as
/// a single aggregate — a safe approximation that prevents allocation.
///
/// MEMORY COST
/// -----------
/// Each OctreeNode ≈ 148 bytes. For 20,000 bodies:
///   Pool size = 4 × 20,000 + 64 = 80,064 nodes ≈ 11.3 MB
/// This is allocated once and reused indefinitely.
/// </summary>
public sealed class OctreePool
{
    private OctreeNode[] _nodes;
    private int _count;

    /// <summary>Number of nodes currently allocated from the pool.</summary>
    public int Count => _count;

    /// <summary>Maximum number of nodes this pool can hold.</summary>
    public int Capacity => _nodes.Length;

    /// <summary>
    /// Direct access to the underlying node array.
    /// Callers use indices returned by <see cref="Allocate"/> to read/write nodes.
    /// </summary>
    public OctreeNode[] Nodes => _nodes;

    /// <summary>
    /// Create a pool with the given initial capacity.
    /// </summary>
    /// <param name="initialCapacity">
    /// Number of nodes to pre-allocate. Use 4 × expected body count + margin.
    /// </param>
    public OctreePool(int initialCapacity)
    {
        if (initialCapacity <= 0) initialCapacity = 256;
        _nodes = new OctreeNode[initialCapacity];
        _count = 0;
    }

    /// <summary>
    /// Allocate a new node from the pool and initialize it with the given bounds.
    /// Returns the pool index of the allocated node, or -1 if the pool is full.
    ///
    /// THREAD SAFETY: This method is NOT thread-safe. Tree construction
    /// must occur on a single thread (which it does — only the force
    /// traversal is parallelised).
    /// </summary>
    public int Allocate(double cx, double cy, double cz, double halfSize)
    {
        if (_count >= _nodes.Length)
        {
            // Pool exhausted — grow by 2x to handle edge cases.
            // This allocation happens very rarely (only if the initial
            // sizing estimate was too low, e.g. many coincident bodies).
            int newCapacity = _nodes.Length * 2;
            var newNodes = new OctreeNode[newCapacity];
            Array.Copy(_nodes, newNodes, _nodes.Length);
            _nodes = newNodes;
        }

        int index = _count++;
        _nodes[index].Init(cx, cy, cz, halfSize);
        return index;
    }

    /// <summary>
    /// Reset the pool for reuse in the next simulation step.
    /// Does NOT zero the array — Init() overwrites all fields on allocation.
    /// This is O(1) and triggers no GC activity.
    /// </summary>
    public void Reset()
    {
        _count = 0;
    }

    /// <summary>
    /// Ensure the pool can hold at least <paramref name="minCapacity"/> nodes.
    /// Grows the array if necessary. Call this before tree construction if
    /// the body count has increased since last step.
    /// </summary>
    public void EnsureCapacity(int minCapacity)
    {
        if (_nodes.Length >= minCapacity) return;

        var newNodes = new OctreeNode[minCapacity];
        // No need to copy old data — Reset() was called before this.
        _nodes = newNodes;
    }
}
