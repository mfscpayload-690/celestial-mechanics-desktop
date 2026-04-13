using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class ModeSelectionViewModel : ObservableObject
{
    public event Action? SimulationSelected;
    public event Action? ExitRequested;

    [RelayCommand]
    private void SelectSimulation()
    {
        SimulationSelected?.Invoke();
    }

    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke();
    }
}
