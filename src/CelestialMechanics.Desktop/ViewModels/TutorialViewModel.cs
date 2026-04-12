using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CelestialMechanics.Desktop.Infrastructure.Security;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the 5-step tutorial overlay. Shows on first launch only.
/// </summary>
public sealed partial class TutorialViewModel : ObservableObject
{
    public event Action? TutorialCompleted;
    public event Action? TutorialSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepBody))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _currentStep = 1;

    public int TotalSteps => 4;
    public bool CanGoBack => CurrentStep > 1;
    public bool IsLastStep => CurrentStep == TotalSteps;
    public string NextButtonText => IsLastStep ? "CONTINUE →" : "NEXT →";

    public string StepTitle => CurrentStep switch
    {
        1 => "N-body Gravitational Simulation",
        2 => "Build Your System",
        3 => "Navigate 3D Space",
        4 => "Choose Your Integrator",
        _ => string.Empty
    };

    public string StepBody => CurrentStep switch
    {
        1 => "Celestial Mechanics simulates the gravitational interactions between multiple celestial bodies using Newtonian physics. Watch stars, planets, and black holes dance in real-time as forces play out at every step.",
        2 => "Use the Scene panel on the left to add celestial bodies. Choose from presets: Sun, Earth, Jupiter, Neutron Star, Black Hole, Asteroid — or define custom parameters for mass, velocity, and position.",
        3 => "Left-drag to orbit the camera. Right-drag to pan. Scroll to zoom. Click any body to inspect it in detail. Velocity arrows show the direction and magnitude of each body's motion.",
        4 => "Verlet is recommended — it's symplectic, meaning energy stays bounded over millions of steps. Euler is educational but unstable. RK4 gives short-term precision at higher cost. Watch energy drift in the Analysis panel.",
        _ => string.Empty
    };

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep)
        {
            CompleteTutorial();
        }
        else
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
            CurrentStep--;
    }

    [RelayCommand]
    private void Skip()
    {
        CompleteTutorial();
        TutorialSkipped?.Invoke();
    }

    private async void CompleteTutorial()
    {
        try
        {
            var settings = await SecureSettingsStore.LoadAsync();
            settings.TutorialCompleted = true;
            await SecureSettingsStore.SaveAsync(settings);
        }
        catch
        {
            // Non-critical — tutorial will show again next time
        }

        TutorialCompleted?.Invoke();
    }

    /// <summary>
    /// Checks whether the tutorial should be shown (first-time users only).
    /// </summary>
    public static async Task<bool> ShouldShowTutorial()
    {
        try
        {
            var settings = await SecureSettingsStore.LoadAsync();
            return !settings.TutorialCompleted;
        }
        catch
        {
            return true; // Show tutorial if we can't read settings
        }
    }
}
