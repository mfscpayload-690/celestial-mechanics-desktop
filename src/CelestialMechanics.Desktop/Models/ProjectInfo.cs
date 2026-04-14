using System.Text.Json.Serialization;

namespace CelestialMechanics.Desktop.Models;

public sealed class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastOpenedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public DateTime LastModifiedDate => LastOpenedAt.ToLocalTime();
}
