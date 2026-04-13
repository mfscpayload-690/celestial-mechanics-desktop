using System.Windows;
using System.Windows.Controls;
using CelestialMechanics.Desktop.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

public partial class AnalysisView : UserControl
{
    public AnalysisView()
    {
        InitializeComponent();
    }

    private void OnBackToHomeClick(object sender, RoutedEventArgs e)
    {
        var appState = App.Services.GetRequiredService<AppState>();
        appState.SetMode(AppMode.Home);
    }
}
