using CelestialMechanics.Physics.SoA;

namespace CelestialMechanics.Physics.Solvers;

/// <summary>
/// CUDA GPU backend stub for N-body force calculation.
///
/// ARCHITECTURE (NOT YET IMPLEMENTED)
/// -----------------------------------
/// This backend is architecturally prepared for GPU acceleration but the
/// actual CUDA interop is not yet implemented. The memory flow model:
///
///   1. HOST → DEVICE: Copy PosX, PosY, PosZ, Mass, IsActive to GPU
///   2. KERNEL: Compute pairwise gravitational accelerations on GPU
///   3. DEVICE → HOST: Copy AccX, AccY, AccZ back to CPU
///   4. CPU performs integration (Verlet kick-drift-kick)
///   5. CPU performs collision resolution
///
/// The GPU never mutates body count or performs structural changes.
/// SoA layout maps directly to GPU device arrays (contiguous doubles).
///
/// IMPLEMENTATION OPTIONS
/// ----------------------
///   • ILGPU — pure C# GPU compiler targeting CUDA/OpenCL
///   • CUDA P/Invoke — native CUDA kernels compiled with nvcc
///   • OpenGL/Vulkan compute shaders — requires Silk.NET integration
///
/// REQUIREMENTS FOR FUTURE IMPLEMENTATION
/// ---------------------------------------
///   • Device memory pool (avoid per-frame allocation)
///   • Double-precision support required (fp64)
///   • Pinned host memory for async copy
///   • Stream-based overlap of copy and compute
/// </summary>
public sealed class CudaPhysicsBackend : IPhysicsComputeBackend
{
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">
    /// Always thrown. CUDA backend is architecturally prepared but not yet implemented.
    /// </exception>
    public void ComputeForces(BodySoA bodies, double softening)
    {
        throw new NotImplementedException(
            "CudaPhysicsBackend is a stub. To enable GPU acceleration, implement the " +
            "CUDA kernel or integrate ILGPU. The memory model is: " +
            "Host→Device copy of position/mass arrays, GPU kernel computes accelerations, " +
            "Device→Host copy of AccX/AccY/AccZ. CPU performs integration and collisions.");
    }
}
