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
/// Result of a <see cref="ProjectDeserializer.LoadProject"/> call.
/// </summary>
public sealed class ProjectLoadResult
{
    public AppScene  Scene   { get; init; } = null!;
    public SimulationManager Manager { get; init; } = null!;
    public List<string>      Warnings { get; init; } = new();
    public bool              Success => Warnings.All(w => !w.StartsWith("[ERROR]"));
}

/// <summary>
/// Reads a <c>.cesim</c> ZIP archive and reconstructs the runtime scene + simulation manager.
/// </summary>
public sealed class ProjectDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true,
        NumberHandling             = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private const string CurrentVersion = "1.0.0";

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a <c>.cesim</c> file and reconstructs all runtime objects.
    /// </summary>
    public ProjectLoadResult LoadProject(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found.", filePath);

        var warnings = new List<string>();

        using var stream  = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // ── Read all required entries ─────────────────────────────────────────
        var physicsConfig    = ReadJsonEntry<PhysicsConfigDTO>(archive,    "PhysicsConfig.json",    warnings) ?? new PhysicsConfigDTO();
        var simulationState  = ReadJsonEntry<SimulationStateDTO>(archive,  "SimulationState.json",  warnings) ?? new SimulationStateDTO();
        var sceneDto         = ReadJsonEntry<SceneDTO>(archive,            "Scene.json",            warnings) ?? new SceneDTO();
        var entities         = ReadJsonEntry<List<EntityDTO>>(archive,     "Entities.json",         warnings) ?? new List<EntityDTO>();
        var eventHistory     = ReadJsonEntry<List<SimulationEventDTO>>(archive, "EventHistory.json", warnings) ?? new List<SimulationEventDTO>();

        // Schema version check
        ValidateVersion(archive, warnings);

        // ── Reconstruct runtime objects ───────────────────────────────────────
        var runtimeConfig  = physicsConfig.ToRuntime();
        var manager        = ReconstructSimulationManager(runtimeConfig, simulationState, entities, warnings);
        var scene          = ReconstructScene(sceneDto, warnings);

        return new ProjectLoadResult
        {
            Scene    = scene,
            Manager  = manager,
            Warnings = warnings,
        };
    }

    // ── SimulationManager reconstruction ──────────────────────────────────────

    private static SimulationManager ReconstructSimulationManager(
        Physics.Types.PhysicsConfig config,
        SimulationStateDTO stateDto,
        List<EntityDTO> entityDtos,
        List<string> warnings)
    {
        var manager = new SimulationManager(config);

        // Apply saved time scale
        manager.Time.TimeScale = stateDto.TimeScale;
        if (stateDto.ExpansionEnabled)
        {
            manager.SpaceMetric.HubbleParameter  = stateDto.ExpansionRate;
            manager.SpaceMetric.ExpansionEnabled = true;
        }
        // Fast-forward simulation clock to saved time via AdvanceTime
        manager.Time.AdvanceTime(stateDto.SimulationTime);

        // Reconstruct entities
        foreach (var dto in entityDtos)
        {
            var entity = new Entity(dto.Id)
            {
                Tag      = dto.Tag,
                IsActive = dto.IsActive,
            };

            foreach (var cDto in dto.Components)
            {
                var component = MapDTOToComponent(cDto, warnings);
                if (component != null)
                    entity.AddComponent(component);
            }
            manager.AddEntity(entity);
        }

        return manager;
    }

    // ── Scene reconstruction ──────────────────────────────────────────────────

    private static AppScene ReconstructScene(SceneDTO dto, List<string> warnings)
    {
        var scene = new AppScene(
            dto.ProjectId == Guid.Empty ? Guid.NewGuid() : dto.ProjectId,
            dto.Name, dto.Author, dto.Description,
            dto.CreatedAt, dto.LastModifiedAt);

        // Build a lookup from id → node first, then wire parent-child
        var nodeMap = new Dictionary<Guid, SceneNode>();
        foreach (var nodeDto in dto.Nodes)
        {
            var node = new SceneNode(nodeDto.Id, nodeDto.Name, nodeDto.NodeType)
            {
                LinkedEntityId = nodeDto.LinkedEntityId,
            };
            nodeMap[node.Id] = node;
        }

        // Wire tree — nodes are stored depth-first so parents appear before children
        foreach (var nodeDto in dto.Nodes)
        {
            if (!nodeMap.TryGetValue(nodeDto.Id, out var node)) continue;

            Guid parentId = nodeDto.ParentId ?? Guid.Empty;
            try
            {
                scene.Graph.AddNode(parentId, node);
            }
            catch (Exception ex)
            {
                warnings.Add($"[WARN] Could not add node {nodeDto.Name} ({nodeDto.Id}): {ex.Message}");
            }
        }
        return scene;
    }

    // ── Component mapping ──────────────────────────────────────────────────────

    private static IComponent? MapDTOToComponent(ComponentDTO dto, List<string> warnings)
    {
        try
        {
            return dto switch
            {
                PhysicsComponentDTO pc => new PhysicsComponent(
                    pc.Mass,
                    new Vec3d(pc.PositionX, pc.PositionY, pc.PositionZ),
                    new Vec3d(pc.VelocityX, pc.VelocityY, pc.VelocityZ),
                    pc.Radius)
                {
                    Density      = pc.Density,
                    IsCollidable = pc.IsCollidable,
                    Acceleration = new Vec3d(pc.AccelerationX, pc.AccelerationY, pc.AccelerationZ),
                },
                _ => null // Unknown component type — silently skip (forward-compat)
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"[WARN] Failed to reconstruct component '{dto.ComponentType}': {ex.Message}");
            return null;
        }
    }

    // ── Version validation ─────────────────────────────────────────────────────

    private static void ValidateVersion(ZipArchive archive, List<string> warnings)
    {
        var entry = archive.GetEntry("ProjectMetadata.json");
        if (entry == null)
        {
            warnings.Add("[WARN] ProjectMetadata.json not found — version cannot be validated.");
            return;
        }

        using var stream = entry.Open();
        using var doc    = JsonDocument.Parse(stream);

        if (doc.RootElement.TryGetProperty("version", out var versionEl) ||
            doc.RootElement.TryGetProperty("Version", out versionEl))
        {
            var fileVersion = versionEl.GetString() ?? "0.0.0";
            if (!string.Equals(fileVersion, CurrentVersion, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"[INFO] File version '{fileVersion}' differs from serializer version '{CurrentVersion}'. Fields may differ.");
        }
    }

    // ── ZIP helpers ───────────────────────────────────────────────────────────

    private static T? ReadJsonEntry<T>(ZipArchive archive, string entryName, List<string> warnings)
    {
        var entry = archive.GetEntry(entryName);
        if (entry == null)
        {
            warnings.Add($"[WARN] Missing archive entry: {entryName}");
            return default;
        }

        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            warnings.Add($"[ERROR] Failed to deserialize {entryName}: {ex.Message}");
            return default;
        }
    }
}
