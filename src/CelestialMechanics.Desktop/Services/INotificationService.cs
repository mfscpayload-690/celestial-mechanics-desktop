namespace CelestialMechanics.Desktop.Services;

/// <summary>
/// Service for showing transient notification messages (snackbar-style).
/// </summary>
public interface INotificationService
{
    event Action<string, int>? NotificationRequested;
    void Show(string message, int durationMs = 3000);
}

/// <summary>
/// Default implementation — raises events consumed by NotificationViewModel.
/// </summary>
public class NotificationService : INotificationService
{
    public event Action<string, int>? NotificationRequested;

    public void Show(string message, int durationMs = 3000)
    {
        NotificationRequested?.Invoke(message, durationMs);
    }
}
