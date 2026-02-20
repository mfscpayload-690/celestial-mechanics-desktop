namespace CelestialMechanics.Physics.BarnesHut;

/// <summary>
/// A single node in the 3D Barnes-Hut octree.
///
/// MEMORY LAYOUT
/// -------------
/// This is a value type (struct) stored in a flat array managed by
/// <see cref="OctreePool"/>. Using struct + pool eliminates all per-step
/// heap allocations: the pool pre-allocates the array once, and
/// <see cref="OctreePool.Reset"/> simply resets the counter to zero.
///
/// CHILD INDEXING
/// --------------
/// Each internal node has up to 8 children, indexed by octant:
///
///   Octant = (x >= cx ? 4 : 0) | (y >= cy ? 2 : 0) | (z >= cz ? 1 : 0)
///
///   Octant 0: x < cx, y < cy, z < cz  (---) 
///   Octant 1: x < cx, y < cy, z >= cz (--+)
///   Octant 2: x < cx, y >= cy, z < cz (-+-)
///   Octant 3: x < cx, y >= cy, z >= cz (-++)
///   Octant 4: x >= cx, y < cy, z < cz (+--) 
///   Octant 5: x >= cx, y < cy, z >= cz (+-+)
///   Octant 6: x >= cx, y >= cy, z < cz (++-) 
///   Octant 7: x >= cx, y >= cy, z >= cz (+++)
///
/// Children are stored as int indices into the <see cref="OctreePool"/> array.
/// A value of -1 means "no child in this octant".
///
/// NUMERICAL PRECISION
/// -------------------
/// All spatial coordinates and mass values use double precision (64-bit),
/// consistent with the rest of the engine. This is critical for galaxy-scale
/// simulations where positions span many orders of magnitude.
///
/// CACHE BEHAVIOUR
/// ---------------
/// The struct is ~144 bytes. A flat array of these nodes has excellent
/// spatial locality during tree traversal because parent and early children
/// are allocated sequentially by the pool. Depth-first traversal reads
/// nodes roughly in allocation order, which aligns with cache-line prefetch.
/// </summary>
public struct OctreeNode
{
    // ── Spatial bounds ────────────────────────────────────────────────────────
    // Axis-aligned bounding cube defined by center + half-size.
    // All bodies within this node fall inside [Center - HalfSize, Center + HalfSize]
    // on each axis.

    /// <summary>X coordinate of the bounding cube center.</summary>
    public double CenterX;
    /// <summary>Y coordinate of the bounding cube center.</summary>
    public double CenterY;
    /// <summary>Z coordinate of the bounding cube center.</summary>
    public double CenterZ;
    /// <summary>Half the side length of the bounding cube.</summary>
    public double HalfSize;

    // ── Aggregated mass properties ────────────────────────────────────────────
    // Computed bottom-up after all bodies are inserted. For a leaf node,
    // these equal the single body's mass and position. For internal nodes,
    // TotalMass = sum of children's masses, and CenterOfMass is the
    // mass-weighted average position.

    /// <summary>Total mass of all bodies within this node's subtree.</summary>
    public double TotalMass;
    /// <summary>X component of the center of mass.</summary>
    public double ComX;
    /// <summary>Y component of the center of mass.</summary>
    public double ComY;
    /// <summary>Z component of the center of mass.</summary>
    public double ComZ;

    // ── Body reference (leaf only) ────────────────────────────────────────────
    // For leaf nodes: index into the BodySoA arrays.
    // For internal nodes: -1 (sentinel).
    // For empty nodes: -1 (sentinel).

    /// <summary>
    /// Index of the body stored in this leaf node, or -1 if this is an
    /// internal node or empty node.
    /// </summary>
    public int BodyIndex;

    // ── Child references ──────────────────────────────────────────────────────
    // Indices into the OctreePool array. -1 means "no child".
    // Using a fixed-size inline storage (8 ints) avoids heap allocation
    // for child arrays.

    /// <summary>Pool index of child in octant 0, or -1 if empty.</summary>
    public int Child0;
    /// <summary>Pool index of child in octant 1, or -1 if empty.</summary>
    public int Child1;
    /// <summary>Pool index of child in octant 2, or -1 if empty.</summary>
    public int Child2;
    /// <summary>Pool index of child in octant 3, or -1 if empty.</summary>
    public int Child3;
    /// <summary>Pool index of child in octant 4, or -1 if empty.</summary>
    public int Child4;
    /// <summary>Pool index of child in octant 5, or -1 if empty.</summary>
    public int Child5;
    /// <summary>Pool index of child in octant 6, or -1 if empty.</summary>
    public int Child6;
    /// <summary>Pool index of child in octant 7, or -1 if empty.</summary>
    public int Child7;

    /// <summary>True if this node contains exactly one body (leaf).</summary>
    public bool IsLeaf;

    // ── Convenience accessors ─────────────────────────────────────────────────

    /// <summary>
    /// Get the child index for the given octant (0–7).
    /// </summary>
    public readonly int GetChild(int octant) => octant switch
    {
        0 => Child0,
        1 => Child1,
        2 => Child2,
        3 => Child3,
        4 => Child4,
        5 => Child5,
        6 => Child6,
        7 => Child7,
        _ => -1
    };

    /// <summary>
    /// Set the child index for the given octant (0–7).
    /// </summary>
    public void SetChild(int octant, int poolIndex)
    {
        switch (octant)
        {
            case 0: Child0 = poolIndex; break;
            case 1: Child1 = poolIndex; break;
            case 2: Child2 = poolIndex; break;
            case 3: Child3 = poolIndex; break;
            case 4: Child4 = poolIndex; break;
            case 5: Child5 = poolIndex; break;
            case 6: Child6 = poolIndex; break;
            case 7: Child7 = poolIndex; break;
        }
    }

    /// <summary>
    /// Initialize this node as an empty internal/leaf node with the given bounds.
    /// All children set to -1, BodyIndex set to -1, mass set to 0.
    /// </summary>
    public void Init(double cx, double cy, double cz, double halfSize)
    {
        CenterX = cx;
        CenterY = cy;
        CenterZ = cz;
        HalfSize = halfSize;

        TotalMass = 0.0;
        ComX = 0.0;
        ComY = 0.0;
        ComZ = 0.0;

        BodyIndex = -1;
        IsLeaf = false;

        Child0 = -1;
        Child1 = -1;
        Child2 = -1;
        Child3 = -1;
        Child4 = -1;
        Child5 = -1;
        Child6 = -1;
        Child7 = -1;
    }

    /// <summary>
    /// Determine which octant a point (px, py, pz) falls into relative
    /// to this node's center.
    ///
    /// Octant encoding:
    ///   bit 2 (value 4): px >= CenterX
    ///   bit 1 (value 2): py >= CenterY
    ///   bit 0 (value 1): pz >= CenterZ
    /// </summary>
    public readonly int OctantFor(double px, double py, double pz)
    {
        int octant = 0;
        if (px >= CenterX) octant |= 4;
        if (py >= CenterY) octant |= 2;
        if (pz >= CenterZ) octant |= 1;
        return octant;
    }

    /// <summary>
    /// Compute the center of the child bounding cube for the given octant.
    /// Each child has half the parent's half-size, offset by ±quarterSize
    /// on each axis according to the octant encoding.
    /// </summary>
    public readonly void ChildCenter(int octant, out double cx, out double cy, out double cz)
    {
        double quarter = HalfSize * 0.5;
        cx = CenterX + ((octant & 4) != 0 ? quarter : -quarter);
        cy = CenterY + ((octant & 2) != 0 ? quarter : -quarter);
        cz = CenterZ + ((octant & 1) != 0 ? quarter : -quarter);
    }
}
