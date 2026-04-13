using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.Services;
using Microsoft.Win32;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class NewProjectViewModel : ObservableObject
{
    private readonly ProjectService _projectService;

    public event Action<ProjectInfo>? ProjectCreated;
    public event Action? CancelRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectLocation = ProjectService.GetDefaultProjectsRoot();

    public NewProjectViewModel(ProjectService projectService)
    {
        _projectService = projectService;
    }

    public void Reset()
    {
        ProjectName = string.Empty;
        ProjectLocation = ProjectService.GetDefaultProjectsRoot();
    }

    private bool CanCreateProject() =>
        !string.IsNullOrWhiteSpace(ProjectName) &&
        !string.IsNullOrWhiteSpace(ProjectLocation);

    [RelayCommand]
    private void BrowseLocation()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Project Directory",
            InitialDirectory = ProjectLocation,
        };

        if (dialog.ShowDialog() == true)
        {
            ProjectLocation = dialog.FolderName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private void CreateProject()
    {
        try
        {
            string safeName = PathSanitizer.SanitizeEmbeddedName(ProjectName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                throw new InvalidOperationException("Invalid project name.");
            }

            string fullPath = Path.Combine(ProjectLocation, safeName);
            var project = _projectService.CreateProject(
                InputSanitizer.SanitizeDisplayText(ProjectName, 80),
                fullPath);

            ProjectCreated?.Invoke(project);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Unable to create project.\n\n{ex.Message}",
                "Celestial Mechanics - Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }
}
