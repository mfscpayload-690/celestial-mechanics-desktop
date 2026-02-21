namespace CelestialMechanics.AppCore.Serialization.DTO;

/// <summary>
/// DTO for the entity identity fields (a single ECS Entity).
/// Components are stored as a polymorphic list to survive the DTO→runtime round-trip
/// without reflection on runtime types.
/// </summary>
public sealed class EntityDTO
{
    public Guid   Id       { get; set; }
    public string Tag      { get; set; } = string.Empty;
    public bool   IsActive { get; set; } = true;

    /// <summary>All components attached to this entity.</summary>
    public List<ComponentDTO> Components { get; set; } = new();
}
