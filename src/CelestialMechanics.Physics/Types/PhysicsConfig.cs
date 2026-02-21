namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Softening kernel mode for gravitational potential regularisation.
///
/// Softening prevents the force from diverging when two bodies approach
/// zero separation. The effective distance is modified as:
///
///   Constant: r_eff = sqrt(r² + ε²)        — simple additive softening
///   Plummer:  r_eff = sqrt(r² + ε²)        — same formula, but ε is
///             interpreted as the Plummer scale length, giving the
///             potential U = -G·m₁·m₂ / sqrt(r² + ε²)
///
/// Both modes use the same mathematical formula; the distinction is
/// semantic and controls how ε is documented and interpreted in outputs.
/// </summary>
public enum SofteningMode
{
    /// <summary>Constant softening length ε added in quadrature.</summary>
    Constant,
    /// <summary>Plummer softening: ε is the Plummer scale length a.</summary>
    Plummer
}

/// <summary>
/// Configuration parameters for the physics simulation.
/// </summary>
public class PhysicsConfig
{
    public double TimeStep { get; set; } = 0.001;
    public double SofteningEpsilon { get; set; } = 1e-4;
    public string IntegratorName { get; set; } = "Verlet";
    public double GravityRangeScale { get; set; } = 1000.0;

    /// <summary>
    /// When true, the N-body solver uses the high-performance SoA (Structure-of-Arrays)
    /// Verlet integrator backed by an <c>IPhysicsComputeBackend</c> instead of the
    /// legacy AoS per-body path. Provides 2–3x cache-efficiency improvement.
    /// </summary>
    public bool UseSoAPath { get; set; } = true;

    /// <summary>
    /// When true (default), the solver uses <c>CpuSingleThreadBackend</c> for fully
    /// deterministic, reproducible results regardless of CPU thread scheduling.
    /// When false, <c>CpuParallelBackend</c> is used for maximum throughput.
    ///
    /// Deterministic → single-threaded (reproducible)
    /// High-performance → parallel (faster, but floating-point addition order
    /// may vary across runs due to thread scheduling).
    /// </summary>
    public bool DeterministicMode { get; set; } = true;

    /// <summary>
    /// When true AND <see cref="DeterministicMode"/> is false, the force calculation
    /// loop is parallelised over body index using <c>Parallel.For</c>.
    /// Has no effect when <see cref="DeterministicMode"/> is true.
    /// </summary>
    public bool UseParallelComputation { get; set; } = false;

    // ── Phase 3: Barnes-Hut configuration ─────────────────────────────────────

    /// <summary>
    /// When true, the solver uses the Barnes-Hut O(n log n) octree backend
    /// instead of the O(n²) brute-force backend. Recommended for n > 1000.
    ///
    /// When false (default), the existing brute-force backends are used.
    /// Small systems (n &lt; 500) may be faster with brute force due to
    /// the overhead of tree construction.
    /// </summary>
    public bool UseBarnesHut { get; set; } = false;

    /// <summary>
    /// Barnes-Hut opening angle parameter θ (theta).
    ///
    /// Controls the accuracy/speed trade-off of the tree approximation:
    ///   θ = 0.0  → equivalent to exact O(n²) computation
    ///   θ = 0.5  → standard value, ~0.1% force error (default)
    ///   θ = 1.0  → faster but ~1% force error
    ///   θ &gt; 1.0  → visual-only quality, significant force errors
    ///
    /// The opening angle criterion: if (node_size / distance) &lt; θ,
    /// the node is approximated as a single point mass.
    /// </summary>
    public double Theta { get; set; } = 0.5;

    /// <summary>
    /// Alias for <see cref="Theta"/>. Provided for naming consistency with
    /// <see cref="UseBarnesHut"/>.
    /// </summary>
    public double BarnesHutTheta
    {
        get => Theta;
        set => Theta = value;
    }

    /// <summary>
    /// Softening kernel mode. See <see cref="Types.SofteningMode"/> for details.
    /// Default is <see cref="Types.SofteningMode.Constant"/>.
    /// </summary>
    public SofteningMode SofteningMode { get; set; } = SofteningMode.Constant;

    /// <summary>
    /// Softening value ε used by both brute-force and Barnes-Hut backends.
    /// Interpreted according to <see cref="SofteningMode"/>.
    /// Defaults to <see cref="SofteningEpsilon"/> for backward compatibility.
    /// </summary>
    public double SofteningValue { get; set; } = 1e-4;

    // ── Phase 4: Collision & Adaptive Timestep ────────────────────────────────

    /// <summary>
    /// When true, sphere-sphere collision detection and merge resolution
    /// run after each integration step.
    /// </summary>
    public bool EnableCollisions { get; set; } = false;

    /// <summary>
    /// When true, the simulation engine adjusts dt based on the maximum
    /// acceleration in the system to prevent tunneling and instability.
    /// Has no effect when <see cref="DeterministicMode"/> is true.
    /// </summary>
    public bool UseAdaptiveTimestep { get; set; } = false;

    /// <summary>Minimum allowed timestep for adaptive mode.</summary>
    public double MinDt { get; set; } = 1e-6;

    /// <summary>Maximum allowed timestep for adaptive mode.</summary>
    public double MaxDt { get; set; } = 0.01;

    // ── Phase 5: SIMD ─────────────────────────────────────────────────────────

    /// <summary>
    /// When true AND using SoA brute-force (not Barnes-Hut), the solver uses
    /// the SIMD-vectorised backend for ~2× throughput improvement.
    /// </summary>
    public bool UseSimd { get; set; } = false;

    // ── Phase 6: Relativistic & High-Energy Physics ──────────────────────────

    /// <summary>
    /// When true, first Post-Newtonian (1PN) corrections are applied to
    /// gravitational accelerations. Adds terms proportional to v²/c² and GM/(rc²).
    /// </summary>
    public bool EnablePostNewtonian { get; set; } = false;

    /// <summary>
    /// When true, gravitational lensing post-processing is applied to the render
    /// pass for black holes. Only affects rendering; no physics impact.
    /// </summary>
    public bool EnableGravitationalLensing { get; set; } = false;

    /// <summary>
    /// When true, accretion disk particle effects are spawned when matter is
    /// absorbed by a compact object (black hole or neutron star).
    /// </summary>
    public bool EnableAccretionDisks { get; set; } = false;

    /// <summary>
    /// When true, gravitational wave strain is estimated for binary pairs and
    /// recorded into a waveform buffer each timestep.
    /// </summary>
    public bool EnableGravitationalWaves { get; set; } = false;

    /// <summary>
    /// When true AND <see cref="EnableAccretionDisks"/> is true, relativistic
    /// jet particles are emitted along the spin axis when the accretion rate
    /// exceeds <see cref="AccretionJetThreshold"/>.
    /// </summary>
    public bool EnableJetEmission { get; set; } = false;

    /// <summary>Maximum number of accretion disk particles per compact object.</summary>
    public int MaxAccretionParticles { get; set; } = 5000;

    /// <summary>
    /// Mass accretion rate threshold (in sim units/timestep) above which
    /// relativistic jets are emitted.
    /// </summary>
    public double AccretionJetThreshold { get; set; } = 0.1;

    /// <summary>
    /// Observer distance from the system centre in simulation units (AU).
    /// Used for gravitational wave strain calculation: h ∝ 1/d.
    /// </summary>
    public double GravitationalWaveObserverDistance { get; set; } = 1000.0;
}
