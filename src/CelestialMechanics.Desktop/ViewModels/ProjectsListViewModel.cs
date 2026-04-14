using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class ProjectsListViewModel : ObservableObject
{
    private readonly ProjectService _projectService;

    public event Action<ProjectInfo>? ProjectOpened;
    public event Action? CancelRequested;

    public ObservableCollection<ProjectInfo> Projects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedProjectCommand))]
    private ProjectInfo? _selectedProject;

    public ProjectsListViewModel(ProjectService projectService)
    {
        _projectService = projectService;
    }

    public void RefreshProjects()
    {
        Projects.Clear();
        foreach (var project in _projectService.GetRecentProjects())
        {
            Projects.Add(project);
        }
    }

    private bool CanOpenSelectedProject() => SelectedProject != null;

    [RelayCommand(CanExecute = nameof(CanOpenSelectedProject))]
    private void OpenSelectedProject()
    {
        if (SelectedProject == null)
        {
            return;
        }

        var opened = _projectService.OpenProject(SelectedProject.Path);
        if (opened != null)
        {
            ProjectOpened?.Invoke(opened);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }
}
