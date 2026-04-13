using System.IO;
using System.Text.Json;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Services;

public sealed class ProjectService
{
    private const string RecentProjectsFileName = "recent_projects.json";
    private const string ProjectMetadataFileName = "project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ProjectInfo CreateProject(string name, string location)
    {
        string safeName = InputSanitizer.SanitizeDisplayText(name, 80);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new InvalidOperationException("Project name cannot be empty.");
        }

        string fullPath = Path.GetFullPath(location);
        Directory.CreateDirectory(fullPath);

        var now = DateTime.UtcNow;
        var project = new ProjectInfo
        {
            Name = safeName,
            Path = fullPath,
            CreatedAt = now,
            LastOpenedAt = now,
        };

        WriteProjectMetadata(project);
        AddOrUpdateRecent(project);
        return project;
    }

    public List<ProjectInfo> GetRecentProjects()
    {
        var recent = ReadRecentProjects();
        var dedup = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in recent)
        {
            if (string.IsNullOrWhiteSpace(project.Path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(project.Path);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            project.Path = fullPath;
            dedup[fullPath] = project;
        }

        var result = dedup.Values
            .OrderByDescending(p => p.LastOpenedAt)
            .ToList();

        WriteRecentProjects(result);
        return result;
    }

    public ProjectInfo? OpenProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        var project = ReadProjectMetadata(fullPath) ?? new ProjectInfo
        {
            Name = Path.GetFileName(fullPath),
            Path = fullPath,
            CreatedAt = Directory.GetCreationTimeUtc(fullPath),
        };

        project.Path = fullPath;
        project.LastOpenedAt = DateTime.UtcNow;

        WriteProjectMetadata(project);
        AddOrUpdateRecent(project);
        return project;
    }

    public static string GetDefaultProjectsRoot()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string root = Path.Combine(documents, "CelestialMechanics", "Projects");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetAppDataRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = Path.Combine(local, "CelestialMechanics");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetRecentProjectsFilePath() =>
        Path.Combine(GetAppDataRoot(), RecentProjectsFileName);

    private static string GetProjectMetadataPath(string projectPath) =>
        Path.Combine(projectPath, ProjectMetadataFileName);

    private static List<ProjectInfo> ReadRecentProjects()
    {
        var filePath = GetRecentProjectsFilePath();
        if (!File.Exists(filePath))
        {
            return new List<ProjectInfo>();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<ProjectInfo>>(json, JsonOptions)
                ?? new List<ProjectInfo>();
        }
        catch
        {
            return new List<ProjectInfo>();
        }
    }

    private static void WriteRecentProjects(List<ProjectInfo> projects)
    {
        try
        {
            var filePath = GetRecentProjectsFilePath();
            var json = JsonSerializer.Serialize(projects, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Non-critical persistence failure.
        }
    }

    private static ProjectInfo? ReadProjectMetadata(string projectPath)
    {
        var metadataPath = GetProjectMetadataPath(projectPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<ProjectInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteProjectMetadata(ProjectInfo project)
    {
        try
        {
            var metadataPath = GetProjectMetadataPath(project.Path);
            var json = JsonSerializer.Serialize(project, JsonOptions);
            File.WriteAllText(metadataPath, json);
        }
        catch
        {
            // Non-critical persistence failure.
        }
    }

    private static void AddOrUpdateRecent(ProjectInfo project)
    {
        var recent = ReadRecentProjects();
        recent.RemoveAll(p => string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, project);

        if (recent.Count > 50)
        {
            recent.RemoveRange(50, recent.Count - 50);
        }

        WriteRecentProjects(recent);
    }
}
