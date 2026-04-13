using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class SimulationMenuViewModel : ObservableObject
{
    public event Action? NewProjectRequested;
    public event Action? FileRequested;
    public event Action? ProjectsRequested;
    public event Action? BackRequested;

    [RelayCommand]
    private void NewProject()
    {
        NewProjectRequested?.Invoke();
    }

    [RelayCommand]
    private void File()
    {
        FileRequested?.Invoke();
    }

    [RelayCommand]
    private void Projects()
    {
        ProjectsRequested?.Invoke();
    }

    [RelayCommand]
    private void Back()
    {
        BackRequested?.Invoke();
    }
}
