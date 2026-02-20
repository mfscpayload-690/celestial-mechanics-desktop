using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// Compute backend for N-body force calculation.
///
/// Responsibility: given a <see cref="BodySoA"/> buffer, fill the
/// AccX/AccY/AccZ arrays with the net gravitational acceleration for every
/// active body. The caller (NBodySolver) owns all integration logic; the
/// backend owns only the O(n²) pairwise force summation.
///
/// Current implementations:
///   • <see cref="CpuSingleThreadBackend"/> — deterministic, single-threaded
///   • <see cref="CpuParallelBackend"/>    — high-performance, multi-threaded
///
/// Future implementations (architecture is ready, no changes needed here):
///   • CudaBackend        — GPU via CUDA P/Invoke or ILGPU
///   • ComputeShaderBackend — GPU via OpenGL/Vulkan compute shaders
/// </summary>
public interface IPhysicsComputeBackend
{
    /// <summary>
    /// Compute gravitational accelerations for all active bodies in
    /// <paramref name="bodies"/> and write the results into
    /// <c>bodies.AccX</c>, <c>bodies.AccY</c>, <c>bodies.AccZ</c>.
    /// </summary>
    /// <param name="bodies">SoA body buffer (positions, masses, active flags).</param>
    /// <param name="softening">
    ///     Softening length ε. Added in quadrature: r_eff = sqrt(r² + ε²).
    ///     Prevents the force from diverging at r→0.
    /// </param>
    void ComputeForces(BodySoA bodies, double softening);
}
