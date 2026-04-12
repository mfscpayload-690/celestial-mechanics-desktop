using System.IO;

namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Writes full crash details to a local log file (SEC-08).
/// Never exposes stack traces or internal paths to the user.
/// Log files are capped at 5MB and rotated.
/// </summary>
public static class CrashLogger
{
    public static string LogPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CelestialMechanics", "crash.log");

    public static string LastErrorId { get; private set; } = string.Empty;

    /// <summary>
    /// Writes a full exception to the crash log with a unique error reference ID.
    /// </summary>
    public static void WriteLog(Exception ex)
    {
        try
        {
            LastErrorId = Guid.NewGuid().ToString("N")[..8].ToUpper();

            string entry = $"""
                [{DateTime.UtcNow:O}] ERROR-{LastErrorId}
                Type: {ex.GetType().FullName}
                Message: {ex.Message}
                Stack:
                {ex.StackTrace}
                ---
                """;

            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            RotateIfNeeded();
            File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Logging must never throw — swallow silently
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        var info = new FileInfo(LogPath);
        if (info.Length > 5 * 1024 * 1024)
        {
            string archive = LogPath + ".old";
            File.Move(LogPath, archive, overwrite: true);
        }
    }
}
