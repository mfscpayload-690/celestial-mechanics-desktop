using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CelestialMechanics.Desktop.ViewModels;

/// <summary>
/// ViewModel for the transient notification snackbar.
/// Auto-dismisses after the specified duration.
/// </summary>
public sealed partial class NotificationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    private System.Windows.Threading.DispatcherTimer? _dismissTimer;

    public void Show(string message, int durationMs = 3000)
    {
        Message = message;
        IsVisible = true;

        _dismissTimer?.Stop();
        _dismissTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _dismissTimer.Tick += (_, _) =>
        {
            IsVisible = false;
            _dismissTimer.Stop();
        };
        _dismissTimer.Start();
    }
}
