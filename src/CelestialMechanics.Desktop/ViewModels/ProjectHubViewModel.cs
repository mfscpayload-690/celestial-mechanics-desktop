using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Project Hub overlay.
/// Displayed after the tutorial (first-time) or welcome (returning user).
/// Lets users create a new project, open an existing one, or exit.
/// </summary>
public sealed partial class ProjectHubViewModel : ObservableObject
{
    private readonly ProjectService _projectService;

    /// <summary>Raised when a project is selected (created or opened).</summary>
    public event Action<ProjectInfo>? ProjectSelected;

    /// <summary>Raised when the user wants to exit the application.</summary>
    public event Action? ExitRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateProject))]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects))]
    private bool _isLoading;

    public ObservableCollection<ProjectInfo> RecentProjects { get; } = new();

    public bool CanCreateProject => !string.IsNullOrWhiteSpace(NewProjectName);

    public bool HasProjects => RecentProjects.Count > 0;

    public ProjectHubViewModel(ProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Loads the recent projects list from disk.</summary>
    public void RefreshProjects()
    {
        IsLoading = true;
        RecentProjects.Clear();

        try
        {
            var projects = _projectService.GetRecentProjects();
            foreach (var project in projects)
            {
                RecentProjects.Add(project);
            }
        }
        catch
        {
            // Non-critical — show empty list
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProjects));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private void CreateProject()
    {
        var safeName = string.Join("_", NewProjectName.Split(Path.GetInvalidFileNameChars()));
        var location = Path.Combine(ProjectService.GetDefaultProjectsRoot(), safeName);

        try
        {
            var project = _projectService.CreateProject(NewProjectName, location);
            ProjectSelected?.Invoke(project);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to create project.\n\n{ex.Message}",
                "Celestial Mechanics — Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenProject(ProjectInfo? project)
    {
        if (project == null) return;

        var opened = _projectService.OpenProject(project.Path);
        if (opened != null)
        {
            ProjectSelected?.Invoke(opened);
        }
        else
        {
            System.Windows.MessageBox.Show(
                "Could not open this project. The project files may be missing or corrupted.",
                "Celestial Mechanics — Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        var result = System.Windows.MessageBox.Show(
            "Exit Celestial Mechanics?\nAny unsaved simulation state will be lost.",
            "Celestial Mechanics — Exit",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.OK)
        {
            ExitRequested?.Invoke();
        }
    }

    /// <summary>Gets a human-readable relative time string.</summary>
    public static string GetRelativeTime(DateTime date)
    {
        var span = DateTime.Now - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
        return date.ToString("MMM d, yyyy");
    }
}
