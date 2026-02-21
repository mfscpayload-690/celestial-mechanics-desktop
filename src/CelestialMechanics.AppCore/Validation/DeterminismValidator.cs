using System.IO;
using AppScene = CelestialMechanics.AppCore.Scene.Scene;
using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.AppCore.Serialization;
using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Validation;

/// <summary>
/// Result of a <see cref="DeterminismValidator"/> run.
/// </summary>
public sealed class ValidationResult
{
    public bool   Passed           { get; init; }
    public double MaxPositionDrift { get; init; }
    public double EnergyDrift      { get; init; }
    public double MomentumDrift    { get; init; }
    public int    StepsRun         { get; init; }
    public string Log              { get; init; } = string.Empty;

    /// <summary>Epsilon tolerance used for the pass/fail decision.</summary>
    public double Epsilon          { get; init; }

    public override string ToString() =>
        $"Determinism [{(Passed ? "PASS" : "FAIL")}] | " +
        $"ΔPos={MaxPositionDrift:E3} | ΔE={EnergyDrift:E3} | Δp={MomentumDrift:E3}";
}

/// <summary>
/// Validates that the simulation is deterministic by running a save → load → re-run cycle
/// and comparing energy, momentum, and entity positions within a configurable epsilon.
///
/// Procedure:
///   1. Capture pre-run metrics from the live manager
///   2. Run N steps, record final state (positions, energy, momentum)
///   3. Save project to a temp .cesim file
///   4. Load project into a fresh manager
///   5. Run the same N steps
///   6. Compare outcomes — report drift
///   7. Delete temp file
/// </summary>
public sealed class DeterminismValidator
{
    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Run the full determinism validation cycle.
    /// </summary>
    /// <param name="manager">The live simulation manager to validate against.</param>
    /// <param name="scene">Scene to persist with the manager.</param>
    /// <param name="steps">Number of simulation steps in each half of the test.</param>
    /// <param name="epsilon">Acceptable drift tolerance (per-entity position delta).</param>
    /// <param name="baseDt">Timestep passed to each <see cref="SimulationManager.Step"/> call.</param>
    public ValidationResult Validate(
        SimulationManager manager,
        AppScene          scene,
        int               steps   = 1000,
        double            epsilon = 1e-6,
        double            baseDt  = 0.001)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(scene);

        var log = new System.Text.StringBuilder();
        log.AppendLine($"=== Determinism Validation: {steps} steps, ε={epsilon:E2} ===");

        // ── 1. Save initial state S0 to temp file ────────────────────────────
        var tempPath = Path.Combine(Path.GetTempPath(), $"cesim_validation_{Guid.NewGuid():N}.cesim");
        log.AppendLine("Phase 0: Saving initial state...");
        try 
        { 
            new ProjectSerializer().SaveProject(tempPath, scene, manager); 
        }
        catch (Exception ex) 
        { 
            return Fail(log, $"Initial save failed: {ex.Message}", epsilon, steps); 
        }

        // ── 2. Run A: advance the provided manager N steps ──────────────────
        log.AppendLine("Phase A: advancing original manager…");
        for (int i = 0; i < steps; i++)
            manager.Step(baseDt);

        var stateA = CaptureMetrics(manager);
        log.AppendLine($"  A (final): energy={stateA.TotalEnergy:E6}, momentum={stateA.MomentumMag:E6}, entities={stateA.Positions.Count}");

        // ── 3. Load S0 into fresh manager B ──────────────────────────────────
        log.AppendLine("Phase B: loading and advancing fresh manager…");
        ProjectLoadResult loaded;
        try
        {
            loaded = new ProjectDeserializer().LoadProject(tempPath);
            if (!loaded.Success)
            {
                log.AppendLine("  Load warnings during reconstruction:");
                foreach (var w in loaded.Warnings) log.AppendLine($"    {w}");
            }
        }
        catch (Exception ex)
        {
            return Fail(log, $"Load failed: {ex.Message}", epsilon, steps);
        }
        finally
        {
            TryDelete(tempPath);
        }

        // ── 4. Run B: advance the loaded manager the same N steps ────────────
        for (int i = 0; i < steps; i++)
            loaded.Manager.Step(baseDt);

        var stateB = CaptureMetrics(loaded.Manager);
        log.AppendLine($"  B (final): energy={stateB.TotalEnergy:E6}, momentum={stateB.MomentumMag:E6}, entities={stateB.Positions.Count}");

        // ── 5. Compare ───────────────────────────────────────────────────────
        double energyDrift   = System.Math.Abs(stateA.TotalEnergy - stateB.TotalEnergy);
        double momentumDrift = System.Math.Abs(stateA.MomentumMag - stateB.MomentumMag);
        double maxPosDrift   = ComputeMaxPositionDrift(stateA.Positions, stateB.Positions, log);

        // Relative epsilon check
        double energyRef = System.Math.Max(1.0, System.Math.Abs(stateA.TotalEnergy));
        double momRef    = System.Math.Max(1.0, stateA.MomentumMag);

        bool passed = maxPosDrift   <= epsilon
                   && energyDrift   <= epsilon * (1.0 + System.Math.Abs(stateA.TotalEnergy))
                   && momentumDrift <= epsilon * (1.0 + stateA.MomentumMag);

        log.AppendLine($"ΔPos_max={maxPosDrift:E3}, ΔE={energyDrift:E3}, Δp={momentumDrift:E3}");
        log.AppendLine($"Result: {(passed ? "PASS ✓" : "FAIL ✗")}");

        return new ValidationResult
        {
            Passed           = passed,
            MaxPositionDrift = maxPosDrift,
            EnergyDrift      = energyDrift,
            MomentumDrift    = momentumDrift,
            StepsRun         = steps,
            Epsilon          = epsilon,
            Log              = log.ToString(),
        };
    }

    // ── Metric capture ────────────────────────────────────────────────────────

    private sealed class Metrics
    {
        public double                      TotalEnergy { get; set; }
        public double                      MomentumMag  { get; set; }
        public Dictionary<Guid, Vec3d>     Positions    { get; } = new();
    }

    private static Metrics CaptureMetrics(SimulationManager manager)
    {
        var m = new Metrics();
        double ke = 0.0, momX = 0.0, momY = 0.0, momZ = 0.0;

        foreach (var entity in manager.Entities)
        {
            if (!entity.IsActive) continue;
            var pc = entity.GetComponent<PhysicsComponent>();
            if (pc == null) continue;

            // KE = ½mv²
            double v2 = pc.Velocity.X * pc.Velocity.X
                      + pc.Velocity.Y * pc.Velocity.Y
                      + pc.Velocity.Z * pc.Velocity.Z;
            ke += 0.5 * pc.Mass * v2;

            // Momentum contributions
            momX += pc.Mass * pc.Velocity.X;
            momY += pc.Mass * pc.Velocity.Y;
            momZ += pc.Mass * pc.Velocity.Z;

            m.Positions[entity.Id] = pc.Position;
        }

        m.TotalEnergy = ke; // potential energy requires full O(n²) pass; KE alone is sufficient for drift detection
        m.MomentumMag = System.Math.Sqrt(momX * momX + momY * momY + momZ * momZ);
        return m;
    }

    private static double ComputeMaxPositionDrift(
        Dictionary<Guid, Vec3d> a,
        Dictionary<Guid, Vec3d> b,
        System.Text.StringBuilder log)
    {
        double maxDrift = 0.0;
        int mismatches  = 0;

        foreach (var (id, posA) in a)
        {
            if (!b.TryGetValue(id, out var posB))
            {
                mismatches++;
                log.AppendLine($"  [WARN] Entity {id} missing from run B");
                continue;
            }

            double dx = posA.X - posB.X;
            double dy = posA.Y - posB.Y;
            double dz = posA.Z - posB.Z;
            double d  = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > maxDrift) maxDrift = d;
        }

        if (mismatches > 0)
            log.AppendLine($"  {mismatches} entity mismatches between runs.");

        return maxDrift;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ValidationResult Fail(System.Text.StringBuilder log, string reason, double eps, int steps)
    {
        log.AppendLine($"[ERROR] {reason}");
        return new ValidationResult
        {
            Passed  = false,
            Log     = log.ToString(),
            Epsilon = eps,
            StepsRun = steps,
        };
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }
}
