namespace CelestialMechanics.AppCore.Serialization.DTO;

/// <summary>
/// Root DTO for a complete <c>.cesim</c> project.
/// This is the top-level object that orchestrates deserialization of all other DTOs.
/// </summary>
public sealed class ProjectDTO
{
    // ── Schema versioning ─────────────────────────────────────────────────────
    /// <summary>
    /// Semantic version of the .cesim file format, e.g. "1.0.0".
    /// Checked during load to emit warnings for newer or incompatible formats.
    /// </summary>
    public string Version     { get; set; } = "1.0.0";

    // ── Project identity ──────────────────────────────────────────────────────
    public string Name        { get; set; } = string.Empty;
    public string Author      { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt      { get; set; }
    public DateTime LastModifiedAt { get; set; }

    // ── Payload references ────────────────────────────────────────────────────
    // These are serialised into separate files inside the .cesim ZIP, so they
    // are NOT embedded here at runtime. ProjectDTO acts as the metadata manifest,
    // and ProjectSerializer reads/writes each section into its own ZIP entry.

    /// <summary>Physics configuration at save time.</summary>
    public PhysicsConfigDTO PhysicsConfig { get; set; } = new();

    /// <summary>Simulation runtime state at save time.</summary>
    public SimulationStateDTO SimulationState { get; set; } = new();

    /// <summary>All entities present at save time.</summary>
    public List<EntityDTO> Entities { get; set; } = new();

    /// <summary>Scene hierarchy.</summary>
    public SceneDTO Scene { get; set; } = new();

    /// <summary>Ordered list of discrete simulation events.</summary>
    public List<SimulationEventDTO> EventHistory { get; set; } = new();
}
