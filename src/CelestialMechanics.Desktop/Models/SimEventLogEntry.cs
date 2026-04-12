using CelestialMechanics.Desktop.Infrastructure.Security;

namespace CelestialMechanics.Desktop.Models;

/// <summary>
/// A single entry in the simulation event log.
/// Created exclusively through the factory method to ensure sanitization.
/// </summary>
public class SimEventLogEntry
{
    public Guid Id { get; init; }
    public DateTime Timestamp { get; init; }
    public SimEventType Type { get; init; }
    public string Message { get; init; } = string.Empty;
    public string TypeColor { get; init; } = "#7A8BA8";

    /// <summary>
    /// Factory method — the ONLY way to create log entries.
    /// Sanitizes message text to prevent log injection (SEC-04).
    /// </summary>
    public static SimEventLogEntry Create(SimEventType type, string rawMessage)
    {
        return new SimEventLogEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = type,
            Message = InputSanitizer.SanitizeLogEntry(rawMessage),
            TypeColor = GetColorForType(type)
        };
    }

    private static string GetColorForType(SimEventType type) => type switch
    {
        SimEventType.Info    => "#4FC3F7",
        SimEventType.Warning => "#FFB300",
        SimEventType.Error   => "#FF4444",
        SimEventType.Physics => "#00E5FF",
        SimEventType.Body    => "#FFB74D",
        _                    => "#7A8BA8"
    };
}

public enum SimEventType
{
    Info,
    Warning,
    Error,
    Physics,
    Body
}
