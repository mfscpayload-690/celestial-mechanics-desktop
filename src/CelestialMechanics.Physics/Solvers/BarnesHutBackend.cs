using CelestialMechanics.Physics.BarnesHut;
using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Barnes-Hut O(n log n) force computation backend.
///
/// ALGORITHM OVERVIEW
/// ------------------
/// The Barnes-Hut algorithm replaces the O(n²) pairwise force summation with
/// a hierarchical approximation using an octree (3D spatial tree). The key idea:
///
///   "If a group of bodies is far enough away, treat the entire group as a
///    single point mass located at the group's center of mass."
///
/// This reduces the number of force calculations from O(n²) to O(n log n),
/// making simulations of 10,000–100,000 bodies feasible on a single CPU.
///
/// STEP-BY-STEP PER SIMULATION TICK
/// ----------------------------------
/// 1. Compute bounding box:  O(n)     — scan all body positions for min/max
/// 2. Build octree:          O(n log n) — insert each body into tree
/// 3. Aggregate mass:        O(n)     — bottom-up pass computes total mass + COM
/// 4. Force traversal:       O(n log n) — for each body, walk tree using θ criterion
///
/// OPENING ANGLE CRITERION (θ)
/// ----------------------------
/// For each node encountered during traversal:
///   - If node is a leaf containing a different body: compute direct force
///   - If (node_size / distance_to_COM) < θ: approximate as single mass
///   - Otherwise: recurse into the node's children
///
/// Typical θ values:
///   θ = 0.0  → equivalent to exact O(n²) (never approximates)
///   θ = 0.5  → good balance of speed and accuracy (~0.1% force error)
///   θ = 1.0  → fast but less accurate (~1% force error)
///   θ = 1.5+ → very fast, suitable for visual-only simulations
///
/// DETERMINISM
/// -----------
/// When DeterministicMode = true:
///   - Tree build is inherently deterministic (single-threaded, insertion order)
///   - Force traversal visits children in fixed octant order (0–7)
///   - No floating-point non-deterministic reductions
///   - Bit-identical results across runs
///
/// When UseParallelComputation = true:
///   - Tree build remains single-threaded (cheap relative to traversal)
///   - Force traversal parallelised via Parallel.For over body index
///   - Each thread reads shared tree (immutable after build) and writes only
///     to its own AccX[i]/AccY[i]/AccZ[i] — no locks, no races
///   - FP addition order within each body's traversal is fixed (deterministic
///     per-body), but thread scheduling may cause bit-level variation
///
/// MEMORY BEHAVIOUR
/// ----------------
/// - OctreePool is allocated once and reused across steps via Reset()
/// - No per-step heap allocations in the hot path
/// - No LINQ, no List{T}, no temporary collections
/// - Pool capacity: ~4N+64 nodes for N bodies (~148 bytes per node)
///   For 20,000 bodies: ~11.3 MB pool, allocated once
/// </summary>
public sealed class BarnesHutBackend : IPhysicsComputeBackend
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opening angle parameter θ. Controls the accuracy/speed trade-off.
    /// Smaller θ = more accurate but slower. Default 0.5.
    /// </summary>
    public double Theta { get; set; } = 0.5;

    /// <summary>
    /// When true, force traversal is parallelised over body index.
    /// Tree construction is always single-threaded.
    /// </summary>
    public bool UseParallel { get; set; } = false;

    /// <summary>
    /// Minimum node half-size below which subdivision stops.
    /// Prevents infinite recursion for coincident or near-coincident bodies.
    /// This is a fraction of the root bounding box size.
    /// </summary>
    public double MinNodeSize { get; set; } = 1e-12;

    // ── Reusable state ────────────────────────────────────────────────────────
    // These are allocated once and reused across simulation steps.

    private OctreePool? _pool;

    // ── IPhysicsComputeBackend implementation ─────────────────────────────────

    /// <inheritdoc/>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        int n = bodies.Count;
        if (n == 0) return;

        double eps2 = softening * softening;
        double theta2 = Theta * Theta; // pre-square for comparison: (s/d)² < θ²

        // ── Preload array references ──────────────────────────────────────────
        double[] px  = bodies.PosX;
        double[] py  = bodies.PosY;
        double[] pz  = bodies.PosZ;
        double[] ax  = bodies.AccX;
        double[] ay  = bodies.AccY;
        double[] az  = bodies.AccZ;
        double[] m   = bodies.Mass;
        bool[]   act = bodies.IsActive;

        // ── Zero accelerations ────────────────────────────────────────────────
        Array.Clear(ax, 0, n);
        Array.Clear(ay, 0, n);
        Array.Clear(az, 0, n);

        // ── Step 1: Compute bounding box ──────────────────────────────────────
        // Scan all active bodies to find the axis-aligned bounding cube.
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        for (int i = 0; i < n; i++)
        {
            if (!act[i]) continue;
            if (px[i] < minX) minX = px[i];
            if (py[i] < minY) minY = py[i];
            if (pz[i] < minZ) minZ = pz[i];
            if (px[i] > maxX) maxX = px[i];
            if (py[i] > maxY) maxY = py[i];
            if (pz[i] > maxZ) maxZ = pz[i];
        }

        // Make it a cube (equal half-size on all axes) with a small margin.
        double cx = (minX + maxX) * 0.5;
        double cy = (minY + maxY) * 0.5;
        double cz = (minZ + maxZ) * 0.5;
        double halfSize = System.Math.Max(maxX - minX, System.Math.Max(maxY - minY, maxZ - minZ)) * 0.5;
        halfSize = System.Math.Max(halfSize, 1e-10); // prevent zero-size for single body
        halfSize *= 1.001; // tiny margin to ensure all bodies are strictly inside

        // ── Step 2: Prepare pool ──────────────────────────────────────────────
        int requiredCapacity = 4 * n + 64;
        if (_pool == null)
        {
            _pool = new OctreePool(requiredCapacity);
        }
        else
        {
            _pool.Reset();
            _pool.EnsureCapacity(requiredCapacity);
        }

        // ── Step 3: Build octree ──────────────────────────────────────────────
        int root = _pool.Allocate(cx, cy, cz, halfSize);

        for (int i = 0; i < n; i++)
        {
            if (!act[i]) continue;
            InsertBody(_pool, root, i, px[i], py[i], pz[i], m[i]);
        }

        // ── Step 4: Force traversal ───────────────────────────────────────────
        OctreeNode[] nodes = _pool.Nodes;

        if (UseParallel)
        {
            // Parallel outer loop: each thread computes forces for its own bodies.
            // The tree (nodes array) is read-only after construction.
            // Each thread writes only to ax[i], ay[i], az[i] — no data races.
            Parallel.For(0, n, i =>
            {
                if (!act[i]) return;

                double axi = 0.0, ayi = 0.0, azi = 0.0;
                ComputeForceOnBody(nodes, root, i, px[i], py[i], pz[i],
                                   eps2, theta2, ref axi, ref ayi, ref azi);
                ax[i] = axi;
                ay[i] = ayi;
                az[i] = azi;
            });
        }
        else
        {
            // Single-threaded: deterministic traversal order.
            for (int i = 0; i < n; i++)
            {
                if (!act[i]) continue;

                double axi = 0.0, ayi = 0.0, azi = 0.0;
                ComputeForceOnBody(nodes, root, i, px[i], py[i], pz[i],
                                   eps2, theta2, ref axi, ref ayi, ref azi);
                ax[i] = axi;
                ay[i] = ayi;
                az[i] = azi;
            }
        }
    }

    // ── Tree construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Insert a body into the octree at the given node.
    ///
    /// Three cases:
    /// 1. Node is empty (TotalMass == 0, BodyIndex == -1): store body as leaf.
    /// 2. Node is a leaf (BodyIndex != -1): subdivide — push existing body and
    ///    new body into child octants.
    /// 3. Node is internal (has children): recurse into appropriate child octant,
    ///    then update aggregate mass/COM.
    ///
    /// SUBDIVISION DEPTH LIMIT
    /// -----------------------
    /// If two bodies are nearly coincident, subdivision can recurse very deep.
    /// We stop subdividing when the node half-size falls below MinNodeSize.
    /// In this case, both bodies share the same leaf — their masses and COMs
    /// are aggregated, introducing a small error that is physically reasonable
    /// (bodies at the same location exert no net force on each other anyway).
    /// </summary>
    private void InsertBody(OctreePool pool, int nodeIdx, int bodyIdx,
                            double bx, double by, double bz, double bm)
    {
        ref OctreeNode node = ref pool.Nodes[nodeIdx];

        // Case 1: Empty node — make it a leaf
        if (node.TotalMass == 0.0 && node.BodyIndex == -1)
        {
            node.BodyIndex = bodyIdx;
            node.IsLeaf = true;
            node.TotalMass = bm;
            node.ComX = bx;
            node.ComY = by;
            node.ComZ = bz;
            return;
        }

        // Case 2: Node is a leaf — must subdivide
        if (node.IsLeaf)
        {
            // Check minimum node size to prevent infinite recursion
            if (node.HalfSize * 0.5 < MinNodeSize)
            {
                // Bodies are effectively coincident. Aggregate into this leaf.
                // This is physically reasonable: at zero separation, the mutual
                // force is regularized by softening anyway.
                double totalMass = node.TotalMass + bm;
                node.ComX = (node.ComX * node.TotalMass + bx * bm) / totalMass;
                node.ComY = (node.ComY * node.TotalMass + by * bm) / totalMass;
                node.ComZ = (node.ComZ * node.TotalMass + bz * bm) / totalMass;
                node.TotalMass = totalMass;
                // BodyIndex becomes meaningless for multi-body leaves, but we
                // keep it as the first body inserted (for identification only).
                return;
            }

            // Save the existing body's data before converting to internal node
            int existingBody = node.BodyIndex;
            double existMass = node.TotalMass;
            double existX = node.ComX;
            double existY = node.ComY;
            double existZ = node.ComZ;

            // Convert this node from leaf to internal
            node.BodyIndex = -1;
            node.IsLeaf = false;
            node.TotalMass = 0.0;
            node.ComX = 0.0;
            node.ComY = 0.0;
            node.ComZ = 0.0;

            // Re-insert the existing body into the appropriate child
            InsertIntoChild(pool, nodeIdx, existingBody, existX, existY, existZ, existMass);

            // Insert the new body into the appropriate child
            InsertIntoChild(pool, nodeIdx, bodyIdx, bx, by, bz, bm);

            // Update aggregate mass/COM for this internal node
            ref OctreeNode updatedNode = ref pool.Nodes[nodeIdx];
            UpdateMassFromInsertion(ref updatedNode, existMass, existX, existY, existZ);
            UpdateMassFromInsertion(ref updatedNode, bm, bx, by, bz);
            return;
        }

        // Case 3: Internal node — recurse into child and update aggregate
        InsertIntoChild(pool, nodeIdx, bodyIdx, bx, by, bz, bm);
        ref OctreeNode internalNode = ref pool.Nodes[nodeIdx];
        UpdateMassFromInsertion(ref internalNode, bm, bx, by, bz);
    }

    /// <summary>
    /// Helper: insert a body into the correct child octant of the given node.
    /// Allocates a new child node if the octant is empty.
    /// </summary>
    private void InsertIntoChild(OctreePool pool, int parentIdx, int bodyIdx,
                                 double bx, double by, double bz, double bm)
    {
        ref OctreeNode parent = ref pool.Nodes[parentIdx];
        int octant = parent.OctantFor(bx, by, bz);
        int childIdx = parent.GetChild(octant);

        if (childIdx == -1)
        {
            // Allocate a new child node for this octant
            parent.ChildCenter(octant, out double ccx, out double ccy, out double ccz);
            childIdx = pool.Allocate(ccx, ccy, ccz, parent.HalfSize * 0.5);
            // Re-read parent ref after potential pool growth
            pool.Nodes[parentIdx].SetChild(octant, childIdx);
        }

        InsertBody(pool, childIdx, bodyIdx, bx, by, bz, bm);
    }

    /// <summary>
    /// Incrementally update an internal node's aggregate mass and center-of-mass
    /// after a body with mass (bm) at position (bx, by, bz) has been inserted
    /// into one of its subtrees.
    ///
    /// COM update formula (weighted average):
    ///   new_com = (old_com × old_mass + body_pos × body_mass) / new_total_mass
    /// </summary>
    private static void UpdateMassFromInsertion(ref OctreeNode node,
                                                 double bm, double bx, double by, double bz)
    {
        double newMass = node.TotalMass + bm;
        if (newMass > 0.0)
        {
            double invNewMass = 1.0 / newMass;
            node.ComX = (node.ComX * node.TotalMass + bx * bm) * invNewMass;
            node.ComY = (node.ComY * node.TotalMass + by * bm) * invNewMass;
            node.ComZ = (node.ComZ * node.TotalMass + bz * bm) * invNewMass;
        }
        node.TotalMass = newMass;
    }

    // ── Force traversal ───────────────────────────────────────────────────────

    /// <summary>
    /// Compute the gravitational acceleration on body i from the octree.
    ///
    /// Traversal rules at each node:
    ///   1. Empty node (TotalMass == 0) → skip
    ///   2. Leaf containing body i itself → skip (no self-interaction)
    ///   3. Leaf containing another body → compute direct force
    ///   4. Internal node passing θ criterion → approximate as monopole
    ///   5. Internal node failing θ criterion → recurse into children
    ///
    /// OPENING ANGLE CHECK (squared to avoid sqrt):
    ///   (2 × halfSize)² / dist² < θ²
    ///   ⟹  4 × halfSize² < θ² × dist²
    ///
    /// GRAVITATIONAL ACCELERATION (G = 1 in simulation units):
    ///   a_i += -M_node / (dist² + ε²)^(3/2) × (pos_i - com_node)
    /// </summary>
    private static void ComputeForceOnBody(OctreeNode[] nodes, int nodeIdx,
                                            int bodyIdx, double bx, double by, double bz,
                                            double eps2, double theta2,
                                            ref double axi, ref double ayi, ref double azi)
    {
        ref readonly OctreeNode node = ref nodes[nodeIdx];

        // Skip empty nodes
        if (node.TotalMass == 0.0) return;

        // Displacement from body to node's center of mass
        double dx = bx - node.ComX;
        double dy = by - node.ComY;
        double dz = bz - node.ComZ;
        double dist2 = dx * dx + dy * dy + dz * dz;

        if (node.IsLeaf)
        {
            // Skip self-interaction
            if (node.BodyIndex == bodyIdx) return;

            // Direct force computation (same formula as brute-force backends)
            double softDist2 = dist2 + eps2;
            double invDist = 1.0 / System.Math.Sqrt(softDist2);
            double invDist3 = invDist * invDist * invDist;
            double factor = node.TotalMass * invDist3;

            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;
            return;
        }

        // Internal node: check the opening angle criterion.
        // (2 × halfSize)² / dist² < θ²  ⟹  accept monopole approximation
        double nodeSize2 = 4.0 * node.HalfSize * node.HalfSize; // (2h)²

        if (dist2 > 0.0 && nodeSize2 < theta2 * dist2)
        {
            // Node is far enough away — approximate as single point mass.
            // This is the key optimisation: instead of visiting O(n) leaves,
            // we compute one force contribution from the aggregate mass.
            double softDist2 = dist2 + eps2;
            double invDist = 1.0 / System.Math.Sqrt(softDist2);
            double invDist3 = invDist * invDist * invDist;
            double factor = node.TotalMass * invDist3;

            axi -= factor * dx;
            ayi -= factor * dy;
            azi -= factor * dz;
            return;
        }

        // Node is too close or θ criterion not met — recurse into children.
        // Fixed octant order 0–7 ensures deterministic traversal.
        if (node.Child0 != -1) ComputeForceOnBody(nodes, node.Child0, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child1 != -1) ComputeForceOnBody(nodes, node.Child1, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child2 != -1) ComputeForceOnBody(nodes, node.Child2, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child3 != -1) ComputeForceOnBody(nodes, node.Child3, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child4 != -1) ComputeForceOnBody(nodes, node.Child4, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child5 != -1) ComputeForceOnBody(nodes, node.Child5, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child6 != -1) ComputeForceOnBody(nodes, node.Child6, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
        if (node.Child7 != -1) ComputeForceOnBody(nodes, node.Child7, bodyIdx, bx, by, bz, eps2, theta2, ref axi, ref ayi, ref azi);
    }
}
