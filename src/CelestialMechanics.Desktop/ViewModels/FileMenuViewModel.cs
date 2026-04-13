using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestialMechanics.Desktop.ViewModels;

public sealed partial class FileMenuViewModel : ObservableObject
{
    public event Action? NewSimulationRequested;
    public event Action? OpenRequested;
    public event Action? SaveRequested;
    public event Action? ExitRequested;
    public event Action? BackRequested;

    [RelayCommand]
    private void NewSimulation()
    {
        NewSimulationRequested?.Invoke();
    }

    [RelayCommand]
    private void Open()
    {
        OpenRequested?.Invoke();
    }

    [RelayCommand]
    private void Save()
    {
        SaveRequested?.Invoke();
    }

    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke();
    }

    [RelayCommand]
    private void Back()
    {
        BackRequested?.Invoke();
    }
}
