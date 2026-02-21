using System.IO;
using System.IO.Compression;
using AppScene = CelestialMechanics.AppCore.Scene.Scene;
using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.AppCore.Serialization;
using CelestialMechanics.AppCore.Serialization.DTO;
using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.AppCore.Tests;

public sealed class SerializationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cesim_tests_{Guid.NewGuid():N}");

    public SerializationTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()        => Directory.Delete(_tempDir, recursive: true);

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    // ── PhysicsConfigDTO round-trip ───────────────────────────────────────────

    [Fact]
    public void PhysicsConfigDTO_RoundTrip_AllFieldsPreserved()
    {
        var original = new PhysicsConfig
        {
            TimeStep          = 0.005,
            SofteningEpsilon  = 2e-4,
            UseBarnesHut      = true,
            Theta             = 0.7,
            DeterministicMode = false,
            EnablePostNewtonian = true,
            MaxAccretionParticles = 3000,
        };

        var dto      = PhysicsConfigDTO.From(original);
        var restored = dto.ToRuntime();

        Assert.Equal(original.TimeStep,               restored.TimeStep,               precision: 15);
        Assert.Equal(original.SofteningEpsilon,        restored.SofteningEpsilon,        precision: 15);
        Assert.Equal(original.UseBarnesHut,            restored.UseBarnesHut);
        Assert.Equal(original.Theta,                   restored.Theta,                   precision: 15);
        Assert.Equal(original.DeterministicMode,       restored.DeterministicMode);
        Assert.Equal(original.EnablePostNewtonian,     restored.EnablePostNewtonian);
        Assert.Equal(original.MaxAccretionParticles,   restored.MaxAccretionParticles);
    }

    // ── EntityDTO round-trip ──────────────────────────────────────────────────

    [Fact]
    public void EntityDTO_RoundTrip_ComponentsPreserved()
    {
        var entityId = Guid.NewGuid();
        var dto = new EntityDTO
        {
            Id       = entityId,
            Tag      = "TestStar",
            IsActive = true,
            Components =
            {
                new PhysicsComponentDTO
                {
                    Mass      = 1.989e30,
                    Radius    = 6.96e8,
                    PositionX = 1.0, PositionY = 2.0, PositionZ = 3.0,
                    VelocityX = 0.1, VelocityY = 0.2, VelocityZ = 0.3,
                }
            }
        };

        Assert.Equal(entityId, dto.Id);
        Assert.Single(dto.Components);
        var pc = Assert.IsType<PhysicsComponentDTO>(dto.Components[0]);
        Assert.Equal(1.989e30, pc.Mass, precision: 5);
        Assert.Equal(1.0, pc.PositionX, precision: 15);
    }

    // ── Full Save → Load round-trip ───────────────────────────────────────────

    [Fact]
    public void ProjectSerializer_SaveLoad_SceneIntact()
    {
        // Build a simple scene + manager
        var manager = BuildTestManager();
        var scene   = new AppScene("UnitTestProject", "Tester");
        var entityId = manager.Entities.First().Id;
        scene.Graph.AddNode(Guid.Empty, new SceneNode("TestBody", NodeType.Entity) { LinkedEntityId = entityId });

        var path = TempFile("test.cesim");
        new ProjectSerializer().SaveProject(path, scene, manager);
        var result = new ProjectDeserializer().LoadProject(path);

        Assert.True(result.Success, string.Join("; ", result.Warnings));
        Assert.Equal("UnitTestProject", result.Scene.Name);
        Assert.Equal("Tester", result.Scene.Author);
        // Scene tree should contain the one node we added
        Assert.True(result.Scene.Graph.FlattenToEntityList().Any());
    }

    [Fact]
    public void ProjectSerializer_SaveLoad_SimulationStateIntact()
    {
        var manager = BuildTestManager();
        // Advance 5 steps to build up simulation time
        for (int i = 0; i < 5; i++) manager.Step(0.001);

        double savedTime = manager.Time.SimulationTime;
        var scene = new AppScene("StateTest");
        var path  = TempFile("state.cesim");

        new ProjectSerializer().SaveProject(path, scene, manager);
        var result = new ProjectDeserializer().LoadProject(path);

        Assert.True(result.Success, string.Join("; ", result.Warnings));
        Assert.Equal(savedTime, result.Manager.Time.SimulationTime, precision: 5);
    }

    // ── ZIP archive structure ─────────────────────────────────────────────────

    [Fact]
    public void CesimFile_IsZipArchive_ContainsExpectedEntries()
    {
        var manager = BuildTestManager();
        var scene   = new AppScene("ZipTest");
        var path    = TempFile("zip.cesim");

        new ProjectSerializer().SaveProject(path, scene, manager);

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(e => e.Name).ToHashSet();

        Assert.Contains("ProjectMetadata.json",  names);
        Assert.Contains("Scene.json",            names);
        Assert.Contains("PhysicsConfig.json",    names);
        Assert.Contains("SimulationState.json",  names);
        Assert.Contains("Entities.json",         names);
        Assert.Contains("EventHistory.json",     names);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimulationManager BuildTestManager()
    {
        var config = new PhysicsConfig { DeterministicMode = true, UseSoAPath = false };
        var manager = new SimulationManager(config);

        var entity = new Entity();
        entity.AddComponent(new PhysicsComponent(
            mass:     1.0,
            position: new Vec3d(0, 0, 0),
            velocity: new Vec3d(0.1, 0, 0),
            radius:   0.1));
        manager.AddEntity(entity);
        manager.FlushPendingChanges();
        return manager;
    }
}
