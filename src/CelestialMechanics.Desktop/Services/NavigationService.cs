using CelestialMechanics.Desktop.Core;
using CelestialMechanics.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Services;

public sealed class NavigationService
{
    private readonly AppState _appState;
    private readonly IServiceProvider _serviceProvider;

    public object CurrentView { get; private set; } = null!;

    public event Action<object>? ViewChanged;

    public NavigationService(AppState appState, IServiceProvider serviceProvider)
    {
        _appState = appState;
        _serviceProvider = serviceProvider;
        _appState.ModeChanged += OnModeChanged;

        OnModeChanged(AppMode.Home);
    }

    private void OnModeChanged(AppMode mode)
    {
        CurrentView = mode switch
        {
            AppMode.Home => _serviceProvider.GetRequiredService<HomeView>(),
            AppMode.Simulation => _serviceProvider.GetRequiredService<SimulationView>(),
            AppMode.Analysis => _serviceProvider.GetRequiredService<AnalysisView>(),
            _ => throw new NotImplementedException(),
        };

        ViewChanged?.Invoke(CurrentView);
    }

    public void NavigateToHome() => _appState.SetMode(AppMode.Home);

    public void NavigateToSimulation() => _appState.SetMode(AppMode.Simulation);

    public void NavigateToAnalysis() => _appState.SetMode(AppMode.Analysis);
}
