using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CelestialMechanics.Desktop.Views;

/// <summary>
/// Welcome moment overlay shown when entering the Simulation IDE.
/// Plays a 3.5-second non-skippable animation, then auto-dismisses.
/// </summary>
public partial class WelcomeOverlay : UserControl
{
    /// <summary>Raised when the welcome animation completes.</summary>
    public event Action? AnimationCompleted;

    public WelcomeOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var sb = (Storyboard)Resources["WelcomeStoryboard"];
        sb.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            AnimationCompleted?.Invoke();
        };
        sb.Begin();
    }
}
