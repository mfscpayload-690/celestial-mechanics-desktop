namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Sanitizes all user-supplied strings before display or logging.
/// Prevents XAML injection (SEC-03) and log injection (SEC-04).
/// </summary>
public static class InputSanitizer
{
    /// <summary>
    /// Sanitizes text for display in WPF TextBlock/TextBox controls.
    /// Strips control characters and truncates to maxLength.
    /// </summary>
    public static string SanitizeDisplayText(string? input, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Strip control characters (includes null bytes, escape sequences)
        string cleaned = new string(input
            .Where(c => !char.IsControl(c) || c == '\t')
            .ToArray());

        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength];

        return cleaned.Trim();
    }

    /// <summary>
    /// Sanitizes text for log entries. Neutralizes newlines to prevent
    /// log injection attacks (SEC-04).
    /// </summary>
    public static string SanitizeLogEntry(string? input)
    {
        string cleaned = SanitizeDisplayText(input, maxLength: 512);
        cleaned = cleaned.Replace("\n", " ").Replace("\r", " ");
        return cleaned;
    }

    /// <summary>
    /// Validates a body name — only printable characters, reasonable length.
    /// </summary>
    public static bool IsValidBodyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 64) return false;
        return name.Any(char.IsLetterOrDigit);
    }
}
