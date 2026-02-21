using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.BarnesHut;

/// <summary>
/// Placeholder interface for future multipole expansion support.
///
/// CURRENT STATUS: Architecture-only. No implementation exists yet.
///
/// FUTURE PLANS
/// ------------
/// When implemented, multipole expansions will improve the accuracy of the
/// Barnes-Hut approximation by including higher-order terms beyond the
/// monopole (point-mass) approximation currently used.
///
/// MONOPOLE (current, implemented in BarnesHutBackend):
///   Treats each distant node as a single point mass at the center of mass.
///   Error: O(s/d)² where s = node size, d = distance
///
/// QUADRUPOLE (future, this interface):
///   Adds the quadrupole moment tensor Q_ij, which captures the shape of
///   the mass distribution within a node. This reduces the force error to
///   O(s/d)⁴, allowing larger θ values (faster computation) at the same
///   accuracy level.
///
///   The quadrupole tensor is symmetric and traceless (5 independent
///   components in 3D):
///     Q_ij = Σ_k m_k (3·r_i·r_j - |r|²·δ_ij)
///
/// INTEGRATION POINT
/// -----------------
/// When implemented, the BarnesHutBackend will:
///   1. Call ComputeExpansion() during tree build (bottom-up)
///   2. Call ApplyCorrection() during force traversal (when accepting a node)
///
/// This interface exists so that future work can implement quadrupole (or
/// higher) expansions without modifying the BarnesHutBackend's architecture.
/// </summary>
public interface IMultipoleExpansion
{
    /// <summary>
    /// Compute the multipole expansion coefficients for a tree node,
    /// given the bodies (or child expansions) it contains.
    /// </summary>
    /// <param name="node">The octree node to compute the expansion for.</param>
    /// <param name="bodies">SoA body buffer for accessing positions/masses.</param>
    void ComputeExpansion(ref OctreeNode node, BodySoA bodies);

    /// <summary>
    /// Apply the multipole correction to the force on body <paramref name="bodyIdx"/>,
    /// given the already-computed monopole acceleration.
    ///
    /// The correction is additive: a_total = a_monopole + a_correction.
    /// </summary>
    /// <param name="node">The tree node being approximated.</param>
    /// <param name="bodyIdx">Index of the body receiving the force.</param>
    /// <param name="bx">Body X position.</param>
    /// <param name="by">Body Y position.</param>
    /// <param name="bz">Body Z position.</param>
    /// <param name="dax">Output: X correction to acceleration.</param>
    /// <param name="day">Output: Y correction to acceleration.</param>
    /// <param name="daz">Output: Z correction to acceleration.</param>
    void ApplyCorrection(ref readonly OctreeNode node, int bodyIdx,
                         double bx, double by, double bz,
                         out double dax, out double day, out double daz);

    /// <summary>
    /// The order of this expansion (2 = quadrupole, 3 = octupole, etc.).
    /// </summary>
    int Order { get; }
}
