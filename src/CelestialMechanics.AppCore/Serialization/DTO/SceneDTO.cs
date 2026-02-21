using CelestialMechanics.AppCore.Scene;

namespace CelestialMechanics.AppCore.Serialization.DTO;

/// <summary>
/// DTO representing a single SceneNode for flat-list serialisation.
/// The hierarchical tree is rebuilt during deserialisation using <see cref="ParentId"/>.
/// </summary>
public sealed class SceneNodeDTO
{
    public Guid     Id             { get; set; }
    public string   Name           { get; set; } = string.Empty;
    public NodeType NodeType       { get; set; } = NodeType.Folder;
    public Guid?    ParentId       { get; set; }   // null → direct child of root
    public Guid?    LinkedEntityId { get; set; }   // null → organizational node
}

/// <summary>
/// DTO for the full scene graph — stored as a flat list of nodes.
/// </summary>
public sealed class SceneDTO
{
    // Scene identity
    public Guid     ProjectId      { get; set; }
    public string   Name           { get; set; } = string.Empty;
    public string   Author         { get; set; } = string.Empty;
    public string   Description    { get; set; } = string.Empty;
    public DateTime CreatedAt      { get; set; }
    public DateTime LastModifiedAt { get; set; }

    /// <summary>
    /// Flat list of all nodes. Order is depth-first to minimise parent-lookup
    /// during tree reconstruction (parents should appear before children).
    /// </summary>
    public List<SceneNodeDTO> Nodes { get; set; } = new();
}
