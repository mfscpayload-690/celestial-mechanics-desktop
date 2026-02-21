namespace CelestialMechanics.AppCore.Serialization.DTO;

/// <summary>
/// DTO for the simulation runtime state at save time.
/// Captures the fields that differ from <see cref="PhysicsConfigDTO"/> at runtime
/// (current time, scale, expansion state).
/// </summary>
public sealed class SimulationStateDTO
{
    /// <summary>Elapsed simulation time in seconds at the moment of save.</summary>
    public double SimulationTime    { get; set; }

    /// <summary>Current time-scale multiplier.</summary>
    public double TimeScale         { get; set; } = 1.0;

    /// <summary>Hubble-like expansion rate parameter.</summary>
    public double ExpansionRate     { get; set; } = 0.0;

    /// <summary>Whether cosmic expansion is currently enabled.</summary>
    public bool   ExpansionEnabled  { get; set; } = false;

    // Mirror important config fields for quick inspection without loading PhysicsConfigDTO
    public double BarnesHutTheta    { get; set; } = 0.5;
    public bool   DeterministicMode { get; set; } = true;
    public bool   UseBarnesHut      { get; set; } = false;
    public string IntegratorName    { get; set; } = "Verlet";

    // ── Diagnostics (informational, not used to reconstruct state) ─────────
    public int    ActiveEntityCount { get; set; }
    public double TotalEnergy       { get; set; }
}

/// <summary>
/// DTO for a discrete simulation event (supernova, merger, collapse).
/// </summary>
public sealed class SimulationEventDTO
{
    public string    EventType          { get; set; } = string.Empty;
    public double    SimulationTime     { get; set; }
    public Guid      PrimaryEntityId    { get; set; }
    public Guid?     SecondaryEntityId  { get; set; }
    public string    Description        { get; set; } = string.Empty;
}
