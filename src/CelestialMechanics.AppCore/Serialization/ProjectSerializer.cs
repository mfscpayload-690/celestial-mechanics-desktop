using System.IO.Compression;
using System.Text.Json;
using AppScene = CelestialMechanics.AppCore.Scene.Scene;
using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.AppCore.Serialization.DTO;
using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Serialization;

/// <summary>
/// Serializes the full project state into a <c>.cesim</c> ZIP archive.
///
/// Internal layout of the archive:
/// <code>
///   project.cesim/
///   ├── ProjectMetadata.json   ← name, version, timestamps
///   ├── Scene.json             ← scene graph tree
///   ├── PhysicsConfig.json     ← physics configuration
///   ├── SimulationState.json   ← runtime state
///   ├── Entities.json          ← entity + component list
///   └── EventHistory.json      ← recorded simulation events
/// </code>
///
/// Thread-safety: <see cref="SaveProject"/> acquires no external locks but suspends
/// the <see cref="SimulationManager"/> step loop by pausing the <c>SimulationManager</c>
/// (caller's responsibility — see remarks in SaveProject).
/// </summary>
public sealed class ProjectSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented              = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling             = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the entire project to <paramref name="filePath"/> (.cesim).
    /// The caller must ensure the simulation is paused or stopped before calling.
    /// </summary>
    /// <param name="filePath">Full path including .cesim extension.</param>
    /// <param name="scene">The scene to serialize.</param>
    /// <param name="manager">The running simulation manager to snapshot.</param>
    public void SaveProject(string filePath, AppScene scene, SimulationManager manager)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(manager);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // Build DTO from live runtime objects
        var dto = BuildProjectDTO(scene, manager);
        scene.MarkModified();

        // Write ZIP archive
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        WriteJsonEntry(archive, "ProjectMetadata.json",  BuildMetadataPayload(dto));
        WriteJsonEntry(archive, "Scene.json",            dto.Scene);
        WriteJsonEntry(archive, "PhysicsConfig.json",    dto.PhysicsConfig);
        WriteJsonEntry(archive, "SimulationState.json",  dto.SimulationState);
        WriteJsonEntry(archive, "Entities.json",         dto.Entities);
        WriteJsonEntry(archive, "EventHistory.json",     dto.EventHistory);
    }

    // ── DTO construction ──────────────────────────────────────────────────────

    private static ProjectDTO BuildProjectDTO(AppScene scene, SimulationManager manager)
    {
        var dto = new ProjectDTO
        {
            Version       = "1.0.0",
            Name          = scene.Name,
            Author        = scene.Author,
            Description   = scene.Description,
            CreatedAt     = scene.CreatedAt,
            LastModifiedAt = DateTime.UtcNow,
            PhysicsConfig  = PhysicsConfigDTO.From(manager.Config),
            SimulationState = BuildSimulationStateDTO(manager),
            Scene          = BuildSceneDTO(scene),
            Entities       = BuildEntityDTOs(manager),
            EventHistory   = new List<SimulationEventDTO>(), // future: pull from EventBus
        };
        return dto;
    }

    private static SimulationStateDTO BuildSimulationStateDTO(SimulationManager manager) => new()
    {
        SimulationTime    = manager.Time.SimulationTime,
        TimeScale         = manager.Time.TimeScale,
        ExpansionRate     = manager.SpaceMetric.HubbleParameter,
        ExpansionEnabled  = manager.SpaceMetric.ExpansionEnabled,
        BarnesHutTheta    = manager.Config.Theta,
        DeterministicMode = manager.Config.DeterministicMode,
        UseBarnesHut      = manager.Config.UseBarnesHut,
        IntegratorName    = manager.Config.IntegratorName,
        ActiveEntityCount = manager.Entities.Count(e => e.IsActive),
    };

    private static SceneDTO BuildSceneDTO(AppScene scene)
    {
        var dto = new SceneDTO
        {
            ProjectId      = scene.ProjectId,
            Name           = scene.Name,
            Author         = scene.Author,
            Description    = scene.Description,
            CreatedAt      = scene.CreatedAt,
            LastModifiedAt = scene.LastModifiedAt,
        };

        // Flatten tree in depth-first order (parents naturally appear before children)
        foreach (var node in scene.Graph.TraverseDepthFirst())
        {
            dto.Nodes.Add(new SceneNodeDTO
            {
                Id             = node.Id,
                Name           = node.Name,
                NodeType       = node.NodeType,
                ParentId       = node.Parent?.Id == Guid.Empty ? null : node.Parent?.Id,
                LinkedEntityId = node.LinkedEntityId,
            });
        }
        return dto;
    }

    private static List<EntityDTO> BuildEntityDTOs(SimulationManager manager)
    {
        var list = new List<EntityDTO>(manager.Entities.Count);
        foreach (var entity in manager.Entities)
        {
            var entityDto = new EntityDTO
            {
                Id       = entity.Id,
                Tag      = entity.Tag,
                IsActive = entity.IsActive,
            };

            foreach (var component in entity.Components)
            {
                var cDto = MapComponentToDTO(component);
                if (cDto != null)
                    entityDto.Components.Add(cDto);
            }
            list.Add(entityDto);
        }
        return list;
    }

    private static ComponentDTO? MapComponentToDTO(IComponent component)
    {
        return component switch
        {
            PhysicsComponent pc => new PhysicsComponentDTO
            {
                Mass        = pc.Mass,
                Radius      = pc.Radius,
                Density     = pc.Density,
                IsCollidable = pc.IsCollidable,
                PositionX   = pc.Position.X,
                PositionY   = pc.Position.Y,
                PositionZ   = pc.Position.Z,
                VelocityX   = pc.Velocity.X,
                VelocityY   = pc.Velocity.Y,
                VelocityZ   = pc.Velocity.Z,
                AccelerationX = pc.Acceleration.X,
                AccelerationY = pc.Acceleration.Y,
                AccelerationZ = pc.Acceleration.Z,
            },
            // Additional components — mapped here when their DTO types are fleshed out
            _ => null
        };
    }

    // ── Metadata manifest ─────────────────────────────────────────────────────

    private static object BuildMetadataPayload(ProjectDTO dto) => new
    {
        dto.Version,
        dto.Name,
        dto.Author,
        dto.Description,
        dto.CreatedAt,
        dto.LastModifiedAt,
        EntityCount   = dto.Entities.Count,
        SceneNodeCount = dto.Scene.Nodes.Count,
        Format        = "cesim",
    };

    // ── ZIP helpers ───────────────────────────────────────────────────────────

    private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T value)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        JsonSerializer.Serialize(entryStream, value, JsonOptions);
    }
}
