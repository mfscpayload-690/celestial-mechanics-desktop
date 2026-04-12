using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// Abstraction over ProjectService for DI registration.
/// Manages project creation, discovery, and persistence.
/// </summary>
public interface IProjectService
{
    /// <summary>Creates a new project with the given name at the specified location.</summary>
    ProjectInfo CreateProject(string name, string location);

    /// <summary>Returns recently opened/created projects, sorted by last modified.</summary>
    List<ProjectInfo> GetRecentProjects();

    /// <summary>Opens an existing project from disk.</summary>
    ProjectInfo? OpenProject(string path);

    /// <summary>Returns the default projects root directory.</summary>
    string GetDefaultProjectsRoot();
}
