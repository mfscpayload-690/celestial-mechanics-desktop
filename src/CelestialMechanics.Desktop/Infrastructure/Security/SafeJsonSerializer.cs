using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Safe JSON serializer using System.Text.Json with strict options (SEC-02).
/// Never uses BinaryFormatter or Newtonsoft TypeNameHandling.
/// </summary>
public static class SafeJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
        MaxDepth = 32,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = true,
    };

    /// <summary>
    /// Deserializes JSON with strict safety checks.
    /// Max file size: 10MB.
    /// </summary>
    public static T? Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        if (json.Length > 10_000_000)
            throw new SecurityException("File exceeds maximum allowed size (10MB).");

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("File is malformed or corrupted.", ex);
        }
    }

    /// <summary>
    /// Serializes an object to JSON with consistent formatting.
    /// </summary>
    public static string Serialize<T>(T obj)
        => JsonSerializer.Serialize(obj, Options);
}
