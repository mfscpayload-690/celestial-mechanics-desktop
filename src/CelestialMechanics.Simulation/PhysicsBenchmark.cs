using System.Diagnostics;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Integrators;
using CelestialMechanics.Physics.SoA;
using CelestialMechanics.Physics.Solvers;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Physics.Validation;

namespace CelestialMechanics.Simulation;

/// <summary>
/// Result for one benchmark run (single body count + backend combination).
/// </summary>
public sealed class BenchmarkResult
{
    public int BodyCount            { get; init; }
    public string BackendName       { get; init; } = "";
    public int Steps                { get; init; }
    public double TotalMs           { get; init; }
    public double MsPerStep         { get; init; }
    public double StepsPerSecond    { get; init; }
    public double EnergyDrift       { get; init; }
    public long MemoryBytes         { get; init; }

    public override string ToString() =>
        $"{BackendName,-22} | n={BodyCount,5} | {MsPerStep,8:F3} ms/step | " +
        $"{StepsPerSecond,8:F0} steps/s | drift={EnergyDrift:E3} | " +
        $"mem={MemoryBytes / 1024.0:F0} KB";
}

/// <summary>
/// Performance benchmark suite for the N-body physics engine.
///
/// WHAT IS MEASURED
/// ----------------
/// • ms per physics step   — wall-clock time for one solver Step() call.
/// • steps per second      — for comparison to real-time requirements.
/// • Energy drift          — |ΔE/E₀| after <see cref="DefaultSteps"/> steps;
///                           measures numerical stability under each backend.
/// • Memory usage          — approximate heap allocation for the backend.
///
/// BACKEND COMPARISON (Phase 3)
/// ----------------------------
/// The benchmark compares four backend configurations:
/// 1. O(n²) single-thread       — CpuSingleThreadBackend (SoA)
/// 2. O(n²) parallel            — CpuParallelBackend (SoA, Parallel.For)
/// 3. Barnes-Hut single-thread  — BarnesHutBackend (θ=0.5, deterministic)
/// 4. Barnes-Hut parallel       — BarnesHutBackend (θ=0.5, Parallel.For)
///
/// SCALING EXPECTATIONS
/// --------------------
/// For n bodies:
///   O(n²) backends:     ms ∝ n²         (exact, no approximation)
///   Barnes-Hut:         ms ∝ n·log(n)   (approximate, controlled by θ)
///
/// At n=10,000: O(n²) = 10⁸ pairs; BH ≈ 10⁴·13 ≈ 130k interactions
/// Expected speedup: ~700x at n=10k.
///
/// MEMORY ANALYSIS
/// ---------------
/// SoA arrays:    14 arrays × 8 bytes × n = 112n bytes
/// Octree pool:   ~148 bytes × 4n = 592n bytes (Barnes-Hut only)
/// Total BH:      ~704n bytes ≈ 13.4 MB for 20k bodies
/// </summary>
public sealed class PhysicsBenchmark
{
    /// <summary>Standard body counts used by <see cref="RunAll"/>.</summary>
    public static readonly IReadOnlyList<int> DefaultBodyCounts = new[] { 100, 1000, 5000, 10000, 20000 };

    /// <summary>Number of physics steps per benchmark run.</summary>
    public const int DefaultSteps = 10_000;

    /// <summary>
    /// Maximum body count for O(n²) backends. Beyond this, brute-force
    /// is too slow for a reasonable benchmark time.
    /// </summary>
    private const int MaxBruteForceN = 5000;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run benchmarks for all default body counts and all backends, then
    /// print a formatted report to <see cref="Console.Out"/>.
    /// </summary>
    public void RunAll()
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║            CELESTIAL MECHANICS — PHYSICS BENCHMARK SUITE (Phase 3)          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  Steps per run : {DefaultSteps:N0}");
        Console.WriteLine($"  Thread count  : {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"  Platform      : .NET {Environment.Version}");
        Console.WriteLine($"  Barnes-Hut θ  : 0.5");
        Console.WriteLine();

        var allResults = new List<BenchmarkResult>();

        foreach (int n in DefaultBodyCounts)
        {
            Console.WriteLine($"── n = {n} bodies ───────────────────────────────────────────────────────────");

            // Brute-force backends (skip for very large n)
            if (n <= MaxBruteForceN)
            {
                var bruteSTResult = Run(n, BackendMode.SoASingleThread, DefaultSteps);
                var brutePResult  = Run(n, BackendMode.SoAParallel,     DefaultSteps);
                Console.WriteLine($"  {bruteSTResult}");
                Console.WriteLine($"  {brutePResult}");
                allResults.Add(bruteSTResult);
                allResults.Add(brutePResult);
            }
            else
            {
                Console.WriteLine($"  O(n²) backends skipped for n={n} (too slow)");
            }

            // Barnes-Hut backends
            var bhSingleResult   = Run(n, BackendMode.BarnesHutSingle,   DefaultSteps);
            var bhParallelResult = Run(n, BackendMode.BarnesHutParallel, DefaultSteps);
            Console.WriteLine($"  {bhSingleResult}");
            Console.WriteLine($"  {bhParallelResult}");
            allResults.Add(bhSingleResult);
            allResults.Add(bhParallelResult);

            // Print speedup comparison where both data points exist
            if (n <= MaxBruteForceN)
            {
                var bruteSTResult = allResults.FindLast(r => r.BodyCount == n && r.BackendName == BackendMode.SoASingleThread.ToString());
                if (bruteSTResult != null)
                {
                    Console.WriteLine($"    → BH speedup vs O(n²)-ST: {bruteSTResult.MsPerStep / bhSingleResult.MsPerStep:F1}x");
                    Console.WriteLine($"    → BH-Parallel speedup vs O(n²)-ST: {bruteSTResult.MsPerStep / bhParallelResult.MsPerStep:F1}x");
                }
            }

            Console.WriteLine();
        }

        PrintSummaryTable(allResults);
        PrintAccuracyAnalysis();
        PrintMemoryAnalysis();
    }

    /// <summary>
    /// Run a single benchmark for the given body count and backend.
    /// </summary>
    public BenchmarkResult Run(int bodyCount, BackendMode mode, int steps = DefaultSteps)
    {
        PhysicsBody[] bodies = GenerateBodies(bodyCount);

        // Configure solver according to the requested mode.
        var solver = new NBodySolver();
        solver.AddForce(new NewtonianGravity { SofteningEpsilon = 1e-4 });

        switch (mode)
        {
            case BackendMode.AoSSingleThread:
                solver.SetIntegrator(new VerletIntegrator());
                solver.ConfigureSoA(enabled: false, softening: 1e-4);
                break;

            case BackendMode.SoASingleThread:
                solver.ConfigureSoA(enabled: true, softening: 1e-4,
                                    deterministic: true, useParallel: false);
                break;

            case BackendMode.SoAParallel:
                solver.ConfigureSoA(enabled: true, softening: 1e-4,
                                    deterministic: false, useParallel: true);
                break;

            case BackendMode.BarnesHutSingle:
                solver.ConfigureSoA(enabled: true, softening: 1e-4,
                                    deterministic: true, useParallel: false,
                                    useBarnesHut: true, theta: 0.5);
                break;

            case BackendMode.BarnesHutParallel:
                solver.ConfigureSoA(enabled: true, softening: 1e-4,
                                    deterministic: false, useParallel: true,
                                    useBarnesHut: true, theta: 0.5);
                break;
        }

        // Memory measurement: snapshot before and after first step
        long memBefore = GC.GetTotalMemory(true);

        // Warm-up: run a few steps so JIT compilation and OS thread-pool
        // spin-up don't distort the measurement.
        for (int i = 0; i < 10; i++)
            solver.Step(bodies, 0.001);

        long memAfter = GC.GetTotalMemory(false);
        long memUsed = System.Math.Max(0, memAfter - memBefore);

        // Reset body state and solver diagnostics after warm-up.
        bodies = GenerateBodies(bodyCount);
        solver.Reset();

        // Timed run.
        var sw = Stopwatch.StartNew();
        SimulationState? state = null;
        for (int i = 0; i < steps; i++)
            state = solver.Step(bodies, 0.001);
        sw.Stop();

        double totalMs   = sw.Elapsed.TotalMilliseconds;
        double msPerStep = totalMs / steps;

        return new BenchmarkResult
        {
            BodyCount      = bodyCount,
            BackendName    = mode.ToString(),
            Steps          = steps,
            TotalMs        = totalMs,
            MsPerStep      = msPerStep,
            StepsPerSecond = 1000.0 / msPerStep,
            EnergyDrift    = state is null ? double.NaN : System.Math.Abs(state.EnergyDrift),
            MemoryBytes    = memUsed
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a stable N-body configuration: bodies arranged in a ring
    /// with circular orbit velocities, so the system stays bound and the
    /// energy drift measurement is meaningful.
    /// </summary>
    private static PhysicsBody[] GenerateBodies(int count)
    {
        var bodies = new PhysicsBody[count];
        double totalMass = (double)count;
        double ringRadius = 5.0;

        for (int i = 0; i < count; i++)
        {
            double angle = 2.0 * System.Math.PI * i / count;
            double x = ringRadius * System.Math.Cos(angle);
            double z = ringRadius * System.Math.Sin(angle);

            // Approximate circular orbit speed around centre of mass.
            double v = System.Math.Sqrt(PhysicalConstants.G_Sim * totalMass / ringRadius) * 0.5;
            double vx = -v * System.Math.Sin(angle);
            double vz =  v * System.Math.Cos(angle);

            bodies[i] = new PhysicsBody(i, mass: 1.0,
                position: new Vec3d(x, 0, z),
                velocity: new Vec3d(vx, 0, vz),
                type: BodyType.Star)
            {
                IsActive       = true,
                Radius         = 0.05,
                GravityStrength = 60,
                GravityRange   = 0
            };
        }

        return bodies;
    }

    private void PrintSummaryTable(IReadOnlyList<BenchmarkResult> results)
    {
        Console.WriteLine("── SUMMARY: ms/step ───────────────────────────────────────────────────────────");
        Console.WriteLine($"  {"Backend",-22} " +
                          string.Concat(DefaultBodyCounts.Select(n => $"| n={n,5}  ")));
        Console.WriteLine(new string('─', 22 + DefaultBodyCounts.Count * 11 + 3));

        foreach (var backend in Enum.GetValues<BackendMode>())
        {
            string row = $"  {backend,-22} ";
            foreach (int n in DefaultBodyCounts)
            {
                var r = results.FirstOrDefault(x => x.BodyCount == n && x.BackendName == backend.ToString());
                row += r is null ? $"| {"N/A",7}  " : $"| {r.MsPerStep,7:F3}  ";
            }
            Console.WriteLine(row);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Print accuracy analysis comparing Barnes-Hut approximation error
    /// at different θ values against exact brute-force computation.
    /// </summary>
    private static void PrintAccuracyAnalysis()
    {
        Console.WriteLine("── ACCURACY ANALYSIS: Barnes-Hut vs Brute-Force ─────────────────────────────");
        Console.WriteLine("  Comparing force accuracy for 500-body ring at various θ values:");
        Console.WriteLine();

        var analyzer = new ForceErrorAnalyzer();
        var bodies500 = GenerateBodies(500);
        var soaBodies = new BodySoA(512);
        soaBodies.CopyFrom(bodies500);

        double[] thetaValues = { 0.3, 0.5, 0.7, 1.0, 1.5 };

        Console.WriteLine($"  {"θ",5} | {"Mean Error",12} | {"Max Error",12} | {"RMS Error",12}");
        Console.WriteLine("  " + new string('─', 55));

        for (int t = 0; t < thetaValues.Length; t++)
        {
            // Re-create SoA each time since ComputeForces modifies AccX/Y/Z
            var soa = new BodySoA(512);
            soa.CopyFrom(bodies500);

            var result = analyzer.CompareForces(soa, softening: 1e-4, theta: thetaValues[t]);
            Console.WriteLine($"  {thetaValues[t],5:F1} | {result.MeanRelativeError,12:E3} | " +
                              $"{result.MaxRelativeError,12:E3} | {result.RmsRelativeError,12:E3}");
        }

        Console.WriteLine();
        Console.WriteLine("  Interpretation:");
        Console.WriteLine("    θ = 0.5 is the recommended default (< 0.1% mean force error).");
        Console.WriteLine("    θ < 0.3 provides near-exact results at reduced speed benefit.");
        Console.WriteLine("    θ > 1.0 trades significant accuracy for speed (visual-only).");
        Console.WriteLine();
    }

    /// <summary>
    /// Print a static memory-layout analysis.
    /// </summary>
    private static void PrintMemoryAnalysis()
    {
        Console.WriteLine("── MEMORY LAYOUT ANALYSIS ─────────────────────────────────────────────────────");
        Console.WriteLine("""
  SoA ARRAYS (all backends)
  ─────────────────────────
  14 contiguous double[] arrays × 8 bytes/element:
    PosX, PosY, PosZ, VelX, VelY, VelZ, AccX, AccY, AccZ,
    OldAccX, OldAccY, OldAccZ, Mass + 1 bool[] IsActive
  Total: ~113n bytes (n = body count)
  For n=20,000: ~2.2 MB (fits in L3 cache)

  OCTREE POOL (Barnes-Hut only)
  ─────────────────────────────
  Pool of OctreeNode structs, each ~148 bytes:
    - Bounding cube: 4 doubles (32 bytes)
    - Mass + COM: 4 doubles (32 bytes)
    - 8 child indices: 8 ints (32 bytes)
    - BodyIndex + IsLeaf + padding: ~52 bytes
  Pool size: 4n + 64 nodes (empirical upper bound)
  For n=20,000: 80,064 nodes × 148 bytes ≈ 11.3 MB
  Allocated ONCE, reused every step via Reset(). Zero GC pressure.

  TOTAL MEMORY (Barnes-Hut, n=20,000)
  ────────────────────────────────────
  SoA arrays: 2.2 MB + Octree pool: 11.3 MB = ~13.5 MB
  All pre-allocated. No per-step heap allocations.
""");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();
    }
}

/// <summary>Backend/path selector for <see cref="PhysicsBenchmark"/>.</summary>
public enum BackendMode
{
    /// <summary>Legacy AoS PhysicsBody[] path with single-threaded Verlet.</summary>
    AoSSingleThread,
    /// <summary>SoA BodySoA path with CpuSingleThreadBackend.</summary>
    SoASingleThread,
    /// <summary>SoA BodySoA path with CpuParallelBackend (Parallel.For).</summary>
    SoAParallel,
    /// <summary>Barnes-Hut O(n log n) backend, single-threaded traversal.</summary>
    BarnesHutSingle,
    /// <summary>Barnes-Hut O(n log n) backend, parallel traversal.</summary>
    BarnesHutParallel
}


