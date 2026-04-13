using System.Windows;
using CelestialMechanics.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CelestialMechanics.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainWindowViewModel>();
    }
}
