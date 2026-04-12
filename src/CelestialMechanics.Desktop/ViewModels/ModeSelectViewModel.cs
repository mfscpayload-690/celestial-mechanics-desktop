using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Mode Select screen (Screen 2).
/// Manages the three mode cards and top bar interactions.
/// </summary>
public sealed partial class ModeSelectViewModel : ObservableObject
{
    /// <summary>Raised when user clicks LAUNCH on the Simulation card.</summary>
    public event Action? SimulationLaunched;

    /// <summary>Raised when user confirms exit.</summary>
    public event Action? ExitConfirmed;

    [ObservableProperty]
    private string _copiedMessage = string.Empty;

    [ObservableProperty]
    private bool _showCopiedNotification;

    [RelayCommand]
    private void LaunchSimulation()
    {
        SimulationLaunched?.Invoke();
    }

    [RelayCommand]
    private void ExitApp()
    {
        var result = MessageBox.Show(
            "Exit Celestial Mechanics?\nAny unsaved simulation state will be lost.",
            "Celestial Mechanics — Exit",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.OK)
        {
            ExitConfirmed?.Invoke();
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        MessageBox.Show(
            "CELESTIAL MECHANICS DESKTOP\n" +
            "Version: 1.0.0-alpha\n" +
            "Build: Phase 2 (Physics Core)\n\n" +
            "A real-time N-body gravitational simulation engine with\n" +
            "3D visualization built with .NET 8 and OpenGL.\n\n" +
            "Features: N-body gravity solver (O(n²) pairwise Newtonian),\n" +
            "three integrators (Verlet/Euler/RK4), SoA physics pipeline,\n" +
            "multi-threaded force computation, real-time energy monitoring,\n" +
            "instanced OpenGL 3D renderer.\n\n" +
            "Contact: celestial1mechanics@gmail.com\n" +
            "GitHub: github.com/SharonMathew4/celestial-mechanics-desktop\n\n" +
            "© 2026 Celestial Mechanics Team. MIT License.",
            "About — Celestial Mechanics",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void CopyEmail()
    {
        const string email = "celestial1mechanics@gmail.com";
        Clipboard.SetText(email);
        ShowCopiedNotification = true;
        CopiedMessage = "📋 Email copied to clipboard";

        // Auto-dismiss after 2 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            ShowCopiedNotification = false;
            timer.Stop();
        };
        timer.Start();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        MessageBox.Show(
            "KEYBOARD SHORTCUTS\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
            "Space         Play / Pause simulation\n" +
            "→ (Right)    Step one frame\n" +
            "R                Reset simulation\n" +
            "Ctrl+S        Save project\n" +
            "Ctrl+O        Open project\n" +
            "Delete         Remove selected body\n\n" +
            "MOUSE CONTROLS\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
            "Left-drag     Orbit camera\n" +
            "Right-drag   Pan camera\n" +
            "Scroll           Zoom in/out\n" +
            "Left-click     Select body\n\n" +
            "QUICK START\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
            "1. Launch Simulation from the mode screen\n" +
            "2. Create or open a project\n" +
            "3. Add celestial bodies using the Add tool\n" +
            "4. Hit Space to start the simulation\n" +
            "5. Watch physics unfold in real-time!",
            "Help — Celestial Mechanics",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
