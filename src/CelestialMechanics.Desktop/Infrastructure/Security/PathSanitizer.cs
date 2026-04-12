using System.IO;
using System.Security;

namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Prevents path traversal attacks (SEC-01) when loading/saving project files.
/// All file paths from user input or loaded files MUST pass through this class.
/// </summary>
public static class PathSanitizer
{
    private static readonly string[] AllowedRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.GetTempPath()
    };

    /// <summary>
    /// Validates and resolves a project file path. Rejects paths
    /// outside permitted directories and with disallowed extensions.
    /// </summary>
    public static string SanitizeProjectPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new SecurityException("Path cannot be empty.");

        string fullPath = Path.GetFullPath(rawPath);

        bool isAllowed = AllowedRoots.Any(root =>
            fullPath.StartsWith(
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
            throw new SecurityException(
                $"Path '{fullPath}' is outside permitted directories.");

        if (fullPath.Contains('\0'))
            throw new SecurityException("Path contains illegal characters.");

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is not (".celestial" or ".json" or ".png" or ".jpg"))
            throw new SecurityException($"File extension '{ext}' not permitted.");

        return fullPath;
    }

    /// <summary>
    /// Sanitizes filenames embedded inside loaded project files.
    /// Strips all path separators and control characters.
    /// </summary>
    public static string SanitizeEmbeddedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";

        var invalid = Path.GetInvalidFileNameChars()
                          .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' })
                          .ToHashSet();

        string sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Length > 128 ? sanitized[..128] : sanitized;
    }
}
