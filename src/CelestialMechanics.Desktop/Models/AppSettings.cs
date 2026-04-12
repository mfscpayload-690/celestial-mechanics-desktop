namespace CelestialMechanics.Desktop.Models;

/// <summary>
/// Application settings persisted to %AppData%\CelestialMechanics\settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Whether the tutorial has been completed (show once on first launch).</summary>
    public bool TutorialCompleted { get; set; }

    /// <summary>Path to the last opened project folder.</summary>
    public string? LastOpenedProject { get; set; }

    /// <summary>Whether the window was maximized on last close.</summary>
    public bool WindowMaximized { get; set; } = true;
}
