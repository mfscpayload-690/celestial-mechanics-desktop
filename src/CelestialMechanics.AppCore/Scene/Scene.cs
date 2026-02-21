namespace CelestialMechanics.AppCore.Scene;

/// <summary>
/// Top-level project container tying together the scene hierarchy,
/// project identity, and an optional runtime link to a running simulation.
///
/// Scene is the object that gets serialized to a <c>.cesim</c> file.
/// </summary>
public sealed class Scene
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Internal project identifier. Survives save/load cycles.</summary>
    public Guid ProjectId { get; }

    public string Name        { get; set; }
    public string Author      { get; set; }
    public string Description { get; set; }

    public DateTime CreatedAt      { get; }
    public DateTime LastModifiedAt { get; set; }

    // ── Scene hierarchy ───────────────────────────────────────────────────────

    public SceneGraph Graph { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public Scene(string name, string author = "")
    {
        ProjectId      = Guid.NewGuid();
        Name           = name;
        Author         = author;
        Description    = string.Empty;
        CreatedAt      = DateTime.UtcNow;
        LastModifiedAt = DateTime.UtcNow;
        Graph          = new SceneGraph();
    }

    /// <summary>
    /// Deserialisation constructor — restores a previously saved scene from DTO data.
    /// </summary>
    public Scene(Guid projectId, string name, string author, string description,
                 DateTime createdAt, DateTime lastModifiedAt)
    {
        ProjectId      = projectId;
        Name           = name;
        Author         = author;
        Description    = description;
        CreatedAt      = createdAt;
        LastModifiedAt = lastModifiedAt;
        Graph          = new SceneGraph();
    }

    /// <summary>
    /// Convenience factory: creates a new scene with a single entity node linked
    /// to an existing ECS entity ID.
    /// </summary>
    public static Scene CreateWithEntity(string sceneName, Guid entityId, string entityName = "Body")
    {
        var scene = new Scene(sceneName);
        var node = new SceneNode(entityName, NodeType.Entity) { LinkedEntityId = entityId };
        scene.Graph.AddNode(Guid.Empty, node);
        return scene;
    }

    /// <summary>Touches <see cref="LastModifiedAt"/> to the current UTC time.</summary>
    public void MarkModified() => LastModifiedAt = DateTime.UtcNow;

    public override string ToString() =>
        $"Scene \"{Name}\" ({ProjectId:D}) — {Graph.Count - 1} nodes";
}
