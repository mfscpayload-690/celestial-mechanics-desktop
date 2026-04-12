using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Simulation IDE shell (SimulationWindow).
/// Phase 4 stub — provides playback commands, state display, and toolbar actions.
/// Full implementation (panel wiring, service integration) comes in Phase 5.
/// </summary>
public partial class SimulationIDEViewModel : ObservableObject
{
    // ── Engine state enum ──────────────────────────────────────
    public enum EngineState { Stopped, Paused, Running }

    [ObservableProperty]
    private string _title = "Simulation IDE";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineStateText))]
    [NotifyPropertyChangedFor(nameof(StatePillBrush))]
    [NotifyPropertyChangedFor(nameof(TimeScaleText))]
    private EngineState _currentState = EngineState.Paused;

    [ObservableProperty]
    private double _timeScale = 1.0;

    // ── Derived display properties ─────────────────────────────

    public string EngineStateText => CurrentState switch
    {
        EngineState.Running => "RUNNING",
        EngineState.Paused => "PAUSED",
        EngineState.Stopped => "STOPPED",
        _ => "UNKNOWN"
    };

    /// <summary>
    /// Returns the appropriate status brush for the engine state pill.
    /// StatusGreen for Running, StatusYellow for Paused, StatusRed for Stopped.
    /// </summary>
    public Brush StatePillBrush => CurrentState switch
    {
        EngineState.Running => _statusGreen ??= CreateBrush("#FF22C55E"),
        EngineState.Paused => _statusYellow ??= CreateBrush("#FFFFB300"),
        EngineState.Stopped => _statusRed ??= CreateBrush("#FFFF4444"),
        _ => _statusYellow ??= CreateBrush("#FFFFB300")
    };

    public string TimeScaleText => $"{TimeScale:F2}×";

    // Cached brushes (frozen for thread safety)
    private SolidColorBrush? _statusGreen;
    private SolidColorBrush? _statusYellow;
    private SolidColorBrush? _statusRed;

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ── Playback commands ──────────────────────────────────────

    [RelayCommand]
    private void TogglePlayPause()
    {
        CurrentState = CurrentState == EngineState.Running
            ? EngineState.Paused
            : EngineState.Running;
    }

    [RelayCommand]
    private void Step()
    {
        // Pause first if running, then step one frame
        CurrentState = EngineState.Paused;
        // Actual physics step will be wired in Phase 5
    }

    [RelayCommand]
    private void Reset()
    {
        CurrentState = EngineState.Stopped;
        TimeScale = 1.0;
        OnPropertyChanged(nameof(TimeScaleText));
    }

    // ── Stub commands for toolbar buttons ──────────────────────

    [RelayCommand]
    private void Save() { /* Phase 5 */ }

    [RelayCommand]
    private void Open() { /* Phase 5 */ }

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
            "Left-click     Select body",
            "Help — Celestial Mechanics",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
    private void Exit()
    {
        var result = MessageBox.Show(
            "Exit Celestial Mechanics?\nAny unsaved simulation state will be lost.",
            "Celestial Mechanics — Exit",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.OK)
        {
            Application.Current.Shutdown();
        }
    }
}
