using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.AppCore.Serialization.DTO;

/// <summary>
/// Full mirror of <see cref="PhysicsConfig"/> for serialisation.
/// Explicit mapping avoids coupling the file format to internal implementation details.
/// </summary>
public sealed class PhysicsConfigDTO
{
    // ── Core ──────────────────────────────────────────────────────────────────
    public double TimeStep           { get; set; } = 0.001;
    public double SofteningEpsilon   { get; set; } = 1e-4;
    public string IntegratorName     { get; set; } = "Verlet";
    public double GravityRangeScale  { get; set; } = 1000.0;

    // ── Compute paths ─────────────────────────────────────────────────────────
    public bool   UseSoAPath            { get; set; } = true;
    public bool   DeterministicMode     { get; set; } = true;
    public bool   UseParallelComputation { get; set; } = false;

    // ── Barnes-Hut ───────────────────────────────────────────────────────────
    public bool   UseBarnesHut  { get; set; } = false;
    public double Theta         { get; set; } = 0.5;
    public string SofteningMode { get; set; } = "Constant";
    public double SofteningValue { get; set; } = 1e-4;

    // ── Collisions & Adaptive ─────────────────────────────────────────────────
    public bool   EnableCollisions     { get; set; } = false;
    public bool   UseAdaptiveTimestep  { get; set; } = false;
    public double MinDt                { get; set; } = 1e-6;
    public double MaxDt                { get; set; } = 0.01;

    // ── SIMD ──────────────────────────────────────────────────────────────────
    public bool UseSimd { get; set; } = false;

    // ── Relativistic & high-energy ────────────────────────────────────────────
    public bool   EnablePostNewtonian            { get; set; } = false;
    public bool   EnableGravitationalLensing     { get; set; } = false;
    public bool   EnableAccretionDisks           { get; set; } = false;
    public bool   EnableGravitationalWaves       { get; set; } = false;
    public bool   EnableJetEmission              { get; set; } = false;
    public int    MaxAccretionParticles          { get; set; } = 5000;
    public double AccretionJetThreshold          { get; set; } = 0.1;
    public double GravitationalWaveObserverDistance { get; set; } = 1000.0;

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>Creates a DTO from a runtime <see cref="PhysicsConfig"/>.</summary>
    public static PhysicsConfigDTO From(PhysicsConfig cfg) => new()
    {
        TimeStep                        = cfg.TimeStep,
        SofteningEpsilon                = cfg.SofteningEpsilon,
        IntegratorName                  = cfg.IntegratorName,
        GravityRangeScale               = cfg.GravityRangeScale,
        UseSoAPath                      = cfg.UseSoAPath,
        DeterministicMode               = cfg.DeterministicMode,
        UseParallelComputation          = cfg.UseParallelComputation,
        UseBarnesHut                    = cfg.UseBarnesHut,
        Theta                           = cfg.Theta,
        SofteningMode                   = cfg.SofteningMode.ToString(),
        SofteningValue                  = cfg.SofteningValue,
        EnableCollisions                = cfg.EnableCollisions,
        UseAdaptiveTimestep             = cfg.UseAdaptiveTimestep,
        MinDt                           = cfg.MinDt,
        MaxDt                           = cfg.MaxDt,
        UseSimd                         = cfg.UseSimd,
        EnablePostNewtonian             = cfg.EnablePostNewtonian,
        EnableGravitationalLensing      = cfg.EnableGravitationalLensing,
        EnableAccretionDisks            = cfg.EnableAccretionDisks,
        EnableGravitationalWaves        = cfg.EnableGravitationalWaves,
        EnableJetEmission               = cfg.EnableJetEmission,
        MaxAccretionParticles           = cfg.MaxAccretionParticles,
        AccretionJetThreshold           = cfg.AccretionJetThreshold,
        GravitationalWaveObserverDistance = cfg.GravitationalWaveObserverDistance,
    };

    /// <summary>Converts this DTO back to a runtime <see cref="PhysicsConfig"/>.</summary>
    public PhysicsConfig ToRuntime()
    {
        var mode = Enum.TryParse<SofteningMode>(SofteningMode, out var sm)
            ? sm
            : Physics.Types.SofteningMode.Constant;

        return new PhysicsConfig
        {
            TimeStep                        = TimeStep,
            SofteningEpsilon                = SofteningEpsilon,
            IntegratorName                  = IntegratorName,
            GravityRangeScale               = GravityRangeScale,
            UseSoAPath                      = UseSoAPath,
            DeterministicMode               = DeterministicMode,
            UseParallelComputation          = UseParallelComputation,
            UseBarnesHut                    = UseBarnesHut,
            Theta                           = Theta,
            SofteningMode                   = mode,
            SofteningValue                  = SofteningValue,
            EnableCollisions                = EnableCollisions,
            UseAdaptiveTimestep             = UseAdaptiveTimestep,
            MinDt                           = MinDt,
            MaxDt                           = MaxDt,
            UseSimd                         = UseSimd,
            EnablePostNewtonian             = EnablePostNewtonian,
            EnableGravitationalLensing      = EnableGravitationalLensing,
            EnableAccretionDisks            = EnableAccretionDisks,
            EnableGravitationalWaves        = EnableGravitationalWaves,
            EnableJetEmission               = EnableJetEmission,
            MaxAccretionParticles           = MaxAccretionParticles,
            AccretionJetThreshold           = AccretionJetThreshold,
            GravitationalWaveObserverDistance = GravitationalWaveObserverDistance,
        };
    }
}
