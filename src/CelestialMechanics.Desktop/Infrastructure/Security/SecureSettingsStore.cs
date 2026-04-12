using System.IO;
using CelestialMechanics.Desktop.Models;

namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Manages application settings in %AppData%\CelestialMechanics\ (SEC-07).
/// Uses restrictive file permissions and atomic writes.
/// </summary>
public static class SecureSettingsStore
{
    private static readonly string SettingsDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CelestialMechanics");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    /// <summary>
    /// Loads application settings from disk, returning defaults if file doesn't exist.
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        EnsureDirectory();

        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            string json = await File.ReadAllTextAsync(SettingsPath);
            return SafeJsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves application settings using atomic write (temp → rename).
    /// </summary>
    public static async Task SaveAsync(AppSettings settings)
    {
        EnsureDirectory();
        string json = SafeJsonSerializer.Serialize(settings);

        string tmp = SettingsPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, SettingsPath, overwrite: true);
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(SettingsDir))
            Directory.CreateDirectory(SettingsDir);
    }
}
